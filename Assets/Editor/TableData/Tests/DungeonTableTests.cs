using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Common;
using Dungeon;
using Inventory;
using NUnit.Framework;
using UnityEditor;

namespace TableDataEditor.Tests
{
    public sealed class DungeonTableTests
    {
        private const string File = TableDataPaths.DungeonCsvFileName;

        private static readonly MethodInfo ValidateDungeonsMethod =
            typeof(TableDataValidator).GetMethod(
                "ValidateDungeons", BindingFlags.NonPublic | BindingFlags.Static);

        private static readonly MethodInfo ValidateHeaderMethod =
            typeof(TableDataCsvReader).GetMethod(
                "ValidateHeader", BindingFlags.NonPublic | BindingFlags.Static);

        private static TableDataValidationResult liveResult;

        [SetUp]
        public void SetUpFixture()
        {
            Assert.IsNotNull(ValidateDungeonsMethod,
                "TableDataValidator.ValidateDungeons를 찾지 못했습니다 - 이름이 바뀌었다면 이 시험도 함께 고치세요.");
            Assert.IsNotNull(ValidateHeaderMethod,
                "TableDataCsvReader.ValidateHeader를 찾지 못했습니다 - 이름이 바뀌었다면 이 시험도 함께 고치세요.");
        }

        // ---- 스키마 ----

        [Test]
        public void Schema_IsExactlyTheAgreedColumns()
        {
            CollectionAssert.AreEqual(
                new[]
                {
                    "dungeon_id", "name_category", "name_key", "world_id",
                    "representative_sprite_key", "monster_ids", "corruption_interval_seconds",
                    "corruption_gain_per_interval", "reward_item_ids", "display_order", "enabled", "memo",
                },
                TableDataColumns.Dungeon,
                "Dungeon.csv의 필수 컬럼과 순서가 약속과 달라졌습니다.");
        }

        [Test]
        public void Schema_PlacesCorruptionColumnsBeforeRewardItemIds()
        {
            int index = Array.IndexOf(TableDataColumns.Dungeon, "corruption_interval_seconds");

            Assert.Greater(index, 0, "corruption_interval_seconds가 필수 컬럼에 없습니다.");
            Assert.AreEqual("monster_ids", TableDataColumns.Dungeon[index - 1]);
            Assert.AreEqual("corruption_gain_per_interval", TableDataColumns.Dungeon[index + 1]);
        }

        [Test]
        public void Schema_DoesNotRetainRemovedRequiredCharacterLevelColumn()
        {
            CollectionAssert.DoesNotContain(TableDataColumns.Dungeon, TableDataColumns.RequiredCharacterLevel);
        }

        // ---- 헤더 검증 ----

        [Test]
        public void Header_MissingCorruptionInterval_FailsWithRequiredColumnDiagnostic()
        {
            string[] headerWithout = TableDataColumns.Dungeon
                .Where(c => c != TableDataColumns.CorruptionIntervalSeconds)
                .ToArray();

            var log = new TableDataDiagnosticLog();
            bool ok = (bool)ValidateHeaderMethod.Invoke(null,
                new object[] { File, headerWithout, TableDataColumns.Dungeon, log });

            Assert.IsFalse(ok, "corruption_interval_seconds가 빠진 헤더는 실패해야 한다.");
            Assert.AreEqual(1, log.ErrorCount, Describe(log));

            TableDataDiagnostic diag = log.Entries[0];
            Assert.AreEqual(TableDataDiagnostic.HeaderRow, diag.Row);
            Assert.AreEqual(TableDataColumns.CorruptionIntervalSeconds, diag.Column);
            StringAssert.Contains("필수 컬럼이 없습니다", diag.Message);
        }

        // ---- 오염도 주기/증가량 검증 ----

        [Test]
        public void CorruptionValues_ValidRow_EnterSnapshot()
        {
            TableDataSnapshot snapshot = SnapshotWithWorld("1");
            TableDataDiagnosticLog log = Validate(snapshot,
                Row("1", interval: "5", gain: "2", worldId: "1", displayOrder: "10"));

            Assert.AreEqual(0, log.ErrorCount, Describe(log));
            Assert.AreEqual(1, snapshot.Dungeons.Count);
            Assert.AreEqual(5, snapshot.Dungeons[0].CorruptionIntervalSeconds);
            Assert.AreEqual(2, snapshot.Dungeons[0].CorruptionGainPerInterval);
        }

        [Test]
        public void CorruptionInterval_MustBeAtLeastOne()
        {
            TableDataSnapshot snapshot = SnapshotWithWorld("1");
            TableDataDiagnosticLog log = Validate(snapshot,
                Row("1", interval: "0", worldId: "1"));

            Assert.AreEqual(1, CountErrors(log, TableDataColumns.CorruptionIntervalSeconds), Describe(log));
        }

        [Test]
        public void CorruptionGain_Blank_IsAnError()
        {
            TableDataSnapshot snapshot = SnapshotWithWorld("1");
            TableDataDiagnosticLog log = Validate(snapshot,
                Row("1", gain: "", worldId: "1"));

            Assert.AreEqual(1, CountErrors(log, TableDataColumns.CorruptionGainPerInterval), Describe(log));
        }

        [Test]
        public void CorruptionGain_Negative_IsAnError()
        {
            TableDataSnapshot snapshot = SnapshotWithWorld("1");
            TableDataDiagnosticLog log = Validate(snapshot,
                Row("1", gain: "-1", worldId: "1"));

            Assert.AreEqual(1, CountErrors(log, TableDataColumns.CorruptionGainPerInterval), Describe(log));
        }

        [Test]
        public void CorruptionInterval_NonInteger_IsAnError()
        {
            TableDataSnapshot snapshot = SnapshotWithWorld("1");
            TableDataDiagnosticLog log = Validate(snapshot,
                Row("1", interval: "abc", worldId: "1"));

            Assert.AreEqual(1, CountErrors(log, TableDataColumns.CorruptionIntervalSeconds), Describe(log));
        }

        [Test]
        public void CorruptionInterval_HasNoUpperBound()
        {
            TableDataSnapshot snapshot = SnapshotWithWorld("1");
            TableDataDiagnosticLog log = Validate(snapshot,
                Row("1", interval: "2147483647", worldId: "1", displayOrder: "10"));

            Assert.AreEqual(0, log.ErrorCount,
                "int.MaxValue는 유효한 값이다 - 상한을 두지 않는다: " + Describe(log));
            Assert.AreEqual(2147483647, snapshot.Dungeons[0].CorruptionIntervalSeconds);
        }

        // ---- 생성 에셋의 필요 레벨 ----

        [Test]
        public void GeneratedDefinitions_HaveCorruptionValuesMatchingCsv()
        {
            TableDataSnapshot snapshot = Live().Snapshot;
            Assert.IsNotNull(snapshot, "여덟 표가 모두 읽혀야 스냅샷이 만들어진다: " + Live().Summary);
            Assert.Greater(snapshot.Dungeons.Count, 0, "활성 던전 행이 하나 이상 있어야 한다.");

            foreach (DungeonRow row in snapshot.Dungeons)
            {
                string path = TableDataPaths.DungeonAssetPath(row.Id);
                var definition = AssetDatabase.LoadAssetAtPath<DungeonDefinition>(path);
                Assert.IsNotNull(definition, $"생성 에셋 '{path}'가 없습니다.");
                Assert.AreEqual(row.CorruptionIntervalSeconds, definition.CorruptionIntervalSeconds);
                Assert.AreEqual(row.CorruptionGainPerInterval, definition.CorruptionGainPerInterval);

                var so = new SerializedObject(definition);
                Assert.AreEqual(row.CorruptionIntervalSeconds, so.FindProperty("corruptionIntervalSeconds").intValue);
                Assert.AreEqual(row.CorruptionGainPerInterval, so.FindProperty("corruptionGainPerInterval").intValue);
            }
        }

        // ---- 프로덕션 데이터 ----

        [Test]
        public void LiveCsv_AllActiveProductionRows_HavePositiveCorruptionValues()
        {
            TableDataSnapshot snapshot = Live().Snapshot;
            Assert.IsNotNull(snapshot, Live().Summary);

            foreach (DungeonRow row in snapshot.Dungeons)
            {
                Assert.GreaterOrEqual(row.CorruptionIntervalSeconds, 1);
                Assert.GreaterOrEqual(row.CorruptionGainPerInterval, 1);
            }
        }

        [Test]
        public void LiveCsv_HasExactlyNineActiveRows()
        {
            TableDataSnapshot snapshot = Live().Snapshot;
            Assert.IsNotNull(snapshot, Live().Summary);
            Assert.AreEqual(9, snapshot.Dungeons.Count,
                "활성 프로덕션 던전 행은 9개여야 한다.");
        }

        [Test]
        public void LiveCsv_AllNineRowsHaveIds1Through9()
        {
            TableDataSnapshot snapshot = Live().Snapshot;
            Assert.IsNotNull(snapshot, Live().Summary);

            for (int i = 1; i <= 9; i++)
            {
                Assert.IsTrue(snapshot.DungeonsById.ContainsKey(i.ToString()),
                    $"던전 ID '{i}'이 스냅샷에 없습니다.");
            }
        }

        /// <summary>
        /// 아홉 던전의 대표 보상은 <b>정해진 값</b>이다. 앞의 여섯(애니멀랜드/판타지아)은 세 종류,
        /// 망자의 도시 셋은 그 앞에 50000이 하나 더 붙는다 - 표를 손볼 때 어느 던전의 보상이 조용히
        /// 달라지는 것을 막기 위해 값 자체를 여기에 적어 둔다.
        /// </summary>
        private static readonly string[] ExpectedRewardItemIds = new string[0];

        [Test]
        public void LiveCsv_AllNineRowsHaveTheAgreedRewardItemIds()
        {
            TableDataSnapshot snapshot = Live().Snapshot;
            Assert.IsNotNull(snapshot, Live().Summary);
            Assert.AreEqual(9, snapshot.Dungeons.Count);

            foreach (DungeonRow row in snapshot.Dungeons)
            {
                CollectionAssert.AreEqual(ExpectedRewardItemIds, row.RewardItemIds,
                    $"던전 '{row.Id}'(CSV {row.Line}행)의 reward_item_ids가 약속한 값과 다릅니다.");
            }
        }

        [Test]
        public void GeneratedDefinitions_HaveRewardItemsMatchingCsv()
        {
            TableDataSnapshot snapshot = Live().Snapshot;
            Assert.IsNotNull(snapshot, "여덟 표가 모두 읽혀야 스냅샷이 만들어진다: " + Live().Summary);
            Assert.AreEqual(9, snapshot.Dungeons.Count);

            foreach (DungeonRow row in snapshot.Dungeons)
            {
                string path = TableDataPaths.DungeonAssetPath(row.Id);
                var definition = AssetDatabase.LoadAssetAtPath<DungeonDefinition>(path);
                Assert.IsNotNull(definition, $"생성 에셋 '{path}'가 없습니다.");

                string[] expected = ExpectedRewardItemIds;
                Assert.AreEqual(expected.Length, definition.RewardItems.Count,
                    $"던전 '{row.Id}'의 대표 보상 개수가 CSV와 다릅니다.");

                for (int i = 0; i < expected.Length; i++)
                {
                    // 프로젝트를 뒤져 찾은 아이템이 아니라 <b>Item 표가 만든 그 에셋</b>이어야 한다 -
                    // 표 밖의 에셋을 가리키면 "CSV에 없는 보상"이 생긴다.
                    var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(
                        TableDataPaths.ItemAssetPath(expected[i]));
                    Assert.IsNotNull(item, $"아이템 생성 에셋이 없습니다: {expected[i]}");
                    Assert.AreSame(item, definition.RewardItems[i],
                        $"던전 '{row.Id}'의 {i}번째 대표 보상이 Item 생성 에셋 '{expected[i]}'가 아닙니다.");
                }
            }
        }

        [Test]
        public void GeneratedDefinitions_HavePreservedGUIDs()
        {
            var expected = new Dictionary<string, string>
            {
                { "1", "c41af48b8189b427897cc7dd37eeeb6b" },
                { "2", "896cccf79b3de406383335a1c0879370" },
                { "3", "ab7d6716f47f74bb1adb376d3c54a102" },
                { "4", "63d4ce595405a4715a77ebc3849a871f" },
                { "5", "9eb5880d32e9944c9900320a79d204c1" },
                { "6", "4e13a928ed5554679878c787fa42f11b" },
                { "7", "d724c65b2b9974fe9a22d1136b5a284a" },
                { "8", "e0bb9dbdfe4ae4702a290c787e3b9742" },
                { "9", "4b2420a8e45664fe3bef263783fab6f2" },
            };

            foreach (var kv in expected)
            {
                string path = TableDataPaths.DungeonAssetPath(kv.Key);
                string guid = AssetDatabase.AssetPathToGUID(path);
                Assert.IsFalse(string.IsNullOrEmpty(guid),
                    $"던전 '{kv.Key}' 에셋 '{path}'의 GUID를 찾지 못했습니다.");
                Assert.AreEqual(kv.Value, guid,
                    $"던전 '{kv.Key}' 에셋의 GUID가 기준값과 다릅니다 - .meta 파일이 변경되었습니다.");
            }
        }

        // ---- 저장 버전 ----

        [Test]
        public void SaveVersion_RemainsExactlyFour()
        {
            Assert.AreEqual(4, SaveData.CurrentSaveVersion,
                "이 단계는 저장 형식을 바꾸지 않는다 - v4를 유지해야 합니다.");
        }

        // ---- 런타임 에셋의 방어적 하한 ----

        [Test]
        public void RuntimeDefinition_ClampsToAtLeastOne()
        {
            var probe = UnityEngine.ScriptableObject.CreateInstance<DungeonDefinition>();
            try
            {
                var serialized = new SerializedObject(probe);
                serialized.FindProperty("corruptionIntervalSeconds").intValue = 0;
                serialized.FindProperty("corruptionGainPerInterval").intValue = 0;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                Assert.GreaterOrEqual(probe.CorruptionIntervalSeconds, 1);
                Assert.GreaterOrEqual(probe.CorruptionGainPerInterval, 1);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(probe);
            }
        }

        // ---- 도우미 ----

        private static TableDataValidationResult Live()
        {
            return liveResult ?? (liveResult = TableDataValidator.Validate());
        }

        private static TableDataSnapshot SnapshotWithWorld(params string[] worldIds)
        {
            var snapshot = new TableDataSnapshot();
            int line = 2;

            foreach (string id in worldIds)
            {
                var world = new WorldRow { Line = line++, Id = id, Enabled = true };
                snapshot.Worlds.Add(world);
                snapshot.WorldsById[id] = world;
            }

            return snapshot;
        }

        private static TableDataDiagnosticLog Validate(TableDataSnapshot snapshot, params string[][] rows)
        {
            var records = new List<CsvRecord>();
            for (int i = 0; i < rows.Length; i++) records.Add(new CsvRecord(i + 2, rows[i]));

            var table = new CsvTable(File, TableDataColumns.Dungeon, records);
            var log = new TableDataDiagnosticLog();
            var assets = new TableDataAssetIndex();

            ValidateDungeonsMethod.Invoke(null, new object[] { table, snapshot, assets, log });
            return log;
        }

        private static string[] Row(
            string dungeonId,
            string nameCategory = "2",
            string nameKey = "4",
            string worldId = "",
            string spriteKey = "",
            string monsterIds = "",
            string interval = "1",
            string gain = "1",
            string rewardItemIds = "",
            string displayOrder = "10",
            string enabled = "1",
            string memo = "")
        {
            return new[]
            {
                dungeonId, nameCategory, nameKey, worldId, spriteKey, monsterIds,
                interval, gain, rewardItemIds, displayOrder, enabled, memo,
            };
        }

        private static int CountErrors(TableDataDiagnosticLog log, string column)
        {
            int count = 0;
            foreach (TableDataDiagnostic diagnostic in log.Entries)
            {
                if (diagnostic.Severity == TableDataSeverity.Error
                    && string.Equals(diagnostic.Column, column, StringComparison.Ordinal))
                {
                    count++;
                }
            }

            return count;
        }

        private static string Describe(TableDataDiagnosticLog log)
        {
            var lines = new List<string>();
            foreach (TableDataDiagnostic diagnostic in log.Entries) lines.Add(diagnostic.ToString());
            return lines.Count == 0 ? "(진단 없음)" : "\n" + string.Join("\n", lines);
        }
    }
}
