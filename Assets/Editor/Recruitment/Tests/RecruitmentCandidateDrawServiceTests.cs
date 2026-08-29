using System;
using System.Collections.Generic;
using System.Reflection;
using Character;
using Common;
using NUnit.Framework;
using Recruitment;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RecruitmentEditor.Tests
{
    /// <summary>READY 후보 보존과 다음 방문 주기를 하나의 저장으로 확정하는 격리 EditMode 시험.</summary>
    public sealed class RecruitmentCandidateDrawServiceTests
    {
        private const string BuildingId = "1";
        private const string AccessId = "Inn_Normal_Access";
        private const string TypeId = "Inn_Normal";
        private static readonly DateTime CompleteAt =
            new DateTime(2026, 8, 20, 1, 2, 3, DateTimeKind.Utc);

        private readonly List<Object> created = new List<Object>();
        private SaveData data;
        private Box<DateTime> now;
        private Box<int> saves;
        private RecruitmentAccessDefinition access;
        private RecruitmentAccessCatalog accesses;
        private RecruitmentTypeCatalog types;
        private RecruitmentPoolCatalog pool;
        private CharacterAcquisitionCatalog acquisitions;

        [SetUp]
        public void SetUp()
        {
            data = new SaveData();
            now = new Box<DateTime> { Value = CompleteAt.AddHours(1) };
            saves = new Box<int>();
            RecruitmentTypeDefinition type = Type(TypeId);
            access = Access(AccessId, TypeId, type, 60);
            accesses = AccessCatalog(access);
            types = TypeCatalog(type);
            pool = Pool(Entry("1", "CatKnight", 100), Entry("2", "CatMage", 60));
            acquisitions = Acquisitions("CatKnight", "CatMage");
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < created.Count; i++)
            {
                if (created[i] != null) Object.DestroyImmediate(created[i]);
            }
            created.Clear();
        }

        [Test]
        public void Ready가_아니면_추첨_난수_저장_모두_없다()
        {
            RecordingRandom random = new RecordingRandom(0);
            RecruitmentCandidateDrawService draw = Draw(random);

            Assert.AreEqual(RecruitmentCandidateDrawCode.Locked, draw.TryDraw(BuildingId).Code);
            AddConstruction(CompleteAt);
            AddCycle(CompleteAt, now.Value.AddSeconds(1));
            Assert.AreEqual(RecruitmentCandidateDrawCode.NotReady, draw.TryDraw(BuildingId).Code);

            Assert.AreEqual(0, random.Calls);
            Assert.AreEqual(0, saves.Value);
        }

        [Test]
        public void Ready에서_CatKnight_보유와_중복불가를_거른_후_후보와_다음주기를_한번에_저장한다()
        {
            AddReadyCycle();
            data.characters.Add(new CharacterSaveState { characterId = "CatKnight" });
            RecordingRandom random = new RecordingRandom(0);

            RecruitmentCandidateDrawResult result = Draw(random).TryDraw(BuildingId);

            Assert.AreEqual(RecruitmentCandidateDrawCode.Selected, result.Code);
            Assert.IsTrue(result.Success);
            Assert.AreEqual("CatMage", result.CharacterId,
                "현재 보유한 CatKnight는 allowDuplicateRecruitment=false라 후보에서 빠져야 합니다.");
            Assert.IsTrue(result.Selection.HasValue);
            Assert.AreEqual("CatMage", result.Selection.Value.CharacterId);
            Assert.AreSame(data.recruitmentCycles[0], result.State);
            Assert.AreEqual("CatMage", result.State.pendingCharacterId);
            Assert.AreEqual(SaveData.FormatTimestamp(now.Value), result.State.startedAtUtc);
            Assert.AreEqual(SaveData.FormatTimestamp(now.Value.AddSeconds(60)), result.State.readyAtUtc);
            Assert.AreEqual(1, random.Calls);
            Assert.AreEqual(1, saves.Value);
        }

        [Test]
        public void 재실행후에도_저장된_후보가_복원되고_다음주기가_Ready여도_재추첨하지_않는다()
        {
            RecruitmentCycleSaveState cycle = AddReadyCycle();
            cycle.pendingCharacterId = "Removed_Character";
            now.Value = now.Value.AddDays(1);
            RecordingRandom random = new RecordingRandom(0);

            // 새 서비스 인스턴스가 같은 SaveData를 읽는 것으로 재실행을 흉내 낸다.
            RecruitmentCandidateDrawService restarted = Draw(random);
            RecruitmentCandidateDrawResult result = restarted.TryDraw(BuildingId);

            Assert.AreEqual(RecruitmentCandidateDrawCode.PendingCandidateExists, result.Code);
            Assert.IsFalse(result.Success);
            Assert.AreEqual(string.Empty, result.CharacterId);
            Assert.IsNull(result.Selection);
            Assert.AreEqual("Removed_Character", cycle.pendingCharacterId);
            Assert.AreEqual(0, random.Calls);
            Assert.AreEqual(0, saves.Value);
        }

        [Test]
        public void 유효_후보가_없으면_Ready와_기존시각을_그대로_보존한다()
        {
            RecruitmentCycleSaveState cycle = AddReadyCycle();
            string started = cycle.startedAtUtc;
            string ready = cycle.readyAtUtc;
            string dataBefore = JsonUtility.ToJson(data);
            pool = Pool(Entry("1", "CatKnight", 0));
            RecordingRandom random = new RecordingRandom(0);

            RecruitmentCandidateDrawResult result = Draw(random).TryDraw(BuildingId);

            Assert.AreEqual(RecruitmentCandidateDrawCode.NoEligibleCandidate, result.Code);
            Assert.IsFalse(result.Success);
            Assert.IsNull(result.Selection, "후보가 없을 때 결과 카드를 열 근거가 되는 Selection을 만들지 않는다.");
            Assert.IsNull(result.State);
            Assert.AreEqual(started, cycle.startedAtUtc);
            Assert.AreEqual(ready, cycle.readyAtUtc);
            Assert.IsTrue(string.IsNullOrEmpty(cycle.pendingCharacterId));
            Assert.AreEqual(0, random.Calls);
            Assert.AreEqual(0, saves.Value);
            Assert.AreEqual(dataBefore, JsonUtility.ToJson(data),
                "후보가 없을 때는 pending, 모집 주기, 저장 문서 어떤 값도 바꾸지 않는다.");
        }

        [Test]
        public void NoEligibleCandidate_클릭방어선은_토스트를_정확히한번_요청한다()
        {
            RecruitmentCycleSaveState cycle = AddReadyCycle();
            pool = Pool(Entry("1", "CatKnight", 0));
            RecruitmentCandidateDrawResult result = Draw(new RecordingRandom(0)).TryDraw(BuildingId);
            int requests = 0;
            MethodInfo handler = typeof(RecruitmentUiController).GetMethod(
                "RequestNoEligibleCandidateToast", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(handler);
            handler.Invoke(null, new object[] { result, new LocalizedTextReference(),
                new Action<LocalizedTextReference>(_ => requests++) });

            Assert.AreEqual(RecruitmentCandidateDrawCode.NoEligibleCandidate, result.Code);
            Assert.AreEqual(1, requests);
            Assert.IsTrue(string.IsNullOrEmpty(cycle.pendingCharacterId));
            Assert.AreEqual(0, saves.Value);
        }

        [Test]
        public void 저장이_false면_후보_시각_메타데이터를_완전히_롤백한다()
        {
            RecruitmentCycleSaveState cycle = AddReadyCycle();
            cycle.pendingCharacterId = null;
            string started = cycle.startedAtUtc;
            string ready = cycle.readyAtUtc;
            data.saveVersion = 1;
            data.saveRevision = 17;
            data.lastSavedAtUtc = "before";

            RecruitmentCandidateDrawResult result = Draw(new RecordingRandom(0), () =>
            {
                saves.Value++;
                SaveData.MarkSaved(data, now.Value);
                return false;
            }).TryDraw(BuildingId);

            Assert.AreEqual(RecruitmentCandidateDrawCode.SaveFailed, result.Code);
            Assert.AreEqual(string.Empty, result.CharacterId);
            Assert.IsNull(result.Selection);
            Assert.IsTrue(string.IsNullOrEmpty(cycle.pendingCharacterId));
            Assert.AreEqual(started, cycle.startedAtUtc);
            Assert.AreEqual(ready, cycle.readyAtUtc);
            Assert.AreEqual(1, data.saveVersion);
            Assert.AreEqual(17, data.saveRevision);
            Assert.AreEqual("before", data.lastSavedAtUtc);
            Assert.AreEqual(1, saves.Value);
        }

        [Test]
        public void 저장_예외도_롤백한_뒤_그대로_전달한다()
        {
            RecruitmentCycleSaveState cycle = AddReadyCycle();
            string started = cycle.startedAtUtc;
            string ready = cycle.readyAtUtc;
            data.saveRevision = 4;

            Assert.Throws<InvalidOperationException>(() => Draw(new RecordingRandom(0), () =>
            {
                saves.Value++;
                SaveData.MarkSaved(data, now.Value);
                throw new InvalidOperationException("write failed");
            }).TryDraw(BuildingId));

            Assert.IsTrue(string.IsNullOrEmpty(cycle.pendingCharacterId));
            Assert.AreEqual(started, cycle.startedAtUtc);
            Assert.AreEqual(ready, cycle.readyAtUtc);
            Assert.AreEqual(4, data.saveRevision);
            Assert.AreEqual(1, saves.Value);
        }

        [Test]
        public void 손상된_주기와_끊어진_Access는_추첨하지_않는다()
        {
            AddConstruction(CompleteAt);
            RecruitmentCycleSaveState damaged = AddCycle(CompleteAt, now.Value);
            damaged.readyAtUtc = "broken";
            RecordingRandom random = new RecordingRandom(0);
            Assert.AreEqual(RecruitmentCandidateDrawCode.Unreadable, Draw(random).TryDraw(BuildingId).Code);

            damaged.readyAtUtc = SaveData.FormatTimestamp(now.Value);
            RecruitmentCandidateDrawService brokenAccess = Draw(
                random, null, AccessCatalog(), types);
            Assert.AreEqual(RecruitmentCandidateDrawCode.Unreadable, brokenAccess.TryDraw(BuildingId).Code);
            Assert.AreEqual(0, random.Calls);
            Assert.AreEqual(0, saves.Value);
        }

        [Test]
        public void null_저장문서는_NoSaveData이고_다시진입은_Reentrant다()
        {
            RecruitmentTypeDefinition type = Type(TypeId);
            RecruitmentAccessDefinition localAccess = Access(AccessId, TypeId, type, 60);
            RecruitmentCycleService nullCycles = new RecruitmentCycleService(
                () => null, () => true, () => now.Value, AccessCatalog(localAccess), TypeCatalog(type));
            RecruitmentCandidateDrawService nullDraw = new RecruitmentCandidateDrawService(
                () => null, () => true, () => now.Value, nullCycles,
                AccessCatalog(localAccess), TypeCatalog(type), pool, acquisitions, new RecordingRandom(0));
            Assert.AreEqual(RecruitmentCandidateDrawCode.NoSaveData, nullDraw.TryDraw(BuildingId).Code);

            AddReadyCycle();
            RecruitmentCandidateDrawService reentrant = null;
            RecruitmentCandidateDrawCode nested = RecruitmentCandidateDrawCode.Selected;
            reentrant = Draw(new RecordingRandom(0), () =>
            {
                nested = reentrant.TryDraw(BuildingId).Code;
                return true;
            });
            Assert.AreEqual(RecruitmentCandidateDrawCode.Selected, reentrant.TryDraw(BuildingId).Code);
            Assert.AreEqual(RecruitmentCandidateDrawCode.Reentrant, nested);
        }

        private RecruitmentCandidateDrawService Draw(
            IRecruitmentRandom random,
            Func<bool> save = null,
            RecruitmentAccessCatalog drawAccesses = null,
            RecruitmentTypeCatalog drawTypes = null)
        {
            RecruitmentCycleService cycles = new RecruitmentCycleService(
                () => data,
                save ?? (() => { saves.Value++; return true; }),
                () => now.Value,
                accesses,
                types);
            return new RecruitmentCandidateDrawService(
                () => data,
                save ?? (() => { saves.Value++; return true; }),
                () => now.Value,
                cycles,
                drawAccesses ?? accesses,
                drawTypes ?? types,
                pool,
                acquisitions,
                random);
        }

        private void AddConstruction(DateTime completeAtUtc)
        {
            data.buildingConstructions.Add(new BuildingConstructionSaveState
            {
                buildingId = BuildingId,
                startedAtUtc = SaveData.FormatTimestamp(completeAtUtc.AddHours(-1)),
                completeAtUtc = SaveData.FormatTimestamp(completeAtUtc),
            });
        }

        private RecruitmentCycleSaveState AddReadyCycle()
        {
            AddConstruction(CompleteAt);
            return AddCycle(CompleteAt, now.Value);
        }

        private RecruitmentCycleSaveState AddCycle(DateTime startedAtUtc, DateTime readyAtUtc)
        {
            var state = new RecruitmentCycleSaveState
            {
                recruitmentAccessId = AccessId,
                startedAtUtc = SaveData.FormatTimestamp(startedAtUtc),
                readyAtUtc = SaveData.FormatTimestamp(readyAtUtc),
            };
            data.recruitmentCycles.Add(state);
            return state;
        }

        private RecruitmentTypeDefinition Type(string id)
        {
            RecruitmentTypeDefinition definition = Create<RecruitmentTypeDefinition>();
            SetString(definition, "recruitmentTypeId", id);
            SetBool(definition, "enabled", true);
            return definition;
        }

        private RecruitmentAccessDefinition Access(
            string accessId, string typeId, RecruitmentTypeDefinition type, int intervalSeconds)
        {
            RecruitmentAccessDefinition definition = Create<RecruitmentAccessDefinition>();
            SetString(definition, "recruitmentAccessId", accessId);
            SetString(definition, "recruitmentTypeId", typeId);
            SetObject(definition, "recruitmentType", type);
            SetString(definition, "sourceType", RecruitmentSourceTypes.Building);
            SetString(definition, "sourceId", BuildingId);
            SetInt(definition, "arrivalIntervalSeconds", intervalSeconds);
            SetBool(definition, "enabled", true);
            return definition;
        }

        private RecruitmentPoolCatalog Pool(params RecruitmentPoolEntryDefinition[] entries)
        {
            RecruitmentPoolCatalog catalog = Create<RecruitmentPoolCatalog>();
            FillList(catalog, "entries", entries);
            catalog.MarkDirty();
            return catalog;
        }

        private RecruitmentPoolEntryDefinition Entry(string id, string characterId, int weight)
        {
            RecruitmentPoolEntryDefinition definition = Create<RecruitmentPoolEntryDefinition>();
            SetString(definition, "recruitmentTypeId", TypeId);
            SetString(definition, "poolEntryId", id);
            SetString(definition, "characterId", characterId);
            SetObject(definition, "character", Character(characterId));
            SetInt(definition, "weight", weight);
            SetBool(definition, "enabled", true);
            return definition;
        }

        private CharacterAcquisitionCatalog Acquisitions(params string[] characterIds)
        {
            var entries = new CharacterAcquisitionDefinition[characterIds.Length];
            for (int i = 0; i < characterIds.Length; i++) entries[i] = Acquisition(characterIds[i]);
            CharacterAcquisitionCatalog catalog = Create<CharacterAcquisitionCatalog>();
            FillList(catalog, "acquisitions", entries);
            catalog.MarkDirty();
            return catalog;
        }

        private CharacterAcquisitionDefinition Acquisition(string characterId)
        {
            CharacterAcquisitionDefinition definition = Create<CharacterAcquisitionDefinition>();
            SetString(definition, "acquisitionId", characterId);
            SetString(definition, "characterId", characterId);
            SetObject(definition, "character", Character(characterId));
            SetString(definition, "acquisitionType", RecruitmentAcquisitionTypes.RecruitOnly);
            SetBool(definition, "allowDuplicateRecruitment", false);
            SetString(definition, "conditionId", string.Empty);
            SetBool(definition, "enabled", true);
            return definition;
        }

        private RecruitmentAccessCatalog AccessCatalog(params RecruitmentAccessDefinition[] values)
        {
            RecruitmentAccessCatalog catalog = Create<RecruitmentAccessCatalog>();
            FillList(catalog, "accesses", values);
            catalog.MarkDirty();
            return catalog;
        }

        private RecruitmentTypeCatalog TypeCatalog(params RecruitmentTypeDefinition[] values)
        {
            RecruitmentTypeCatalog catalog = Create<RecruitmentTypeCatalog>();
            FillList(catalog, "types", values);
            catalog.MarkDirty();
            return catalog;
        }

        private CharacterDefinition Character(string characterId)
        {
            CharacterDefinition definition = Create<CharacterDefinition>();
            SetString(definition, "characterId", characterId);
            return definition;
        }

        private T Create<T>() where T : ScriptableObject
        {
            T value = ScriptableObject.CreateInstance<T>();
            created.Add(value);
            return value;
        }

        private static void FillList(ScriptableObject owner, string field, Object[] values)
        {
            var serialized = new SerializedObject(owner);
            SerializedProperty list = serialized.FindProperty(field);
            Assert.IsNotNull(list, $"{owner.GetType().Name}에 '{field}' 목록 칸이 없습니다.");
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

        private sealed class RecordingRandom : IRecruitmentRandom
        {
            private readonly int value;
            internal RecordingRandom(int value) { this.value = value; }
            internal int Calls { get; private set; }
            public int Next(int maxExclusive) { Calls++; return value; }
        }

        private sealed class Box<T>
        {
            internal T Value;
        }
    }
}
