using System;
using System.Collections.Generic;
using System.Reflection;
using Building;
using Inventory;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace TableDataEditor.Tests
{
    /// <summary>
    /// Building 표의 <b>출력</b>에 관한 시험 - 생성 경로의 경계, Building만 다시 만드는 좁은 범위의
    /// 계약, 그리고 실제로 만들어진 에셋의 모양이다.
    ///
    /// <b>여기서 에셋을 만들거나 지우지 않는다.</b> Rebuild를 부르는 시험은 없다 - 시험이 프로젝트를
    /// 다시 쓰기 시작하면 "시험을 돌렸더니 자산이 달라졌다"가 되기 때문이다
    /// (<see cref="CharacterTableOutputTests"/>와 같은 방식이다). 생성 결과는 읽기만 한다.
    /// </summary>
    public sealed class BuildingTableOutputTests
    {
        /// <summary>Building을 뺀 나머지 여덟 도메인의 생성 폴더. 좁은 범위가 <b>열어서도 안 되는</b> 곳이다.</summary>
        private static readonly string[] OtherOutputFolders =
        {
            TableDataPaths.WorldOutputFolder, TableDataPaths.CurrencyOutputFolder,
            TableDataPaths.ItemOutputFolder, TableDataPaths.MonsterOutputFolder,
            TableDataPaths.DungeonOutputFolder, TableDataPaths.CharacterOutputFolder,
            TableDataPaths.SkillOutputFolder, TableDataPaths.CharacterSkillOutputFolder,
        };

        private const string BuildingAssetPath = "Assets/Generated/TableData/Building/Building_1.asset";

        private const string BuildingTableGuidTag = "GUID:161824df6b6eb43a1a6fa7c55deea323";

        private const long BuildingNameKeyId = 288458006528L;

        private const string UiTableGuidTag = "GUID:32fd067a20b754a50b20446b9c78d2ae";

        private const long UiFunctionKeyId = 8908411117756417L;

        private static TableDataValidationResult allResult;
        private static TableDataValidationResult buildingResult;

        // ---- 출력 경로의 경계 ----

        [Test]
        public void BuildingOutputFolder_IsASiblingOfEveryOtherDomain()
        {
            string folder = TableDataPaths.BuildingOutputFolder;

            Assert.IsTrue(folder.StartsWith(TableDataPaths.OutputRoot + "/", StringComparison.Ordinal),
                $"'{folder}'는 생성 루트 아래에 있어야 한다.");

            foreach (string other in OtherOutputFolders)
            {
                Assert.AreNotEqual(other, folder);
                Assert.IsFalse(folder.StartsWith(other + "/", StringComparison.Ordinal),
                    $"'{folder}'가 '{other}' 안에 있으면 그 폴더의 정리 동작이 건물 에셋에 닿는다.");
                Assert.IsFalse(other.StartsWith(folder + "/", StringComparison.Ordinal),
                    $"'{other}'가 '{folder}' 안에 있으면 안 된다.");
            }
        }

        // ---- 범위의 계약 ----

        [Test]
        public void BuildingScope_IsSupportedAndClassified()
        {
            Assert.IsTrue(TableDataRebuildScopes.IsSupported(TableDataRebuildScope.BuildingTable));

            Assert.IsFalse(TableDataRebuildScopes.IncludesLegacyDomains(TableDataRebuildScope.BuildingTable));
            Assert.IsFalse(TableDataRebuildScopes.IncludesCharacterTables(TableDataRebuildScope.BuildingTable));
            Assert.IsTrue(TableDataRebuildScopes.IncludesBuildingTable(TableDataRebuildScope.BuildingTable));

            Assert.IsTrue(TableDataRebuildScopes.IncludesBuildingTable(TableDataRebuildScope.All),
                "전체 범위는 Building도 만든다.");
            Assert.IsFalse(TableDataRebuildScopes.IncludesBuildingTable(TableDataRebuildScope.CharacterSkillTables),
                "캐릭터 쪽 좁은 범위는 Building을 건드리지 않는다.");
        }

        [Test]
        public void EveryDeclaredScope_IsSupported()
        {
            CollectionAssert.AreEquivalent(
                new[]
                {
                    TableDataRebuildScope.All,
                    TableDataRebuildScope.CharacterSkillTables,
                    TableDataRebuildScope.BuildingTable,
                },
                Enum.GetValues(typeof(TableDataRebuildScope)),
                "선언된 범위와 지원하는 범위가 어긋나면 안 된다.");
        }

        [Test]
        public void UnsupportedScope_IsRejectedEverywhere()
        {
            var unsupported = (TableDataRebuildScope)999;

            Assert.IsFalse(TableDataRebuildScopes.IsSupported(unsupported));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => TableDataRebuildScopes.IncludesBuildingTable(unsupported));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => TableDataRebuildScopes.IncludesCharacterTables(unsupported));
        }

        [Test]
        public void BuildingScope_SelectsOnlyTheBuildingGeneratedFolder()
        {
            IReadOnlyList<string> targeted =
                TableDataValidator.GeneratedOutputFolders(TableDataRebuildScope.BuildingTable);

            CollectionAssert.AreEqual(
                new[] { TableDataPaths.BuildingOutputFolder }, targeted,
                "Building만 다시 만드는 범위는 그 폴더 하나만 열어야 한다.");

            foreach (string other in OtherOutputFolders)
            {
                CollectionAssert.DoesNotContain(targeted, other,
                    $"'{other}'를 열면 그 도메인의 생성 에셋을 로드하게 된다.");
            }
        }

        [Test]
        public void AllScope_IncludesTheBuildingFolderToo()
        {
            CollectionAssert.Contains(
                TableDataValidator.GeneratedOutputFolders(TableDataRebuildScope.All),
                TableDataPaths.BuildingOutputFolder,
                "전체 범위는 아홉 도메인을 모두 본다.");
        }

        [Test]
        public void BuildingScopeValidation_ReportsNoOrphansForTheOtherDomains()
        {
            // 다른 도메인에는 CSV에서 사라진 생성 에셋이 실제로 남아 있다(World animal_land 등).
            // 전체 범위에서는 그 경고가 나오고, Building 범위에서는 그 폴더를 열지 않으므로 나오지
            // 않는다 - 경고 유무가 "로드했는가"의 관측 가능한 증거다.
            Assert.AreEqual(0, All().ErrorCount, Describe(All()));
            Assert.AreEqual(0, BuildingOnly().ErrorCount, Describe(BuildingOnly()));

            Assert.Greater(CountOtherDomainMentions(All()), 0,
                "전제 확인 - 전체 범위에서는 다른 도메인의 생성 에셋을 가리키는 진단이 실제로 있어야 한다.");
            Assert.AreEqual(0, CountOtherDomainMentions(BuildingOnly()),
                "Building 범위는 다른 도메인의 생성 폴더를 열지 않으므로 그 진단이 나올 수 없다.");
        }

        [Test]
        public void BuildingScopeValidation_StillReadsEveryCsv()
        {
            // 범위는 "무엇을 쓸지"만 정하고 "무엇을 확인할지"는 정하지 않는다 - 좁게 돌렸더니
            // 통과했다는 상태가 생기면 안 된다.
            TableDataSnapshot snapshot = BuildingOnly().Snapshot;
            Assert.IsNotNull(snapshot, "좁은 범위에서도 아홉 표가 모두 읽혀야 한다: " + BuildingOnly().Summary);

            Assert.Greater(snapshot.Worlds.Count, 0);
            Assert.Greater(snapshot.Currencies.Count, 0);
            Assert.Greater(snapshot.Items.Count, 0);
            Assert.Greater(snapshot.Monsters.Count, 0);
            Assert.Greater(snapshot.Dungeons.Count, 0);
            Assert.Greater(snapshot.Characters.Count, 0);
            Assert.AreEqual(1, snapshot.Buildings.Count);
        }

        [Test]
        public void RebuildEntryPoint_TakesTheScope()
        {
            MethodInfo rebuild = typeof(TableDataRebuilder).GetMethod(
                "Rebuild", new[] { typeof(TableDataRebuildScope) });

            Assert.IsNotNull(rebuild, "범위를 받는 Rebuild 진입점이 있어야 한다.");
        }

        [Test]
        public void BuildingRebuild_SavesOnlyItsOwnTargets_NeverTheGlobalSaveAssets()
        {
            // AssetDatabase.SaveAssets()는 프로젝트의 <b>모든</b> dirty 에셋을 디스크에 쓰는 전역
            // 동작이라, 그것을 부르는 순간 "이 범위 밖은 건드리지 않는다"가 저장 단계에서 깨진다 -
            // 사람이 인스펙터에서 고쳐 두고 아직 저장하지 않은 무관한 에셋까지 함께 커밋된다.
            Assert.IsNotNull(
                typeof(TableDataRebuilder).GetMethod(
                    "RebuildBuildingTable", BindingFlags.NonPublic | BindingFlags.Static),
                "TableDataRebuilder.RebuildBuildingTable을 찾지 못했습니다.");

            string body = ReadMethodBody("RebuildBuildingTable");

            StringAssert.Contains("SaveAssetIfDirty", body,
                "이번에 만든/다시 쓴 대상만 하나씩 저장해야 한다.");
            Assert.IsFalse(body.Contains("SaveAssets()"),
                "Building 범위의 Rebuild가 전역 SaveAssets를 부르면 범위 밖 에셋까지 함께 저장된다.");
        }

        // ---- 실제로 만들어진 에셋 ----

        [Test]
        public void GeneratedInn_HasTheValuesAuthoredInTheCsv()
        {
            BuildingDefinition inn = LoadInn();

            Assert.AreEqual("1", inn.BuildingId, "building_id는 CSV에 적힌 그대로여야 한다.");
            Assert.AreEqual(60, inn.BuildTimeSeconds);
            Assert.AreEqual("jewel", inn.CostCurrencyId);
            Assert.AreEqual(2000, inn.CostCurrencyAmount);
            Assert.AreEqual(0, inn.CostItems.Count, "여관은 아이템 비용이 없다.");
            Assert.AreEqual(10, inn.DisplayOrder);
            Assert.IsTrue(inn.IsValid);
        }

        [Test]
        public void GeneratedInn_CarriesTheEnabledValueFromTheCsv()
        {
            // 여관은 Building.csv에서 enabled=1이다. 정의 에셋도 그 값을 그대로 들고 있어야 한다 -
            // "카탈로그에 있으니 켜진 것이겠지"로 미루면 에셋 하나만 보고는 표의 값을 알 수 없다.
            BuildingDefinition inn = LoadInn();

            Assert.IsTrue(inn.Enabled, "Building.csv에서 enabled=1인 여관의 정의는 Enabled가 true여야 한다.");

            SerializedProperty enabled = new SerializedObject(inn).FindProperty("enabled");
            Assert.IsNotNull(enabled, "생성된 에셋에 'enabled' 칸이 있어야 한다.");
            Assert.AreEqual(SerializedPropertyType.Boolean, enabled.propertyType,
                "enabled는 참/거짓 칸이어야 한다 - 타입이 어긋나면 Rebuild가 적은 값이 조용히 버려진다.");
            Assert.IsTrue(enabled.boolValue, "직렬화된 값 자체가 켜져 있어야 한다.");
        }

        [Test]
        public void GeneratedInn_PointsAtTheGeneratedJewelCurrency()
        {
            BuildingDefinition inn = LoadInn();

            var jewel = AssetDatabase.LoadAssetAtPath<CurrencyDefinition>(
                TableDataPaths.CurrencyAssetPath("jewel"));

            Assert.IsNotNull(jewel, "생성된 Currency_jewel.asset이 있어야 한다.");
            Assert.AreSame(jewel, inn.CostCurrency,
                "비용 재화 참조가 생성된 CurrencyDefinition을 가리켜야 한다 - " +
                "참조가 비어 있으면 '비용이 적혀 있는데 아무것도 안 내는' 건물이 된다.");
        }

        [Test]
        public void GeneratedInn_BindsBothLocalizedReferences()
        {
            // Table GUID + Entry Key ID 두 숫자로만 저장되므로, 조금이라도 어긋나면 화면에 빈 문구가
            // 나온다. 번역 <b>내용</b>은 보지 않는다 - 영어와 한국어가 같아도 정상이다.
            SerializedObject serialized = new SerializedObject(LoadInn());

            AssertLocalizedReference(serialized, "localizedName", BuildingTableGuidTag, BuildingNameKeyId);
            AssertLocalizedReference(serialized, "localizedFunctionName", UiTableGuidTag, UiFunctionKeyId);
        }

        [Test]
        public void GeneratedCatalog_ContainsExactlyTheEnabledInn()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<BuildingCatalog>(TableDataPaths.BuildingCatalogAssetPath);
            Assert.IsNotNull(catalog, $"'{TableDataPaths.BuildingCatalogAssetPath}'를 읽지 못했습니다.");

            Assert.AreEqual(1, catalog.Count, "지금 활성 건물은 여관 하나뿐이다.");
            Assert.AreSame(LoadInn(), catalog.Buildings[0]);
            Assert.AreSame(LoadInn(), catalog.Find("1"));
            Assert.IsNull(catalog.Find("Inn"), "조회는 적힌 그대로 비교한다 - 표시 이름으로는 찾지 못한다.");
        }

        [Test]
        public void GeneratedFolder_HoldsOnlyTheInnAndItsCatalog()
        {
            var found = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets(
                         "t:ScriptableObject", new[] { TableDataPaths.BuildingOutputFolder }))
            {
                found.Add(AssetDatabase.GUIDToAssetPath(guid));
            }

            CollectionAssert.AreEquivalent(
                new[] { BuildingAssetPath, TableDataPaths.BuildingCatalogAssetPath },
                found,
                "생성 폴더에 목록에 없는 에셋이 있습니다:\n" + string.Join("\n", found));
        }

        // ---- 도우미 ----

        private static TableDataValidationResult All()
        {
            return allResult ?? (allResult = TableDataValidator.Validate(TableDataRebuildScope.All));
        }

        private static TableDataValidationResult BuildingOnly()
        {
            return buildingResult
                   ?? (buildingResult = TableDataValidator.Validate(TableDataRebuildScope.BuildingTable));
        }

        private static BuildingDefinition LoadInn()
        {
            var inn = AssetDatabase.LoadAssetAtPath<BuildingDefinition>(BuildingAssetPath);
            Assert.IsNotNull(inn, $"'{BuildingAssetPath}'를 읽지 못했습니다 - Building Rebuild를 실행하세요.");
            return inn;
        }

        private static void AssertLocalizedReference(
            SerializedObject serialized, string field, string expectedTableGuidTag, long expectedKeyId)
        {
            SerializedProperty property = serialized.FindProperty(field);
            Assert.IsNotNull(property, $"'{field}' 칸을 찾지 못했습니다.");

            SerializedProperty table = property.FindPropertyRelative("m_TableReference.m_TableCollectionName");
            SerializedProperty keyId = property.FindPropertyRelative("m_TableEntryReference.m_KeyId");

            Assert.IsNotNull(table, $"'{field}'의 Table 참조 칸을 찾지 못했습니다.");
            Assert.IsNotNull(keyId, $"'{field}'의 Key Id 칸을 찾지 못했습니다.");

            Assert.AreEqual(expectedTableGuidTag, table.stringValue,
                $"'{field}'가 가리키는 String Table이 다릅니다.");
            Assert.AreEqual(expectedKeyId, keyId.longValue, $"'{field}'의 Entry Key Id가 다릅니다.");
        }

        /// <summary>진단이 <b>다른 도메인의 생성 에셋 경로</b>를 몇 번 가리키는지. 그 폴더를 열었다는
        /// 관측 가능한 증거다.</summary>
        private static int CountOtherDomainMentions(TableDataValidationResult result)
        {
            int mentions = 0;
            foreach (TableDataDiagnostic diagnostic in result.Diagnostics)
            {
                foreach (string folder in OtherOutputFolders)
                {
                    if (diagnostic.Message.IndexOf(folder + "/", StringComparison.Ordinal) >= 0) mentions++;
                }
            }

            return mentions;
        }

        /// <summary>
        /// <see cref="TableDataRebuilder"/> 소스에서 메서드 하나의 본문을 잘라 온다. 컴파일된 IL을
        /// 뒤지는 대신 소스를 읽는 이유는, 확인하려는 것이 "무엇을 <b>부르지 않는가</b>"이고 그것을
        /// 리플렉션으로는 볼 수 없기 때문이다. 이름이 바뀌면 여기서 곧바로 실패한다.
        /// </summary>
        private static string ReadMethodBody(string methodName)
        {
            string path = System.IO.Path.Combine(
                System.IO.Directory.GetParent(Application.dataPath).FullName,
                "Assets/Editor/TableData/TableDataRebuilder.cs"
                    .Replace('/', System.IO.Path.DirectorySeparatorChar));

            Assert.IsTrue(System.IO.File.Exists(path), $"'{path}'를 찾지 못했습니다.");

            string source = System.IO.File.ReadAllText(path);

            // 호출부가 아니라 <b>선언</b>을 찾아야 한다 - 같은 이름이 먼저 호출되는 자리가 있으면
            // 엉뚱한 메서드의 본문을 읽고도 통과하거나 실패한다.
            int start = source.IndexOf("static TableDataRebuildResult " + methodName + "(", StringComparison.Ordinal);
            Assert.Greater(start, 0, $"'{methodName}' 선언을 찾지 못했습니다.");

            // 선언 다음의 여는 중괄호부터 짝이 맞는 닫는 중괄호까지.
            int open = source.IndexOf('{', start);
            Assert.Greater(open, 0, $"'{methodName}'의 본문을 찾지 못했습니다.");

            int depth = 0;
            for (int i = open; i < source.Length; i++)
            {
                if (source[i] == '{') depth++;
                else if (source[i] == '}' && --depth == 0) return source.Substring(open, i - open + 1);
            }

            Assert.Fail($"'{methodName}'의 본문이 닫히지 않았습니다.");
            return string.Empty;
        }

        private static string Describe(TableDataValidationResult result)
        {
            var lines = new List<string>();
            foreach (TableDataDiagnostic diagnostic in result.Diagnostics) lines.Add(diagnostic.ToString());
            return lines.Count == 0 ? "(진단 없음)" : "\n" + string.Join("\n", lines);
        }
    }
}
