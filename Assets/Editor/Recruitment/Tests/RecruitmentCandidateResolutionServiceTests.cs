using System;
using System.Collections.Generic;
using Character;
using Common;
using NUnit.Framework;
using Recruitment;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RecruitmentEditor.Tests
{
    /// <summary>Result 후보 처리의 저장 경계만 확인하는 격리 EditMode 시험.</summary>
    public sealed class RecruitmentCandidateResolutionServiceTests
    {
        private const string BuildingId = "1";
        private const string AccessId = "Inn_Normal_Access";
        private const string TypeId = "Inn_Normal";
        private static readonly DateTime CompleteAt = new DateTime(2026, 8, 20, 1, 2, 3, DateTimeKind.Utc);

        private readonly List<Object> created = new List<Object>();
        private SaveData data;
        private DateTime now;
        private int saves;
        private RecruitmentAccessCatalog accesses;
        private RecruitmentTypeCatalog types;
        private CharacterCatalog characters;
        private CharacterDefinition catMageDefinition;

        [SetUp]
        public void SetUp()
        {
            data = new SaveData();
            now = CompleteAt.AddHours(1);
            saves = 0;
            RecruitmentTypeDefinition type = Create<RecruitmentTypeDefinition>();
            SetString(type, "recruitmentTypeId", TypeId);
            SetBool(type, "enabled", true);
            RecruitmentAccessDefinition access = Create<RecruitmentAccessDefinition>();
            SetString(access, "recruitmentAccessId", AccessId);
            SetString(access, "recruitmentTypeId", TypeId);
            SetString(access, "sourceType", RecruitmentSourceTypes.Building);
            SetString(access, "sourceId", BuildingId);
            SetInt(access, "arrivalIntervalSeconds", 60);
            SetBool(access, "enabled", true);
            SetObject(access, "recruitmentType", type);
            accesses = Create<RecruitmentAccessCatalog>(); Fill(accesses, "accesses", access); accesses.MarkDirty();
            types = Create<RecruitmentTypeCatalog>(); Fill(types, "types", type); types.MarkDirty();
            characters = Create<CharacterCatalog>();
            catMageDefinition = Definition("CatMage", 17);
            Fill(characters, "characters", Definition("CatKnight"), catMageDefinition);
            characters.MarkDirty();
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < created.Count; i++) if (created[i] != null) Object.DestroyImmediate(created[i]);
            created.Clear();
        }

        [Test]
        public void Acquire_GrantsInitialState_ClearsPending_AndSavesOnce()
        {
            RecruitmentCycleSaveState cycle = AddCycle(now, "CatMage");
            CharacterSaveState retained = new CharacterSaveState { characterId = "CatKnight", level = 7 };
            data.characters.Add(retained);

            RecruitmentCandidateResolutionResult result = Service().TryAcquire(BuildingId);

            Assert.AreEqual(RecruitmentCandidateResolutionCode.Acquired, result.Code);
            Assert.AreEqual(1, saves);
            Assert.IsNull(cycle.pendingCharacterId);
            Assert.AreSame(retained, data.characters[0]);
            Assert.AreEqual(2, data.characters.Count);
            Assert.AreEqual("CatMage", data.characters[1].characterId);
            Assert.AreEqual(1, data.characters[1].level);
            Assert.AreEqual(0, data.characters[1].currentExp);
            Assert.AreNotEqual(30, catMageDefinition.MaxStamina);
            Assert.AreEqual(catMageDefinition.MaxStamina, data.characters[1].currentStamina);
            Assert.AreEqual(SaveData.CurrentSaveVersion, data.saveVersion);
        }

        [Test]
        public void Acquire_AlreadyOwned_PreservesPendingWithoutGrantOrSave()
        {
            RecruitmentCycleSaveState cycle = AddCycle(now, "CatMage");
            CharacterSaveState existing = new CharacterSaveState { characterId = "CatMage", level = 9 };
            data.characters.Add(existing);

            RecruitmentCandidateResolutionResult result = Service().TryAcquire(BuildingId);

            Assert.AreEqual(RecruitmentCandidateResolutionCode.AlreadyOwned, result.Code);
            Assert.AreEqual("CatMage", cycle.pendingCharacterId);
            Assert.AreEqual(1, data.characters.Count);
            Assert.AreSame(existing, data.characters[0]);
            Assert.AreEqual(0, saves);
        }

        [Test]
        public void Acquire_SaveFalseAndThrow_RollBackListPendingAndMetadata()
        {
            RecruitmentCycleSaveState cycle = AddCycle(now, "CatMage");
            data.saveRevision = 17;
            data.lastSavedAtUtc = "before";
            List<CharacterSaveState> original = data.characters;
            RecruitmentCandidateResolutionResult falseResult = Service(() =>
            {
                saves++; SaveData.MarkSaved(data, now); return false;
            }).TryAcquire(BuildingId);

            Assert.AreEqual(RecruitmentCandidateResolutionCode.SaveFailed, falseResult.Code);
            Assert.AreSame(original, data.characters);
            Assert.AreEqual(0, data.characters.Count);
            Assert.AreEqual("CatMage", cycle.pendingCharacterId);
            Assert.AreEqual(17, data.saveRevision);
            Assert.AreEqual("before", data.lastSavedAtUtc);

            Assert.Throws<InvalidOperationException>(() => Service(() =>
            {
                saves++; SaveData.MarkSaved(data, now); throw new InvalidOperationException("write failed");
            }).TryAcquire(BuildingId));
            Assert.AreSame(original, data.characters);
            Assert.AreEqual(0, data.characters.Count);
            Assert.AreEqual("CatMage", cycle.pendingCharacterId);
            Assert.AreEqual(17, data.saveRevision);
        }

        [Test]
        public void ReentrantResolution_IsBlockedAndDoesNotSaveTwice()
        {
            AddCycle(now, "CatMage");
            RecruitmentCandidateResolutionService service = null;
            RecruitmentCandidateResolutionCode nested = RecruitmentCandidateResolutionCode.Acquired;
            service = Service(() =>
            {
                saves++;
                nested = service.TryReturn(BuildingId).Code;
                return true;
            });

            Assert.AreEqual(RecruitmentCandidateResolutionCode.Acquired, service.TryAcquire(BuildingId).Code);
            Assert.AreEqual(RecruitmentCandidateResolutionCode.Reentrant, nested);
            Assert.AreEqual(1, saves);
        }

        [Test]
        public void Return_HalvesOnlyRemainingTime_PreservesStarted_AndNeverLengthensReady()
        {
            DateTime ready = now.AddMinutes(10);
            RecruitmentCycleSaveState cycle = AddCycle(ready, "CatMage");
            string started = cycle.startedAtUtc;

            RecruitmentCandidateResolutionResult waiting = Service().TryReturn(BuildingId);

            Assert.AreEqual(RecruitmentCandidateResolutionCode.Returned, waiting.Code);
            Assert.IsNull(cycle.pendingCharacterId);
            Assert.AreEqual(started, cycle.startedAtUtc);
            Assert.AreEqual(SaveData.FormatTimestamp(now.AddMinutes(5)), cycle.readyAtUtc);
            Assert.AreEqual(1, saves);

            cycle.pendingCharacterId = "CatMage";
            cycle.readyAtUtc = SaveData.FormatTimestamp(now.AddSeconds(-1));
            RecruitmentCandidateResolutionResult readyResult = Service().TryReturn(BuildingId);
            Assert.AreEqual(RecruitmentCandidateResolutionCode.Returned, readyResult.Code);
            Assert.AreEqual(SaveData.FormatTimestamp(now.AddSeconds(-1)), cycle.readyAtUtc);
            Assert.AreEqual(2, saves);
        }

        [Test]
        public void Return_SaveFalseAndThrow_RollBackPendingTimingAndMetadata()
        {
            RecruitmentCycleSaveState cycle = AddCycle(now.AddMinutes(10), "CatMage");
            string ready = cycle.readyAtUtc;
            data.saveRevision = 8;
            RecruitmentCandidateResolutionResult failed = Service(() =>
            {
                saves++; SaveData.MarkSaved(data, now); return false;
            }).TryReturn(BuildingId);

            Assert.AreEqual(RecruitmentCandidateResolutionCode.SaveFailed, failed.Code);
            Assert.AreEqual("CatMage", cycle.pendingCharacterId);
            Assert.AreEqual(ready, cycle.readyAtUtc);
            Assert.AreEqual(8, data.saveRevision);

            Assert.Throws<InvalidOperationException>(() => Service(() =>
            {
                saves++; SaveData.MarkSaved(data, now); throw new InvalidOperationException("write failed");
            }).TryReturn(BuildingId));
            Assert.AreEqual("CatMage", cycle.pendingCharacterId);
            Assert.AreEqual(ready, cycle.readyAtUtc);
            Assert.AreEqual(8, data.saveRevision);
        }

        [Test]
        public void MissingOrInvalidPendingCandidate_IsNeverConsumed()
        {
            RecruitmentCycleSaveState cycle = AddCycle(now, null);
            Assert.AreEqual(RecruitmentCandidateResolutionCode.NoPendingCandidate, Service().TryReturn(BuildingId).Code);
            cycle.pendingCharacterId = "RemovedCharacter";
            Assert.AreEqual(RecruitmentCandidateResolutionCode.InvalidCandidate, Service().TryAcquire(BuildingId).Code);
            Assert.AreEqual("RemovedCharacter", cycle.pendingCharacterId);
            Assert.AreEqual(0, saves);
        }

        private RecruitmentCandidateResolutionService Service(Func<bool> save = null)
        {
            Func<bool> action = save ?? (() => { saves++; SaveData.MarkSaved(data, now); return true; });
            RecruitmentCycleService cycles = new RecruitmentCycleService(() => data, action, () => now, accesses, types);
            return new RecruitmentCandidateResolutionService(() => data, action, () => now, cycles, characters);
        }

        private RecruitmentCycleSaveState AddCycle(DateTime readyAtUtc, string pending)
        {
            data.buildingConstructions.Add(new BuildingConstructionSaveState
            {
                buildingId = BuildingId,
                startedAtUtc = SaveData.FormatTimestamp(CompleteAt.AddHours(-1)),
                completeAtUtc = SaveData.FormatTimestamp(CompleteAt),
            });
            var state = new RecruitmentCycleSaveState
            {
                recruitmentAccessId = AccessId,
                startedAtUtc = SaveData.FormatTimestamp(CompleteAt),
                readyAtUtc = SaveData.FormatTimestamp(readyAtUtc),
                pendingCharacterId = pending,
            };
            data.recruitmentCycles.Add(state);
            return state;
        }

        private CharacterDefinition Definition(string id, int maxStamina = 5)
        {
            CharacterDefinition definition = Create<CharacterDefinition>();
            SetString(definition, "characterId", id);
            SetInt(definition, "maxStamina", maxStamina);
            return definition;
        }

        private T Create<T>() where T : ScriptableObject
        {
            T value = ScriptableObject.CreateInstance<T>();
            created.Add(value);
            return value;
        }

        private static void Fill(ScriptableObject owner, string field, params Object[] values)
        {
            var serialized = new SerializedObject(owner);
            SerializedProperty list = serialized.FindProperty(field);
            list.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetString(Object owner, string field, string value)
        {
            var serialized = new SerializedObject(owner);
            serialized.FindProperty(field).stringValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetInt(Object owner, string field, int value)
        {
            var serialized = new SerializedObject(owner);
            serialized.FindProperty(field).intValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetBool(Object owner, string field, bool value)
        {
            var serialized = new SerializedObject(owner);
            serialized.FindProperty(field).boolValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetObject(Object owner, string field, Object value)
        {
            var serialized = new SerializedObject(owner);
            serialized.FindProperty(field).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
