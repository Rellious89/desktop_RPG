using System;
using System.Collections.Generic;
using Common;
using NUnit.Framework;
using Recruitment;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RecruitmentEditor.Tests
{
    /// <summary>모집 방문 주기의 저장·시간 경계만 확인하는 격리 EditMode 시험.</summary>
    public sealed class RecruitmentCycleServiceTests
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
        private RecruitmentCycleService service;

        [SetUp]
        public void SetUp()
        {
            data = new SaveData();
            now = new Box<DateTime> { Value = CompleteAt };
            saves = new Box<int>();

            RecruitmentTypeDefinition type = Type(TypeId);
            access = Access(AccessId, TypeId, type, intervalSeconds: 3600);
            service = Service(AccessCatalog(access), TypeCatalog(type));
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
        public void 미건축과_건축_중은_Locked다()
        {
            Assert.AreEqual(RecruitmentCyclePhase.Locked, service.GetStatus(BuildingId).Phase);

            AddConstruction(CompleteAt.AddSeconds(1));
            Assert.AreEqual(RecruitmentCyclePhase.Locked, service.GetStatus(BuildingId).Phase);
            Assert.AreEqual(0, saves.Value);
            Assert.IsEmpty(data.recruitmentCycles);
        }

        [Test]
        public void 완료됐지만_기록이_없으면_조회만으로_NotInitialized다()
        {
            AddConstruction(CompleteAt);
            data.recruitmentCycles = null;

            RecruitmentCycleStatus status = service.GetStatus(BuildingId);

            Assert.AreEqual(RecruitmentCyclePhase.NotInitialized, status.Phase);
            Assert.AreEqual(AccessId, status.Access.Access.RecruitmentAccessId);
            Assert.IsNull(data.recruitmentCycles, "조회가 null 목록을 빈 목록으로 바꾸면 안 됩니다.");
            Assert.AreEqual(0, saves.Value);
        }

        [Test]
        public void 최초_생성은_건물_완성_시각과_방문_간격을_저장한다()
        {
            AddConstruction(CompleteAt);
            now.Value = CompleteAt.AddDays(5);

            RecruitmentCycleInitializeResult result = service.TryInitialize(BuildingId);

            Assert.AreEqual(RecruitmentCycleInitializeCode.Initialized, result.Code);
            Assert.AreEqual(1, saves.Value);
            Assert.AreEqual(1, data.recruitmentCycles.Count);
            Assert.AreSame(result.State, data.recruitmentCycles[0]);
            Assert.AreEqual(AccessId, result.State.recruitmentAccessId);
            Assert.AreEqual(SaveData.FormatTimestamp(CompleteAt), result.State.startedAtUtc,
                "앱 실행 시각이 아니라 여관 completeAtUtc에서 시작해야 합니다.");
            Assert.AreEqual(SaveData.FormatTimestamp(CompleteAt.AddSeconds(3600)), result.State.readyAtUtc);
        }

        [Test]
        public void 경계_직전은_Waiting이고_경계부터_Ready다()
        {
            AddConstruction(CompleteAt);
            AddCycle(CompleteAt, CompleteAt.AddSeconds(3600));

            now.Value = CompleteAt.AddSeconds(3600).AddTicks(-1);
            RecruitmentCycleStatus waiting = service.GetStatus(BuildingId);
            Assert.AreEqual(RecruitmentCyclePhase.Waiting, waiting.Phase);
            Assert.AreEqual(TimeSpan.FromTicks(1), waiting.Remaining);

            now.Value = CompleteAt.AddSeconds(3600);
            RecruitmentCycleStatus ready = service.GetStatus(BuildingId);
            Assert.AreEqual(RecruitmentCyclePhase.Ready, ready.Phase);
            Assert.AreEqual(TimeSpan.Zero, ready.Remaining);
        }

        [Test]
        public void 장시간_오프라인이면_즉시_Ready이고_반복_조회해도_재시작하지_않는다()
        {
            AddConstruction(CompleteAt);
            RecruitmentCycleSaveState cycle = AddCycle(CompleteAt, CompleteAt.AddSeconds(3600));
            string started = cycle.startedAtUtc;
            string ready = cycle.readyAtUtc;
            now.Value = CompleteAt.AddYears(2);

            for (int i = 0; i < 3; i++)
            {
                Assert.AreEqual(RecruitmentCyclePhase.Ready, service.GetStatus(BuildingId).Phase);
            }

            Assert.AreEqual(0, saves.Value);
            Assert.AreEqual(1, data.recruitmentCycles.Count);
            Assert.AreSame(cycle, data.recruitmentCycles[0]);
            Assert.AreEqual(started, cycle.startedAtUtc);
            Assert.AreEqual(ready, cycle.readyAtUtc);
        }

        [Test]
        public void 이미_주기가_있으면_중복_초기화하거나_저장하지_않는다()
        {
            AddConstruction(CompleteAt);
            RecruitmentCycleSaveState original = AddCycle(CompleteAt, CompleteAt.AddSeconds(12));

            RecruitmentCycleInitializeResult result = service.TryInitialize(BuildingId);

            Assert.AreEqual(RecruitmentCycleInitializeCode.AlreadyInitialized, result.Code);
            Assert.AreSame(original, result.State);
            Assert.AreEqual(1, data.recruitmentCycles.Count);
            Assert.AreEqual(0, saves.Value);
        }

        [Test]
        public void 기존_주기는_테이블_간격이_바뀌어도_readyAt을_바꾸지_않는다()
        {
            AddConstruction(CompleteAt);
            RecruitmentCycleSaveState cycle = AddCycle(CompleteAt, CompleteAt.AddSeconds(3600));
            SetInt(access, "arrivalIntervalSeconds", 5);
            now.Value = CompleteAt.AddSeconds(10);

            RecruitmentCycleStatus status = service.GetStatus(BuildingId);

            Assert.AreEqual(RecruitmentCyclePhase.Waiting, status.Phase);
            Assert.AreEqual(TimeSpan.FromSeconds(3590), status.Remaining);
            Assert.AreEqual(SaveData.FormatTimestamp(CompleteAt.AddSeconds(3600)), cycle.readyAtUtc);
            Assert.AreEqual(0, saves.Value);
        }

        [Test]
        public void 건물이나_주기의_시각이_손상되면_Unreadable이고_보정하지_않는다()
        {
            data.buildingConstructions.Add(new BuildingConstructionSaveState
            {
                buildingId = BuildingId,
                completeAtUtc = "broken-construction-time",
            });

            Assert.AreEqual(RecruitmentCyclePhase.Unreadable, service.GetStatus(BuildingId).Phase);
            Assert.AreEqual(RecruitmentCycleInitializeCode.Unreadable, service.TryInitialize(BuildingId).Code);
            Assert.IsEmpty(data.recruitmentCycles);

            data.buildingConstructions[0].completeAtUtc = SaveData.FormatTimestamp(CompleteAt);
            RecruitmentCycleSaveState damaged = AddCycle(CompleteAt, CompleteAt.AddSeconds(1));
            damaged.startedAtUtc = "broken-cycle-time";

            Assert.AreEqual(RecruitmentCyclePhase.Unreadable, service.GetStatus(BuildingId).Phase);
            Assert.AreEqual("broken-cycle-time", damaged.startedAtUtc);
            Assert.AreEqual(0, saves.Value);
        }

        [Test]
        public void 끊어진_Access는_Unreadable이고_모르는_주기를_보존한다()
        {
            AddConstruction(CompleteAt);
            RecruitmentCycleSaveState unknown = new RecruitmentCycleSaveState
            {
                recruitmentAccessId = "Removed_Access",
                startedAtUtc = "damaged-but-preserved",
                readyAtUtc = null,
            };
            data.recruitmentCycles.Add(unknown);
            service = Service(AccessCatalog(), TypeCatalog());

            Assert.AreEqual(RecruitmentCyclePhase.Unreadable, service.GetStatus(BuildingId).Phase);
            Assert.AreEqual(RecruitmentCycleInitializeCode.Unreadable, service.TryInitialize(BuildingId).Code);
            Assert.AreEqual(1, data.recruitmentCycles.Count);
            Assert.AreSame(unknown, data.recruitmentCycles[0]);
            Assert.AreEqual(0, saves.Value);
        }

        [Test]
        public void 저장_실패는_추가_항목과_메타데이터를_완전히_롤백한다()
        {
            AddConstruction(CompleteAt);
            List<RecruitmentCycleSaveState> originalList = data.recruitmentCycles;
            RecruitmentCycleSaveState unknown = new RecruitmentCycleSaveState
            {
                recruitmentAccessId = "Unknown",
                startedAtUtc = "keep",
                readyAtUtc = "keep",
            };
            originalList.Add(unknown);
            data.saveVersion = 1;
            data.saveRevision = 17;
            data.lastSavedAtUtc = "before";
            service = Service(AccessCatalog(access), TypeCatalog(access.RecruitmentType), () =>
            {
                saves.Value++;
                SaveData.MarkSaved(data, CompleteAt.AddDays(9));
                return false;
            });

            RecruitmentCycleInitializeResult result = service.TryInitialize(BuildingId);

            Assert.AreEqual(RecruitmentCycleInitializeCode.SaveFailed, result.Code);
            Assert.AreEqual(1, saves.Value);
            Assert.AreSame(originalList, data.recruitmentCycles);
            Assert.AreEqual(1, data.recruitmentCycles.Count);
            Assert.AreSame(unknown, data.recruitmentCycles[0]);
            Assert.AreEqual(1, data.saveVersion);
            Assert.AreEqual(17, data.saveRevision);
            Assert.AreEqual("before", data.lastSavedAtUtc);
        }

        [Test]
        public void null_목록에서_저장_실패하면_null까지_복구한다()
        {
            AddConstruction(CompleteAt);
            data.recruitmentCycles = null;
            service = Service(AccessCatalog(access), TypeCatalog(access.RecruitmentType), () =>
            {
                saves.Value++;
                return false;
            });

            Assert.AreEqual(
                RecruitmentCycleInitializeCode.SaveFailed, service.TryInitialize(BuildingId).Code);
            Assert.IsNull(data.recruitmentCycles);
            Assert.AreEqual(1, saves.Value);
        }

        [Test]
        public void Normalize는_null만_정리하고_모르는_ID와_손상_시각은_보존한다()
        {
            var unknown = new RecruitmentCycleSaveState
            {
                recruitmentAccessId = "Removed_Access",
                startedAtUtc = "bad-start",
                readyAtUtc = "bad-ready",
            };
            data.recruitmentCycles = new List<RecruitmentCycleSaveState> { null, unknown, null };

            SaveDataNormalizer.Normalize(data);

            Assert.AreEqual(1, data.recruitmentCycles.Count);
            Assert.AreSame(unknown, data.recruitmentCycles[0]);
            Assert.AreEqual("bad-start", unknown.startedAtUtc);
            Assert.AreEqual("bad-ready", unknown.readyAtUtc);

            data.recruitmentCycles = null;
            SaveDataNormalizer.Normalize(data);
            Assert.IsNotNull(data.recruitmentCycles);
            Assert.IsEmpty(data.recruitmentCycles);
        }

        [Test]
        public void 마이그레이션_사본은_모집_주기_목록과_항목을_깊게_복사한다()
        {
            RecruitmentCycleSaveState original = AddCycle(CompleteAt, CompleteAt.AddSeconds(3600));
            original.pendingCharacterId = "CatMage";
            List<RecruitmentCycleSaveState> originalList = data.recruitmentCycles;

            SaveMigrationResult result = SaveMigrationRunner.Default.Migrate(data, SaveData.CurrentSaveVersion);

            Assert.AreEqual(SaveMigrationOutcome.AlreadyCurrent, result.Outcome);
            Assert.AreNotSame(originalList, data.recruitmentCycles);
            Assert.AreNotSame(original, data.recruitmentCycles[0]);
            Assert.AreEqual(original.recruitmentAccessId, data.recruitmentCycles[0].recruitmentAccessId);
            Assert.AreEqual(original.startedAtUtc, data.recruitmentCycles[0].startedAtUtc);
            Assert.AreEqual(original.readyAtUtc, data.recruitmentCycles[0].readyAtUtc);
            Assert.AreEqual(original.pendingCharacterId, data.recruitmentCycles[0].pendingCharacterId);
            Assert.AreEqual(3, SaveData.CurrentSaveVersion);
        }

        private RecruitmentCycleService Service(
            RecruitmentAccessCatalog accesses,
            RecruitmentTypeCatalog types,
            Func<bool> save = null)
        {
            return new RecruitmentCycleService(
                () => data,
                save ?? (() => { saves.Value++; return true; }),
                () => now.Value,
                accesses,
                types);
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
            string accessId,
            string typeId,
            RecruitmentTypeDefinition type,
            int intervalSeconds)
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
            list.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
            {
                list.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }
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

        private sealed class Box<T>
        {
            public T Value;
        }
    }
}
