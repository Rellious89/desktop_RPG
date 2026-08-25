using System;
using System.Collections.Generic;
using Character;
using Common;
using Corruption;
using NUnit.Framework;
using Party;
using Recovery;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CorruptionEditor.Tests
{
    /// <summary>파일 저장소나 씬 없이 v6 정화 트랜잭션의 경계만 검증한다.</summary>
    public sealed class PurificationServiceTests
    {
        private readonly List<Object> created = new List<Object>();
        private readonly DateTime now = new DateTime(2026, 8, 24, 1, 0, 0, DateTimeKind.Utc);
        private SaveData data;
        private CharacterCatalog characters;
        private PurificationConfigCatalog configs;
        private int saves;
        private bool buildingComplete;

        [SetUp]
        public void SetUp()
        {
            data = new SaveData
            {
                characters = new List<CharacterSaveState>
                {
                    State("A", 10), State("B", 8), State("C", 7),
                },
                partyCharacterIds = new List<string> { "A", "B" },
                recoverySlots = new List<RecoverySlotSaveState>(),
                purificationSlots = new List<PurificationSlotSaveState>(),
            };
            characters = CreateCharacterCatalog(("A", 2), ("B", 1), ("C", 0));
            configs = CreatePurificationCatalog("prayer", 60, 2, 2, true);
            buildingComplete = true;
            saves = 0;
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < created.Count; i++) if (created[i] != null) Object.DestroyImmediate(created[i]);
            created.Clear();
        }

        [Test]
        public void Register_OwnedNonPartyCharacter_UsesRequestedFixedSlotAndSavesOnce()
        {
            PurificationResult result = Service().TryRegister("prayer", "C", 1);

            Assert.AreEqual(PurificationResultCode.Success, result.Code);
            Assert.AreEqual(1, saves);
            Assert.AreEqual(2, data.purificationSlots.Count);
            Assert.IsFalse(data.purificationSlots[0].HasCharacter);
            Assert.AreEqual("C", data.purificationSlots[1].characterId);
            Assert.AreEqual("prayer", data.purificationSlots[1].purificationTypeId);
        }

        [Test]
        public void Register_PartyMember_EmptiesOnlyOriginalSlotInTheSameSave()
        {
            Assert.AreEqual(PurificationResultCode.Success, Service().TryRegister("prayer", "A", 0).Code);

            CollectionAssert.AreEqual(new[] { string.Empty, "B" }, data.partyCharacterIds);
            Assert.AreEqual("A", data.purificationSlots[0].characterId);
            Assert.AreEqual(1, saves);
        }

        [Test]
        public void Register_SinglePartyRecoveryAndDuplicate_AreBlocked_ButOccupiedSlotIsReplaced()
        {
            data.partyCharacterIds = new List<string> { "A" };
            Assert.AreEqual(PurificationResultCode.MinimumPartySize, Service().TryRegister("prayer", "A", 0).Code);

            data.partyCharacterIds = new List<string> { "A", "B" };
            data.recoverySlots.Add(new RecoverySlotSaveState { characterId = "C" });
            Assert.AreEqual(PurificationResultCode.InRecovery, Service().TryRegister("prayer", "C", 0).Code);
            data.recoverySlots.Clear();
            data.purificationSlots.Add(new PurificationSlotSaveState { characterId = "B", purificationTypeId = "prayer" });
            Assert.AreEqual(PurificationResultCode.AlreadyInPurification, Service().TryRegister("prayer", "B", 1).Code);
            data.purificationSlots[0].lastCalculatedAtUtc = SaveData.FormatTimestamp(now.AddSeconds(-60));
            PurificationResult replaced = Service().TryRegister("prayer", "C", 0);
            Assert.AreEqual(PurificationResultCode.Success, replaced.Code);
            Assert.AreEqual("B", replaced.PreviousCharacterId);
            Assert.AreEqual("C", replaced.CharacterId);
            Assert.AreEqual("C", data.purificationSlots[0].characterId);
            Assert.AreEqual(1, saves);
        }

        [Test]
        public void MoveToParty_SettlesAndClearsPurificationInOneSave()
        {
            data.partyCharacterIds = new List<string> { "A", string.Empty };
            data.purificationSlots.Add(new PurificationSlotSaveState
            {
                characterId = "C", purificationTypeId = "prayer",
                lastCalculatedAtUtc = SaveData.FormatTimestamp(now.AddSeconds(-120)),
            });

            PurificationResult result = Service().TryMoveToParty("C", 1, 3);
            Assert.AreEqual(PurificationResultCode.Success, result.Code);
            CollectionAssert.AreEqual(new[] { "A", "C" }, data.partyCharacterIds);
            Assert.IsFalse(data.purificationSlots[0].HasCharacter);
            Assert.AreEqual(3d, data.characters[2].currentCorruption);
            Assert.AreEqual(1, saves);
        }

        [Test]
        public void Register_BlocksBuildingMissingInvalidConfigAndInvalidCharacter()
        {
            buildingComplete = false;
            Assert.AreEqual(PurificationResultCode.RequiredBuildingUnavailable, Service().TryRegister("prayer", "C", 0).Code);
            buildingComplete = true;
            Assert.AreEqual(PurificationResultCode.ConfigurationMissing, Service().TryRegister("none", "C", 0).Code);
            Assert.AreEqual(PurificationResultCode.InvalidCharacter, Service().TryRegister("prayer", "unknown", 0).Code);
            Assert.AreEqual(PurificationResultCode.InvalidSlot, Service().TryRegister("prayer", "C", 2).Code);
            Assert.AreEqual(0, saves);
        }

        [Test]
        public void Tick_SettlesWholeIntervalsPreservesRemainderAndAppliesBaseFloor()
        {
            data.purificationSlots.Add(new PurificationSlotSaveState
            {
                characterId = "A", purificationTypeId = "prayer",
                lastCalculatedAtUtc = SaveData.FormatTimestamp(now.AddSeconds(-130)),
                progressTicks = TimeSpan.FromSeconds(10).Ticks,
            });

            PurificationResult result = Service().Tick();
            Assert.AreEqual(PurificationResultCode.Success, result.Code);
            Assert.AreEqual(1, saves);
            Assert.AreEqual(6d, data.characters[0].currentCorruption);
            Assert.AreEqual(TimeSpan.FromSeconds(20).Ticks, data.purificationSlots[0].progressTicks);

            data.characters[0].currentCorruption = 3;
            data.purificationSlots[0].lastCalculatedAtUtc = SaveData.FormatTimestamp(now.AddMinutes(-10));
            Assert.AreEqual(PurificationResultCode.Success, Service().Tick().Code);
            Assert.AreEqual(2d, data.characters[0].currentCorruption);
            Assert.AreEqual(0, data.purificationSlots[0].progressTicks, "하한 도달 뒤의 초과 시간은 적립하지 않는다.");
        }

        [Test]
        public void Tick_InvalidAndFutureTimesResetBaselineWithoutFreePurification()
        {
            data.purificationSlots.Add(new PurificationSlotSaveState { characterId = "A", purificationTypeId = "prayer", lastCalculatedAtUtc = "bad", progressTicks = 42 });
            Assert.AreEqual(PurificationResultCode.Success, Service().Tick().Code);
            Assert.AreEqual(10d, data.characters[0].currentCorruption);
            Assert.AreEqual(0, data.purificationSlots[0].progressTicks);
            Assert.AreEqual(SaveData.FormatTimestamp(now), data.purificationSlots[0].lastCalculatedAtUtc);

            data.purificationSlots[0].lastCalculatedAtUtc = SaveData.FormatTimestamp(now.AddHours(1));
            data.purificationSlots[0].progressTicks = long.MaxValue;
            Assert.AreEqual(PurificationResultCode.Success, Service().Tick().Code);
            Assert.AreEqual(10d, data.characters[0].currentCorruption);
            Assert.AreEqual(0, data.purificationSlots[0].progressTicks);
        }

        [Test]
        public void Stop_SettlesThenClearsWithoutReturningToParty()
        {
            data.partyCharacterIds = new List<string> { string.Empty, "B" };
            data.purificationSlots.Add(new PurificationSlotSaveState
            {
                characterId = "A", purificationTypeId = "prayer",
                lastCalculatedAtUtc = SaveData.FormatTimestamp(now.AddSeconds(-120)),
            });

            Assert.AreEqual(PurificationResultCode.Success, Service().TryStop(0).Code);
            Assert.AreEqual(6d, data.characters[0].currentCorruption);
            Assert.IsFalse(data.purificationSlots[0].HasCharacter);
            CollectionAssert.AreEqual(new[] { string.Empty, "B" }, data.partyCharacterIds);
            Assert.AreEqual(1, saves);
        }

        [Test]
        public void SaveFailureAndException_RestoreSlotsPartyCorruptionAndMetadata()
        {
            data.saveRevision = 9;
            data.lastSavedAtUtc = "before";
            List<string> originalParty = data.partyCharacterIds;
            List<PurificationSlotSaveState> originalSlots = data.purificationSlots;
            PurificationResult failed = Service(() => { saves++; SaveData.MarkSaved(data, now); return false; })
                .TryRegister("prayer", "A", 0);
            Assert.AreEqual(PurificationResultCode.SaveFailed, failed.Code);
            Assert.AreSame(originalParty, data.partyCharacterIds);
            Assert.AreSame(originalSlots, data.purificationSlots);
            Assert.AreEqual(9, data.saveRevision);
            Assert.AreEqual("before", data.lastSavedAtUtc);

            data.purificationSlots.Add(new PurificationSlotSaveState { characterId = "A", purificationTypeId = "prayer", lastCalculatedAtUtc = SaveData.FormatTimestamp(now.AddMinutes(-1)) });
            double before = data.characters[0].currentCorruption;
            PurificationResult thrown = Service(() => { saves++; SaveData.MarkSaved(data, now); throw new InvalidOperationException(); }).TryStop(0);
            Assert.AreEqual(PurificationResultCode.SaveFailed, thrown.Code);
            Assert.AreEqual(before, data.characters[0].currentCorruption);
            Assert.AreEqual("A", data.purificationSlots[0].characterId);
            Assert.AreEqual(9, data.saveRevision);
        }

        [Test]
        public void PurifyingCharacter_IsBlockedFromPartyJoin()
        {
            data.purificationSlots.Add(new PurificationSlotSaveState { characterId = "C", purificationTypeId = "prayer" });
            PartyCompositionService party = new PartyCompositionService(() => data, () => true, CreatePartyCatalog());

            Assert.AreEqual(PartyCompositionCode.InPurification, party.TryJoin("C").Code);
        }

        private PurificationService Service(Func<bool> save = null)
        {
            return new PurificationService(() => data, save ?? (() => { saves++; SaveData.MarkSaved(data, now); return true; }),
                () => now, characters, configs, _ => buildingComplete);
        }

        private CharacterCatalog CreateCharacterCatalog(params (string id, int baseCorruption)[] values)
        {
            CharacterCatalog catalog = Create<CharacterCatalog>();
            SerializedProperty list = new SerializedObject(catalog).FindProperty("characters");
            list.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
            {
                CharacterDefinition definition = Create<CharacterDefinition>();
                SetString(definition, "characterId", values[i].id); SetInt(definition, "baseCorruption", values[i].baseCorruption);
                list.GetArrayElementAtIndex(i).objectReferenceValue = definition;
            }
            list.serializedObject.ApplyModifiedPropertiesWithoutUndo(); catalog.MarkDirty(); return catalog;
        }

        private PurificationConfigCatalog CreatePurificationCatalog(string id, int interval, int value, int slots, bool enabled)
        {
            PurificationConfigDefinition definition = Create<PurificationConfigDefinition>();
            SetString(definition, "purificationTypeId", id); SetString(definition, "requiredBuildingId", "church");
            SetInt(definition, "purificationIntervalSeconds", interval); SetInt(definition, "purificationValuePerInterval", value);
            SetInt(definition, "baseSlotCount", slots); SetBool(definition, "enabled", enabled);
            PurificationConfigCatalog catalog = Create<PurificationConfigCatalog>();
            SerializedObject serialized = new SerializedObject(catalog); SerializedProperty list = serialized.FindProperty("configs"); list.arraySize = 1;
            list.GetArrayElementAtIndex(0).objectReferenceValue = definition; serialized.ApplyModifiedPropertiesWithoutUndo(); catalog.MarkDirty(); return catalog;
        }

        private PartyConfigCatalog CreatePartyCatalog()
        {
            PartyConfigDefinition definition = Create<PartyConfigDefinition>(); SetString(definition, "configId", PartyConfigIds.Default); SetInt(definition, "baseCapacity", 3); SetBool(definition, "enabled", true);
            PartyConfigCatalog catalog = Create<PartyConfigCatalog>(); SerializedObject serialized = new SerializedObject(catalog); SerializedProperty list = serialized.FindProperty("configs"); list.arraySize = 1;
            list.GetArrayElementAtIndex(0).objectReferenceValue = definition; serialized.ApplyModifiedPropertiesWithoutUndo(); catalog.MarkDirty(); return catalog;
        }

        private T Create<T>() where T : ScriptableObject { T value = ScriptableObject.CreateInstance<T>(); created.Add(value); return value; }
        private static CharacterSaveState State(string id, double corruption) => new CharacterSaveState { characterId = id, currentCorruption = corruption };
        private static void SetString(Object owner, string field, string value) { SerializedObject serialized = new SerializedObject(owner); serialized.FindProperty(field).stringValue = value; serialized.ApplyModifiedPropertiesWithoutUndo(); }
        private static void SetInt(Object owner, string field, int value) { SerializedObject serialized = new SerializedObject(owner); serialized.FindProperty(field).intValue = value; serialized.ApplyModifiedPropertiesWithoutUndo(); }
        private static void SetBool(Object owner, string field, bool value) { SerializedObject serialized = new SerializedObject(owner); serialized.FindProperty(field).boolValue = value; serialized.ApplyModifiedPropertiesWithoutUndo(); }
    }
}
