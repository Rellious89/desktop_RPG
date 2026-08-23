using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Common;
using NUnit.Framework;
using Party;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace PartyEditor.Tests
{
    /// <summary>파티 편성 저장 경계만 확인하는 격리 EditMode 시험. 실제 저장 경로는 쓰지 않는다.</summary>
    public sealed class PartyCompositionServiceTests
    {
        private readonly List<Object> created = new List<Object>();
        private SaveData data;
        private PartyConfigCatalog catalog;
        private int saves;
        private static readonly DateTime Now = new DateTime(2026, 8, 24, 1, 2, 3, DateTimeKind.Utc);

        [SetUp]
        public void SetUp()
        {
            data = new SaveData
            {
                characters = new List<CharacterSaveState>
                {
                    Character("CatKnight", 8), Character("Barbarian", 13),
                    Character("ElfArcher", 21), Character("CatMage", 34),
                },
                partyCharacterIds = new List<string> { "CatKnight", "Barbarian" },
                recoverySlots = new List<RecoverySlotSaveState>(),
            };
            catalog = CreateCatalog(3, true);
            saves = 0;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object value in created) if (value != null) Object.DestroyImmediate(value);
            created.Clear();
        }

        [Test]
        public void Capacity_UsesDefaultPartyConfigWithoutHardcodedValue()
        {
            PartyCapacityResult result = Service().GetCapacity();

            Assert.AreEqual(PartyCompositionCode.Success, result.Code);
            Assert.AreEqual(3, result.Capacity);

            PartyConfigCatalog nonDefaultCapacity = CreateCatalog(5, true);
            Assert.AreEqual(5, Service(catalogOverride: nonDefaultCapacity).GetCapacity().Capacity);
        }

        [Test]
        public void Capacity_MissingDisabledAndInvalidConfigsAreBlocked()
        {
            Assert.AreEqual(PartyCompositionCode.ConfigurationMissing,
                new PartyCompositionService(() => data, () => true, null).TryJoin("ElfArcher").Code);

            PartyConfigCatalog disabled = CreateCatalog(3, false);
            Assert.AreEqual(PartyCompositionCode.ConfigurationInvalid,
                Service(catalogOverride: disabled).TryJoin("ElfArcher").Code);

            PartyConfigCatalog invalid = CreateCatalog(0, true);
            LogAssert.Expect(LogType.Error, new Regex("Base Capacity가 0라 목록에서 제외합니다"));
            Assert.AreEqual(PartyCompositionCode.ConfigurationMissing,
                Service(catalogOverride: invalid).TryJoin("ElfArcher").Code);
            Assert.AreEqual(0, saves);
        }

        [Test]
        public void Join_AddsOwnedCharacterOnce_AndLeavesCharacterStateUntouched()
        {
            CharacterSaveState original = data.characters[2];
            PartyCompositionResult result = Service().TryJoin("ElfArcher");

            Assert.AreEqual(PartyCompositionCode.Success, result.Code);
            Assert.AreEqual(1, saves);
            CollectionAssert.AreEqual(new[] { "CatKnight", "Barbarian", "ElfArcher" }, data.partyCharacterIds);
            Assert.AreSame(original, data.characters[2]);
            Assert.AreEqual(21, data.characters[2].level);
        }

        [Test]
        public void Join_BlocksInvalidUnownedDuplicateCapacityAndRecovery()
        {
            Assert.AreEqual(PartyCompositionCode.InvalidCharacterId, Service().TryJoin(string.Empty).Code);
            Assert.AreEqual(PartyCompositionCode.NotOwned, Service().TryJoin("Unknown").Code);
            Assert.AreEqual(PartyCompositionCode.AlreadyInParty, Service().TryJoin("CatKnight").Code);

            data.partyCharacterIds.Add("ElfArcher");
            Assert.AreEqual(PartyCompositionCode.CapacityReached, Service().TryJoin("CatMage").Code);

            data.partyCharacterIds.Remove("ElfArcher");
            data.recoverySlots.Add(new RecoverySlotSaveState { characterId = "ElfArcher" });
            Assert.AreEqual(PartyCompositionCode.InRecovery, Service().TryJoin("ElfArcher").Code);
            Assert.AreEqual(0, saves);
        }

        [Test]
        public void Join_AllowsFirstOwnedMemberWhenPartyIsEmpty()
        {
            data.partyCharacterIds.Clear();

            Assert.AreEqual(PartyCompositionCode.Success, Service().TryJoin("CatMage").Code);
            CollectionAssert.AreEqual(new[] { "CatMage" }, data.partyCharacterIds);
            Assert.AreEqual(1, saves);
        }

        [Test]
        public void Leave_EnforcesMinimumAndRecoveryThenRemovesNormally()
        {
            data.partyCharacterIds = new List<string> { "CatKnight" };
            Assert.AreEqual(PartyCompositionCode.MinimumPartySize, Service().TryLeave("CatKnight").Code);

            data.partyCharacterIds.Add("Barbarian");
            data.recoverySlots.Add(new RecoverySlotSaveState { characterId = "CatKnight" });
            Assert.AreEqual(PartyCompositionCode.InRecovery, Service().TryLeave("CatKnight").Code);

            data.recoverySlots.Clear();
            Assert.AreEqual(PartyCompositionCode.Success, Service().TryLeave("CatKnight").Code);
            CollectionAssert.AreEqual(new[] { "Barbarian" }, data.partyCharacterIds);
            Assert.AreEqual(1, saves);
        }

        [Test]
        public void Replace_AtCapacityIsAtomicAndPreservesOutgoingIndex()
        {
            data.partyCharacterIds.Add("ElfArcher");

            PartyCompositionResult result = Service().TryReplace("Barbarian", "CatMage");

            Assert.AreEqual(PartyCompositionCode.Success, result.Code);
            CollectionAssert.AreEqual(new[] { "CatKnight", "CatMage", "ElfArcher" }, data.partyCharacterIds);
            Assert.AreEqual(1, saves);
        }

        [Test]
        public void Replace_BlocksUnownedDuplicateRecoveryAndSameIdWithoutSaving()
        {
            Assert.AreEqual(PartyCompositionCode.NotOwned, Service().TryReplace("CatKnight", "Unknown").Code);
            Assert.AreEqual(PartyCompositionCode.AlreadyInParty, Service().TryReplace("CatKnight", "Barbarian").Code);
            Assert.AreEqual(PartyCompositionCode.NoChange, Service().TryReplace("CatKnight", "CatKnight").Code);

            data.recoverySlots.Add(new RecoverySlotSaveState { characterId = "CatKnight" });
            Assert.AreEqual(PartyCompositionCode.InRecovery, Service().TryReplace("CatKnight", "CatMage").Code);
            Assert.AreEqual(0, saves);
        }

        [Test]
        public void Move_ChangesOnlyOrderAndBlocksInvalidOrSameIndex()
        {
            data.partyCharacterIds.Add("ElfArcher");
            Assert.AreEqual(PartyCompositionCode.Success, Service().TryMove("ElfArcher", 0).Code);
            CollectionAssert.AreEqual(new[] { "ElfArcher", "CatKnight", "Barbarian" }, data.partyCharacterIds);
            Assert.AreEqual(PartyCompositionCode.InvalidIndex, Service().TryMove("CatKnight", 3).Code);
            Assert.AreEqual(PartyCompositionCode.NoChange, Service().TryMove("CatKnight", 1).Code);
            Assert.AreEqual(1, saves);
        }

        [Test]
        public void OverCapacityPartyIsNotTrimmedButJoinIsBlockedAndOtherChangesWork()
        {
            data.partyCharacterIds.Add("ElfArcher");
            data.partyCharacterIds.Add("CatMage");

            Assert.AreEqual(PartyCompositionCode.NotOwned, Service().TryJoin("Unknown").Code,
                "미보유 캐릭터는 정원 상태와 무관하게 합류할 수 없다.");
            data.characters.Add(Character("RabbitHealer", 55));
            Assert.AreEqual(PartyCompositionCode.CapacityReached, Service().TryJoin("RabbitHealer").Code);
            Assert.AreEqual(4, data.partyCharacterIds.Count);
            Assert.AreEqual(PartyCompositionCode.Success, Service().TryMove("CatMage", 0).Code);
            Assert.AreEqual(PartyCompositionCode.Success, Service().TryLeave("CatKnight").Code);
            Assert.AreEqual(3, data.partyCharacterIds.Count);
        }

        [Test]
        public void InvalidPartyDataAndQueriesDoNotMutateOrSave()
        {
            data.partyCharacterIds = new List<string> { "CatKnight", "CatKnight" };
            List<string> original = data.partyCharacterIds;

            Assert.AreEqual(PartyCompositionCode.InvalidPartyData, Service().TryJoin("ElfArcher").Code);
            Assert.AreEqual(PartyCompositionCode.Success, Service().GetCapacity().Code);
            Assert.AreSame(original, data.partyCharacterIds);
            Assert.AreEqual(0, saves);
        }

        [Test]
        public void SaveFailureAndExceptionRestorePartyAndMetadata()
        {
            data.saveRevision = 7;
            data.lastSavedAtUtc = "before";
            List<string> original = data.partyCharacterIds;

            PartyCompositionResult failed = Service(() =>
            {
                saves++; SaveData.MarkSaved(data, Now); return false;
            }).TryJoin("ElfArcher");
            Assert.AreEqual(PartyCompositionCode.SaveFailed, failed.Code);
            Assert.AreSame(original, data.partyCharacterIds);
            CollectionAssert.AreEqual(new[] { "CatKnight", "Barbarian" }, data.partyCharacterIds);
            Assert.AreEqual(7, data.saveRevision);
            Assert.AreEqual("before", data.lastSavedAtUtc);

            PartyCompositionResult thrown = Service(() =>
            {
                saves++; SaveData.MarkSaved(data, Now); throw new InvalidOperationException();
            }).TryJoin("ElfArcher");
            Assert.AreEqual(PartyCompositionCode.SaveFailed, thrown.Code);
            Assert.AreSame(original, data.partyCharacterIds);
            Assert.AreEqual(7, data.saveRevision);
            Assert.AreEqual(2, saves);
        }

        [Test]
        public void ReentrantCallIsBlockedWithoutSecondSave()
        {
            PartyCompositionService service = null;
            PartyCompositionCode nested = PartyCompositionCode.Success;
            service = Service(() =>
            {
                saves++;
                nested = service.TryJoin("CatMage").Code;
                return true;
            });

            Assert.AreEqual(PartyCompositionCode.Success, service.TryJoin("ElfArcher").Code);
            Assert.AreEqual(PartyCompositionCode.Reentrant, nested);
            Assert.AreEqual(1, saves);
        }

        private PartyCompositionService Service(Func<bool> save = null, PartyConfigCatalog catalogOverride = null)
        {
            Func<bool> action = save ?? (() => { saves++; SaveData.MarkSaved(data, Now); return true; });
            return new PartyCompositionService(() => data, action, catalogOverride ?? catalog);
        }

        private PartyConfigCatalog CreateCatalog(int capacity, bool enabled)
        {
            PartyConfigDefinition definition = Create<PartyConfigDefinition>();
            SetString(definition, "configId", PartyConfigIds.Default);
            SetInt(definition, "baseCapacity", capacity);
            SetBool(definition, "enabled", enabled);
            PartyConfigCatalog result = Create<PartyConfigCatalog>();
            SerializedObject serialized = new SerializedObject(result);
            SerializedProperty configs = serialized.FindProperty("configs");
            configs.arraySize = 1;
            configs.GetArrayElementAtIndex(0).objectReferenceValue = definition;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            result.MarkDirty();
            return result;
        }

        private T Create<T>() where T : ScriptableObject
        {
            T value = ScriptableObject.CreateInstance<T>();
            created.Add(value);
            return value;
        }

        private static CharacterSaveState Character(string id, int level)
        {
            return new CharacterSaveState { characterId = id, level = level, currentExp = level + 1, currentStamina = level + 2 };
        }

        private static void SetString(Object owner, string field, string value)
        {
            SerializedObject serialized = new SerializedObject(owner);
            serialized.FindProperty(field).stringValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetInt(Object owner, string field, int value)
        {
            SerializedObject serialized = new SerializedObject(owner);
            serialized.FindProperty(field).intValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetBool(Object owner, string field, bool value)
        {
            SerializedObject serialized = new SerializedObject(owner);
            serialized.FindProperty(field).boolValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
