using System;
using System.Collections.Generic;
using System.Reflection;
using Character;
using Common;
using CommonEditor.Save;
using NUnit.Framework;
using Recruitment;
using TableDataEditor;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RecruitmentEditor.Tests
{
    /// <summary>조건 판단과 영구화는 전부 메모리에서 검증한다. 실제 저장 파일·씬·네트워크는 쓰지 않는다.</summary>
    public sealed class RecruitmentUnlockServiceTests
    {
        private static readonly MethodInfo ValidateConditions = typeof(TableDataValidator).GetMethod(
            "ValidateCharacterUnlockConditions", BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly MethodInfo ValidateAcquisitions = typeof(TableDataValidator).GetMethod(
            "ValidateCharacterAcquisitions", BindingFlags.NonPublic | BindingFlags.Static);
        private readonly List<Object> created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object value in created) if (value != null) Object.DestroyImmediate(value);
            created.Clear();
        }

        [Test]
        public void Csv_정상_두행과_획득조건_참조를_받는다()
        {
            TableDataSnapshot snapshot = ValidateConditionsOnly(out TableDataDiagnosticLog log,
                Condition("unlock_barbarian", "1", "1", "MAX_OWNED_CHARACTER_LEVEL_AT_LEAST", "", "10"),
                Condition("unlock_elfarcher", "1", "1", "OWNED_CHARACTER_COUNT_AT_LEAST", "", "2"));
            Assert.AreEqual(0, log.ErrorCount);
            Assert.AreEqual(2, snapshot.CharacterUnlockConditions.Count);
            snapshot.CharactersById.Add("Barbarian", new CharacterRow { Id = "Barbarian", Enabled = true });
            var table = new CsvTable("CharacterAcquisition.csv", TableDataColumns.CharacterAcquisition,
                new List<CsvRecord> { new CsvRecord(2, Acquisition("1", "Barbarian", "unlock_barbarian")) });
            ValidateAcquisitions.Invoke(null, new object[] { table, snapshot, log });
            Assert.AreEqual(0, log.ErrorCount, string.Join("\n", log.Entries));
            Assert.AreEqual("unlock_barbarian", snapshot.CharacterAcquisitions[0].ConditionId);
        }

        [TestCase("MAX_OWNED_CHARACTER_LEVEL_AT_LEAST", "target", "10")]
        [TestCase("UNKNOWN", "", "10")]
        [TestCase("OWNED_CHARACTER_COUNT_AT_LEAST", "", "0")]
        [TestCase("OWNED_CHARACTER_COUNT_AT_LEAST", "", "-1")]
        [TestCase("OWNED_CHARACTER_COUNT_AT_LEAST", "", "1.5")]
        public void Csv_지원하지않는_계약은_거부한다(string type, string target, string required)
        {
            ValidateConditionsOnly(out TableDataDiagnosticLog log, Condition("unlock", "1", "1", type, target, required));
            Assert.Greater(log.ErrorCount, 0);
        }

        [Test]
        public void Csv_조건과엔트리_중복_및_없는_참조를_거부한다()
        {
            TableDataSnapshot snapshot = ValidateConditionsOnly(out TableDataDiagnosticLog log,
                Condition("unlock", "1", "1", "OWNED_CHARACTER_COUNT_AT_LEAST", "", "2"),
                Condition("unlock", "1", "2", "OWNED_CHARACTER_COUNT_AT_LEAST", "", "3"));
            Assert.Greater(log.ErrorCount, 0);
            snapshot.CharactersById.Add("Barbarian", new CharacterRow { Id = "Barbarian", Enabled = true });
            var table = new CsvTable("CharacterAcquisition.csv", TableDataColumns.CharacterAcquisition,
                new List<CsvRecord> { new CsvRecord(2, Acquisition("1", "Barbarian", "missing")) });
            ValidateAcquisitions.Invoke(null, new object[] { table, snapshot, log });
            Assert.Greater(log.ErrorCount, 1);
        }

        [Test]
        public void Evaluate_같은그룹은_AND_그룹간은_OR다()
        {
            var data = Data("CatKnight", 10);
            CharacterUnlockConditionDefinition and = ConditionAsset("and",
                Entry("1", "same", "MAX_OWNED_CHARACTER_LEVEL_AT_LEAST", 10),
                Entry("2", "same", "OWNED_CHARACTER_COUNT_AT_LEAST", 2));
            CharacterUnlockConditionDefinition or = ConditionAsset("or",
                Entry("1", "a", "OWNED_CHARACTER_COUNT_AT_LEAST", 2),
                Entry("2", "b", "MAX_OWNED_CHARACTER_LEVEL_AT_LEAST", 10));
            Assert.IsFalse(RecruitmentUnlockService.Evaluate(and, data.characters));
            Assert.IsTrue(RecruitmentUnlockService.Evaluate(or, data.characters));
        }

        [Test]
        public void 영구해금은_최초달성시_한번저장하고_조건후퇴후에도_유지한다()
        {
            SaveData data = Data("CatKnight", 10);
            var service = Service(data, out Box<int> saves, out _);
            Assert.IsTrue(service.TryPersistCurrentUnlocks());
            CollectionAssert.AreEqual(new[] { "Barbarian" }, data.unlockedRecruitmentCharacterIds);
            Assert.AreEqual(1, saves.Value);
            data.characters[0].level = 1;
            Assert.IsTrue(service.TryPersistCurrentUnlocks());
            Assert.IsTrue(service.IsUnlocked("Barbarian"));
            Assert.AreEqual(1, saves.Value, "이미 확정된 해금은 다시 저장하지 않는다.");
        }

        [Test]
        public void 다중해금은_한번만저장하고_실패와예외는_전체롤백한다()
        {
            SaveData data = Data("CatKnight", 10, "Barbarian", 1);
            var service = Service(data, out Box<int> saves, out _);
            Assert.IsTrue(service.TryPersistCurrentUnlocks());
            CollectionAssert.AreEqual(new[] { "Barbarian", "ElfArcher" }, data.unlockedRecruitmentCharacterIds);
            Assert.AreEqual(1, saves.Value);

            SaveData failed = Data("CatKnight", 10, "Barbarian", 1); failed.saveRevision = 7;
            var failedService = Service(failed, out _, out Func<bool> failSave, false);
            Assert.IsFalse(failedService.TryPersistCurrentUnlocks());
            Assert.AreEqual(0, failed.unlockedRecruitmentCharacterIds.Count); Assert.AreEqual(7, failed.saveRevision);

            SaveData thrown = Data("CatKnight", 10); string before = JsonUtility.ToJson(thrown);
            var throwing = new RecruitmentUnlockService(() => thrown, () => throw new InvalidOperationException(),
                Acquisitions(), Conditions());
            Assert.Throws<InvalidOperationException>(() => throwing.TryPersistCurrentUnlocks());
            Assert.AreEqual(before, JsonUtility.ToJson(thrown));
        }

        [Test]
        public void 진행순서_고양이기사에서_레벨10_바바리안_보유후_엘프궁수다()
        {
            SaveData data = Data("CatKnight", 1);
            var service = Service(data, out _, out _);
            RecruitmentPoolCatalog pool = Pool(); CharacterAcquisitionCatalog acquisitions = Acquisitions();
            Func<string, bool> unlocked = service.IsUnlocked;
            Assert.AreEqual(0, RecruitmentCandidateSelector.CollectEligible("Inn_Normal", pool, acquisitions,
                RecruitmentOwnership.Of("CatKnight"), unlocked).Count);
            data.characters[0].level = 10; Assert.IsTrue(service.TryPersistCurrentUnlocks());
            CollectionAssert.AreEqual(new[] { "Barbarian" }, Ids(RecruitmentCandidateSelector.CollectEligible(
                "Inn_Normal", pool, acquisitions, RecruitmentOwnership.Of("CatKnight"), unlocked)));
            data.characters.Add(new CharacterSaveState { characterId = "Barbarian", level = 1 });
            Assert.IsTrue(service.TryPersistCurrentUnlocks());
            CollectionAssert.AreEqual(new[] { "ElfArcher" }, Ids(RecruitmentCandidateSelector.CollectEligible(
                "Inn_Normal", pool, acquisitions, RecruitmentOwnership.Of("CatKnight", "Barbarian"), unlocked)));
        }

        [Test]
        public void V6에서V7_정규화_깊은복사와_Reset을_보존한다()
        {
            var old = Data("Barbarian", 10); old.saveVersion = 6; old.unlockedRecruitmentCharacterIds = null;
            SaveMigrationResult migration = SaveMigrationRunner.Default.Migrate(old, 6);
            Assert.IsTrue(migration.Succeeded); Assert.AreEqual(7, old.saveVersion); Assert.AreEqual(0, old.unlockedRecruitmentCharacterIds.Count);
            var unversioned = new SaveData { characters = new List<CharacterSaveState>() };
            Assert.IsTrue(SaveMigrationRunner.Default.Migrate(unversioned, 0).Succeeded,
                "기존 v0→…→v7 순차 경로에도 새 단계가 빠지면 안 된다.");
            Assert.AreEqual(7, unversioned.saveVersion);
            old.unlockedRecruitmentCharacterIds = new List<string> { "Barbarian", "", "Barbarian", "Unknown" };
            SaveDataNormalizer.Normalize(old); CollectionAssert.AreEqual(new[] { "Barbarian", "Unknown" }, old.unlockedRecruitmentCharacterIds);
            SaveMigrationRunner.Default.Migrate(old, 7); old.unlockedRecruitmentCharacterIds.Add("ElfArcher");
            Assert.IsFalse(old.unlockedRecruitmentCharacterIds.Contains("CatKnight"));
            SaveResetService.Apply(old, SaveResetTargets.Character, new[] { "Barbarian" }, new[] { "CatKnight" }, () => true);
            CollectionAssert.AreEqual(new[] { "Unknown", "ElfArcher" }, old.unlockedRecruitmentCharacterIds);
            SaveResetService.Apply(old, SaveResetTargets.All, new[] { "ElfArcher" }, new[] { "CatKnight" }, () => true);
            Assert.AreEqual(0, old.unlockedRecruitmentCharacterIds.Count);
        }

        private TableDataSnapshot ValidateConditionsOnly(out TableDataDiagnosticLog log, params string[][] rows)
        {
            Assert.IsNotNull(ValidateConditions); Assert.IsNotNull(ValidateAcquisitions);
            var records = new List<CsvRecord>(); for (int i = 0; i < rows.Length; i++) records.Add(new CsvRecord(i + 2, rows[i]));
            var snapshot = new TableDataSnapshot(); log = new TableDataDiagnosticLog();
            ValidateConditions.Invoke(null, new object[] { new CsvTable("CharacterUnlockCondition.csv", TableDataColumns.CharacterUnlockCondition, records), snapshot, log });
            return snapshot;
        }

        private static string[] Condition(string id, string entry, string group, string type, string target, string required) =>
            new[] { id, entry, group, type, target, required, "1", string.Empty };
        private static string[] Acquisition(string id, string character, string condition) =>
            new[] { id, character, RecruitmentAcquisitionTypes.RecruitOnly, "0", condition, "1", string.Empty };

        private RecruitmentUnlockService Service(SaveData data, out Box<int> saves, out Func<bool> action, bool succeeds = true)
        {
            var counter = new Box<int>(); saves = counter; action = () => { counter.Value++; return succeeds; };
            return new RecruitmentUnlockService(() => data, action, Acquisitions(), Conditions());
        }
        private CharacterAcquisitionCatalog Acquisitions()
        {
            var catalog = Create<CharacterAcquisitionCatalog>(); var values = new[] { AcquisitionAsset("Barbarian", "unlock_barbarian"), AcquisitionAsset("ElfArcher", "unlock_elfarcher") };
            SetObjects(catalog, "acquisitions", values); catalog.MarkDirty(); return catalog;
        }
        private CharacterUnlockConditionCatalog Conditions()
        {
            var catalog = Create<CharacterUnlockConditionCatalog>(); SetObjects(catalog, "conditions", new[] {
                ConditionAsset("unlock_barbarian", Entry("1", "1", "MAX_OWNED_CHARACTER_LEVEL_AT_LEAST", 10)),
                ConditionAsset("unlock_elfarcher", Entry("1", "1", "OWNED_CHARACTER_COUNT_AT_LEAST", 2)) }); catalog.MarkDirty(); return catalog;
        }
        private RecruitmentPoolCatalog Pool()
        {
            var pool = Create<RecruitmentPoolCatalog>(); SetObjects(pool, "entries", new[] { PoolEntry("1", "Barbarian", 80), PoolEntry("2", "ElfArcher", 60) }); pool.MarkDirty(); return pool;
        }
        private CharacterAcquisitionDefinition AcquisitionAsset(string id, string condition)
        {
            var value = Create<CharacterAcquisitionDefinition>(); var so = new SerializedObject(value);
            so.FindProperty("acquisitionId").stringValue = id; so.FindProperty("characterId").stringValue = id;
            so.FindProperty("acquisitionType").stringValue = RecruitmentAcquisitionTypes.RecruitOnly; so.FindProperty("conditionId").stringValue = condition; so.FindProperty("enabled").boolValue = true; so.ApplyModifiedPropertiesWithoutUndo(); return value;
        }
        private RecruitmentPoolEntryDefinition PoolEntry(string entry, string id, int weight)
        {
            var value = Create<RecruitmentPoolEntryDefinition>(); var so = new SerializedObject(value);
            so.FindProperty("recruitmentTypeId").stringValue = "Inn_Normal"; so.FindProperty("poolEntryId").stringValue = entry; so.FindProperty("characterId").stringValue = id; so.FindProperty("weight").intValue = weight; so.FindProperty("enabled").boolValue = true; so.ApplyModifiedPropertiesWithoutUndo(); return value;
        }
        private CharacterUnlockConditionDefinition ConditionAsset(string id, params (string Id, string Group, string Type, int Value)[] entries)
        {
            var value = Create<CharacterUnlockConditionDefinition>(); var so = new SerializedObject(value); so.FindProperty("conditionId").stringValue = id;
            SerializedProperty list = so.FindProperty("entries"); list.arraySize = entries.Length;
            for (int i = 0; i < entries.Length; i++) { var e = list.GetArrayElementAtIndex(i); e.FindPropertyRelative("entryId").stringValue = entries[i].Id; e.FindPropertyRelative("groupId").stringValue = entries[i].Group; e.FindPropertyRelative("conditionType").stringValue = entries[i].Type; e.FindPropertyRelative("requiredValue").intValue = entries[i].Value; e.FindPropertyRelative("enabled").boolValue = true; }
            so.ApplyModifiedPropertiesWithoutUndo(); return value;
        }
        private static (string Id, string Group, string Type, int Value) Entry(string id, string group, string type, int value) => (id, group, type, value);
        private T Create<T>() where T : ScriptableObject { var value = ScriptableObject.CreateInstance<T>(); created.Add(value); return value; }
        private static void SetObjects(ScriptableObject owner, string field, Object[] values) { var so = new SerializedObject(owner); var list = so.FindProperty(field); list.arraySize = values.Length; for (int i = 0; i < values.Length; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = values[i]; so.ApplyModifiedPropertiesWithoutUndo(); }
        private static SaveData Data(params object[] values) { var data = new SaveData { characters = new List<CharacterSaveState>() }; for (int i = 0; i < values.Length; i += 2) data.characters.Add(new CharacterSaveState { characterId = (string)values[i], level = (int)values[i + 1] }); return data; }
        private static string[] Ids(IReadOnlyList<RecruitmentPoolEntryDefinition> entries) { var ids = new string[entries.Count]; for (int i = 0; i < ids.Length; i++) ids[i] = entries[i].CharacterId; return ids; }
        private sealed class Box<T> { public T Value; }
    }
}
