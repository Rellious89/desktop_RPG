using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Party;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace TableDataEditor.Tests
{
    /// <summary>
    /// PartyConfig.csv의 스키마와 경로, <b>실제 CSV에 적혀 있는 내용</b>, 그리고 생성 에셋과 카탈로그를
    /// 확인한다(10A-1).
    ///
    /// <b>파일을 쓰지도 에셋을 만들지도 않는다.</b> 읽기 전용인
    /// <see cref="TableDataValidator.Validate()"/>와 이미 만들어져 있는 생성 에셋만 보며, Rebuild는
    /// 프로젝트를 바꾸므로 여기서 부르지 않는다(<see cref="RecruitmentTableTests"/>와 같은 방식이다).
    ///
    /// 값 검증의 <b>거부</b> 경로는 실제 CSV를 고칠 수 없으므로 메모리 위의 표로 확인한다 - 빈 ID,
    /// 중복 ID, 0/음수/비정수 정원이 모두 오류가 되는지를 임포터의 같은 코드로 통과시켜 본다.
    /// </summary>
    public sealed class PartyConfigTableTests
    {
        /// <summary>PartyConfig.csv에 실제로 적혀 있는 유일한 설정.</summary>
        private const string LiveConfigId = "default";

        private const int LiveBaseCapacity = 3;

        private static TableDataValidationResult liveResult;

        private static TableDataValidationResult Live =>
            liveResult ?? (liveResult = TableDataValidator.Validate());

        private readonly List<Object> created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object asset in created)
            {
                if (asset != null) Object.DestroyImmediate(asset);
            }

            created.Clear();
        }

        // ---- 스키마와 경로 ----

        [Test]
        public void Schema_IsExactlyTheAgreedColumns()
        {
            CollectionAssert.AreEqual(
                new[] { "party_config_id", "base_capacity", "enabled", "memo" },
                TableDataColumns.PartyConfig,
                "PartyConfig.csv의 필수 컬럼과 순서가 약속과 달라졌습니다.");
        }

        [Test]
        public void Schema_HasNoInventedColumns()
        {
            // 없는 칸을 지어내지 않는다 - 이 표에는 display_order도 이름 참조도 없다.
            foreach (string column in new[] { "display_order", "name_category", "name_key" })
            {
                CollectionAssert.DoesNotContain(TableDataColumns.PartyConfig, column);
            }
        }

        [Test]
        public void Paths_AreTheAgreedLocations()
        {
            Assert.AreEqual("Assets/TableData/Game/PartyConfig.csv", TableDataPaths.PartyConfigCsvPath);
            Assert.AreEqual("Assets/Generated/TableData/PartyConfig", TableDataPaths.PartyConfigOutputFolder);
            Assert.AreEqual("Assets/Generated/TableData/PartyConfig/PartyConfig_default.asset",
                TableDataPaths.PartyConfigAssetPath(LiveConfigId));
            Assert.AreEqual("Assets/Generated/TableData/PartyConfig/PartyConfigCatalog.asset",
                TableDataPaths.PartyConfigCatalogAssetPath);
        }

        [Test]
        public void Scope_PartyConfigOnlyWritesItsOwnFolder()
        {
            IReadOnlyList<string> folders =
                TableDataValidator.GeneratedOutputFolders(TableDataRebuildScope.PartyConfigTable);

            CollectionAssert.AreEqual(
                new[] { TableDataPaths.PartyConfigOutputFolder }, folders,
                "PartyConfig만 다시 만드는 범위는 자기 폴더 밖을 열지 않아야 한다.");
        }

        [Test]
        public void Scope_FullRebuildStillIncludesPartyConfig()
        {
            CollectionAssert.Contains(
                TableDataValidator.GeneratedOutputFolders(TableDataRebuildScope.All),
                TableDataPaths.PartyConfigOutputFolder,
                "전체 Rebuild가 PartyConfig를 빠뜨리면 표를 고쳐도 에셋이 따라오지 않는다.");

            Assert.IsTrue(TableDataRebuildScopes.IncludesPartyConfigTable(TableDataRebuildScope.All));
            Assert.IsTrue(TableDataRebuildScopes.IncludesPartyConfigTable(TableDataRebuildScope.PartyConfigTable));

            // 다른 좁은 범위는 PartyConfig를 건드리지 않는다.
            Assert.IsFalse(TableDataRebuildScopes.IncludesPartyConfigTable(TableDataRebuildScope.BuildingTable));
            Assert.IsFalse(TableDataRebuildScopes.IncludesPartyConfigTable(TableDataRebuildScope.RecruitmentTables));
            Assert.IsFalse(
                TableDataRebuildScopes.IncludesPartyConfigTable(TableDataRebuildScope.CharacterSkillTables));

            // 반대로 PartyConfig 범위는 다른 도메인을 하나도 포함하지 않는다.
            Assert.IsFalse(TableDataRebuildScopes.IncludesLegacyDomains(TableDataRebuildScope.PartyConfigTable));
            Assert.IsFalse(TableDataRebuildScopes.IncludesCharacterTables(TableDataRebuildScope.PartyConfigTable));
            Assert.IsFalse(TableDataRebuildScopes.IncludesBuildingTable(TableDataRebuildScope.PartyConfigTable));
            Assert.IsFalse(TableDataRebuildScopes.IncludesRecruitmentTables(TableDataRebuildScope.PartyConfigTable));
        }

        // ---- 실제 CSV ----

        [Test]
        public void LiveCsv_HasNoErrors()
        {
            Assert.IsNotNull(Live.Snapshot, "열네 표가 모두 읽히지 않았습니다: " + Live.Summary);
            Assert.AreEqual(0, Live.ErrorCount, "PartyConfig를 포함한 검증에서 오류가 남아 있습니다: " + Live.Summary);
        }

        [Test]
        public void LiveCsv_HasExactlyTheDefaultRowWithCapacityThree()
        {
            TableDataSnapshot snapshot = Live.Snapshot;
            Assert.IsNotNull(snapshot);

            Assert.AreEqual(1, snapshot.PartyConfigs.Count, "지금 표에 적혀 있는 설정은 하나뿐이어야 한다.");

            PartyConfigRow row = snapshot.PartyConfigs[0];
            Assert.AreEqual(LiveConfigId, row.Id);
            Assert.AreEqual(LiveBaseCapacity, row.BaseCapacity);
            Assert.IsTrue(row.Enabled);
            Assert.IsTrue(snapshot.PartyConfigsById.ContainsKey(LiveConfigId));
        }

        [Test]
        public void LiveCsv_IdMatchesTheCodeConstantExactly()
        {
            // 비교는 Ordinal이므로 대소문자가 다르면 다른 키다 - 표와 코드가 어긋나면 정원을 못 읽는다.
            Assert.AreEqual(LiveConfigId, PartyConfigIds.Default);
            Assert.IsTrue(Live.Snapshot.PartyConfigsById.ContainsKey(PartyConfigIds.Default),
                $"코드가 쓰는 키 '{PartyConfigIds.Default}'가 표에 없습니다 - 철자가 글자 하나까지 같아야 합니다.");
        }

        [Test]
        public void IdFormat_UsesTheStandardRule()
        {
            Assert.IsTrue(TableDataFieldRules.IsValidId(PartyConfigIds.Default));

            // 표준 형식이므로 대문자와 공백은 받지 않는다.
            Assert.IsFalse(TableDataFieldRules.IsValidId("Default"));
            Assert.IsFalse(TableDataFieldRules.IsValidId(" default"));
            Assert.IsFalse(TableDataFieldRules.IsValidId(string.Empty));
        }

        // ---- 값 거부 ----

        [Test]
        public void Validation_RejectsEmptyAndDuplicateIds()
        {
            TableDataSnapshot snapshot = ValidateRows(
                out TableDataDiagnosticLog log,
                ",3,1,빈 id",
                "default,3,1,첫 행",
                "default,4,1,같은 id 두 번");

            Assert.AreEqual(2, log.ErrorCount, Describe(log));
            Assert.AreEqual(1, snapshot.PartyConfigs.Count, "빈 id와 중복 id 행은 스냅샷에 남지 않는다.");
            Assert.AreEqual(3, snapshot.PartyConfigs[0].BaseCapacity, "겹칠 때는 먼저 나온 행이 남는다.");
        }

        [TestCase("0", TestName = "Validation_RejectsCapacity_Zero")]
        [TestCase("-1", TestName = "Validation_RejectsCapacity_Negative")]
        [TestCase("2.5", TestName = "Validation_RejectsCapacity_NotAnInteger")]
        [TestCase("three", TestName = "Validation_RejectsCapacity_NotANumber")]
        [TestCase("", TestName = "Validation_RejectsCapacity_Empty")]
        public void Validation_RejectsBadCapacityWithoutSilentlyFixingIt(string rawCapacity)
        {
            TableDataSnapshot snapshot = ValidateRows(
                out TableDataDiagnosticLog log, "default," + rawCapacity + ",1,잘못된 정원");

            Assert.AreEqual(1, log.ErrorCount, Describe(log));
            Assert.AreNotEqual(0, snapshot.PartyConfigs[0].BaseCapacity,
                "잘못된 값을 0으로 옮겨 적지 않는다 - 오류로 막았으므로 Rebuild는 실행되지 않는다.");
        }

        [Test]
        public void Validation_AcceptsTheMinimumCapacity()
        {
            TableDataSnapshot snapshot = ValidateRows(out TableDataDiagnosticLog log, "default,1,1,최소 정원");

            Assert.AreEqual(0, log.ErrorCount, Describe(log));
            Assert.AreEqual(PartyConfigRules.MinimumBaseCapacity, snapshot.PartyConfigs[0].BaseCapacity);
        }

        // ---- 카탈로그 ----

        /// <summary>
        /// <b>카탈로그에 무엇이 들어가는지는 임포터가 정한다.</b> 활성 행만 넣는 것은
        /// <c>WritePartyConfigTable</c>의 <c>FilterForCatalog</c>가 하는 일이고(실제 결과는
        /// <see cref="GeneratedAssets_OnlyContainTheEnabledRows"/>가 확인한다), 카탈로그 자신은
        /// 넘겨받은 목록을 그대로 들고 있으면서 자기 몫의 거르기만 한다 - 이 둘을 뒤섞으면 "왜 빠졌는가"의
        /// 답이 두 곳이 된다.
        /// </summary>
        [Test]
        public void Catalog_DoesNotFilterByEnabledItself()
        {
            PartyConfigDefinition enabled = Config("default", 3, enabled: true);
            PartyConfigDefinition disabled = Config("hard", 5, enabled: false);
            PartyConfigCatalog catalog = Catalog(enabled, disabled);

            Assert.AreEqual(2, catalog.Count, "꺼진 정의라도 목록에 직접 넣으면 카탈로그는 그대로 들고 있는다.");
            Assert.AreSame(enabled, catalog.Find("default"));
            Assert.AreSame(disabled, catalog.Find("hard"));
            Assert.IsFalse(disabled.Enabled, "표의 enabled 값 자체는 정의 에셋에 그대로 남아 있어야 한다.");
        }

        [Test]
        public void Catalog_DropsEmptyInvalidAndDuplicateEntries()
        {
            PartyConfigDefinition first = Config("default", 3);
            PartyConfigCatalog catalog = Catalog(
                null,
                Config(string.Empty, 3),
                Config("broken", 0),
                first,
                Config("default", 9));

            LogAssert.ignoreFailingMessages = true;
            try
            {
                Assert.AreEqual(1, catalog.Count);
                Assert.AreSame(first, catalog.Configs[0], "겹칠 때는 먼저 작성된 행이 남는다.");
                Assert.AreEqual(3, catalog.Find("default").BaseCapacity);
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
            }
        }

        [Test]
        public void Catalog_LookupIsOrdinalAndChangesNothing()
        {
            PartyConfigCatalog catalog = Catalog(Config("default", 3));

            Assert.IsNotNull(catalog.Find("default"));
            Assert.IsNull(catalog.Find("Default"), "조회는 Ordinal이라 대소문자가 다르면 다른 키다.");
            Assert.IsNull(catalog.Find("  "));
            Assert.IsNull(catalog.Find(null));

            // 조회로 상태가 바뀌지 않는다 - 직렬화 내용이 한 글자도 달라지지 않아야 한다.
            string before = EditorJsonUtility.ToJson(catalog);
            for (int i = 0; i < 10; i++)
            {
                catalog.Find("default");
                Assert.AreEqual(1, catalog.Count);
            }

            Assert.AreEqual(before, EditorJsonUtility.ToJson(catalog));
        }

        // ---- 생성 에셋 ----

        [Test]
        public void GeneratedAssets_MatchTheLiveCsv()
        {
            var definition = AssetDatabase.LoadAssetAtPath<PartyConfigDefinition>(
                TableDataPaths.PartyConfigAssetPath(LiveConfigId));
            Assert.IsNotNull(definition,
                $"{TableDataPaths.PartyConfigAssetPath(LiveConfigId)}가 없습니다 - Rebuild를 실행하세요.");

            Assert.AreEqual(LiveConfigId, definition.ConfigId);
            Assert.AreEqual(LiveBaseCapacity, definition.BaseCapacity);
            Assert.IsTrue(definition.Enabled);
            Assert.IsTrue(definition.IsValid);

            var catalog = AssetDatabase.LoadAssetAtPath<PartyConfigCatalog>(
                TableDataPaths.PartyConfigCatalogAssetPath);
            Assert.IsNotNull(catalog,
                $"{TableDataPaths.PartyConfigCatalogAssetPath}가 없습니다 - Rebuild를 실행하세요.");

            Assert.AreSame(definition, catalog.Find(PartyConfigIds.Default));
            Assert.AreEqual(LiveBaseCapacity, catalog.Find(PartyConfigIds.Default).BaseCapacity);
        }

        /// <summary>생성 카탈로그에는 <b>활성 행만</b> 들어간다 - 임포터가 그렇게 넣기 때문이다.</summary>
        [Test]
        public void GeneratedAssets_OnlyContainTheEnabledRows()
        {
            TableDataSnapshot snapshot = Live.Snapshot;
            Assert.IsNotNull(snapshot);

            var expected = new List<string>();
            foreach (PartyConfigRow row in snapshot.PartyConfigs)
            {
                if (row.Enabled) expected.Add(row.Id);
            }

            var catalog = AssetDatabase.LoadAssetAtPath<PartyConfigCatalog>(
                TableDataPaths.PartyConfigCatalogAssetPath);
            Assert.IsNotNull(catalog,
                $"{TableDataPaths.PartyConfigCatalogAssetPath}가 없습니다 - Rebuild를 실행하세요.");

            var actual = new List<string>();
            foreach (PartyConfigDefinition config in catalog.Configs) actual.Add(config.ConfigId);

            CollectionAssert.AreEqual(expected, actual,
                "카탈로그는 표의 활성 행을 CSV 순서 그대로 담아야 한다.");
        }

        // ---- 임포터가 쓰는 칸이 실제로 있는지 ----

        [Test]
        public void SerializedLayout_MatchesWhatTheImporterWrites()
        {
            MethodInfo verify = typeof(TableDataRebuilder).GetMethod(
                "VerifySerializedLayout", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(verify,
                "TableDataRebuilder.VerifySerializedLayout을 찾지 못했습니다 - 이름이 바뀌었다면 이 시험도 함께 고치세요.");

            var log = new TableDataDiagnosticLog();
            var ok = (bool)verify.Invoke(null, new object[] { log });

            Assert.IsTrue(ok, "임포터가 쓰려는 직렬화 칸이 런타임 클래스와 어긋납니다: " +
                              string.Join("\n", log.Entries));
        }

        // ---- 도우미 ----

        /// <summary>
        /// 메모리 위의 PartyConfig 표를 임포터의 <b>같은 검증 코드</b>로 통과시킨다. 파일을 만들지도
        /// 읽지도 않으므로 실제 CSV와 프로젝트는 한 글자도 달라지지 않는다.
        /// </summary>
        private static TableDataSnapshot ValidateRows(out TableDataDiagnosticLog log, params string[] rows)
        {
            var text = new System.Text.StringBuilder();
            text.Append(string.Join(",", TableDataColumns.PartyConfig));
            foreach (string row in rows) text.Append('\n').Append(row);

            Assert.IsTrue(CsvParser.TryParse(text.ToString(), out List<CsvRecord> records, out string error, out int _),
                "시험용 CSV를 만들지 못했습니다: " + error);

            string[] header = new string[TableDataColumns.PartyConfig.Length];
            System.Array.Copy(TableDataColumns.PartyConfig, header, header.Length);
            records.RemoveAt(0);

            var table = new CsvTable(TableDataPaths.PartyConfigCsvFileName, header, records);
            var snapshot = new TableDataSnapshot();
            log = new TableDataDiagnosticLog();

            MethodInfo validate = typeof(TableDataValidator).GetMethod(
                "ValidatePartyConfigs", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(validate,
                "TableDataValidator.ValidatePartyConfigs를 찾지 못했습니다 - 이름이 바뀌었다면 이 시험도 함께 고치세요.");

            validate.Invoke(null, new object[] { table, snapshot, log });
            return snapshot;
        }

        private static string Describe(TableDataDiagnosticLog log)
        {
            return "진단: " + string.Join("\n", log.Entries);
        }

        private PartyConfigDefinition Config(string configId, int baseCapacity, bool enabled = true)
        {
            var definition = ScriptableObject.CreateInstance<PartyConfigDefinition>();
            created.Add(definition);

            var serialized = new SerializedObject(definition);
            serialized.FindProperty("configId").stringValue = configId;
            serialized.FindProperty("baseCapacity").intValue = baseCapacity;
            serialized.FindProperty("enabled").boolValue = enabled;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return definition;
        }

        private PartyConfigCatalog Catalog(params PartyConfigDefinition[] configs)
        {
            var catalog = ScriptableObject.CreateInstance<PartyConfigCatalog>();
            created.Add(catalog);

            var serialized = new SerializedObject(catalog);
            SerializedProperty list = serialized.FindProperty("configs");
            Assert.IsNotNull(list, "PartyConfigCatalog에 'configs' 목록 칸이 없습니다.");

            list.arraySize = configs.Length;
            for (int i = 0; i < configs.Length; i++)
            {
                list.GetArrayElementAtIndex(i).objectReferenceValue = configs[i];
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            catalog.MarkDirty();
            return catalog;
        }
    }
}
