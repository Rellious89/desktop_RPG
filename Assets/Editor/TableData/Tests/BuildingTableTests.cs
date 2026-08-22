using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Building;
using Common;
using Inventory;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace TableDataEditor.Tests
{
    /// <summary>
    /// Building.csv 파이프라인 시험 - 스키마와 경로, 행 검증 규칙, 실제 CSV에 적힌 여관 한 행, 그리고
    /// 비용을 <see cref="InventoryCostRequest"/>로 옮기는 변환이다.
    ///
    /// <b>파일을 쓰지도 에셋을 만들지도 않는다.</b> 행 검증은 메모리에서 만든 <see cref="CsvTable"/>로
    /// 돌리고, 실제 데이터 확인은 읽기 전용인 <see cref="TableDataValidator.Validate"/>만 쓴다
    /// (<see cref="CurrencyTableTests"/> / <see cref="ItemTableTests"/>와 같은 방식이다). Rebuild는
    /// 프로젝트를 바꾸므로 여기서 부르지 않는다.
    ///
    /// <b>번역 <i>내용</i>은 하나도 보지 않는다.</b> 영어와 한국어 값이 같아도(1001번 '용병 모집'이
    /// 그렇다), 영어 칸에 한국어가 들어 있어도 그것은 번역의 문제이지 표 파이프라인의 문제가 아니다 -
    /// 여기서 확인하는 것은 "그 Entry가 실제로 있고, 생성 에셋이 그 Entry를 가리키는가"까지다.
    /// </summary>
    public sealed class BuildingTableTests
    {
        private const string File = TableDataPaths.BuildingCsvFileName;

        /// <summary>Building.csv에 실제로 적혀 있는 유일한 행(여관)의 값들. <b>표에 적힌 그대로</b>다.</summary>
        private const string LiveBuildingId = "1";

        private const int LiveBuildTimeSeconds = 60;
        private const string LiveCostCurrencyId = "jewel";
        private const int LiveCostCurrencyAmount = 2000;
        private const int LiveDisplayOrder = 10;

        /// <summary>여관 이름은 07_Building의 숫자 키 1이다.</summary>
        private const string BuildingTableGuid = "161824df6b6eb43a1a6fa7c55deea323";

        private const long BuildingNameKeyId = 288458006528L;

        /// <summary>여관이 여는 기능 이름은 <b>다른 표</b>(01_UI)의 숫자 키 1001이다 - 건물 이름과
        /// 기능 이름이 같은 카테고리에 있어야 할 이유는 없다.</summary>
        private const string UiTableGuid = "32fd067a20b754a50b20446b9c78d2ae";

        private const long UiFunctionKeyId = 8908411117756417L;

        private static readonly MethodInfo ValidateBuildingsMethod =
            typeof(TableDataValidator).GetMethod("ValidateBuildings", BindingFlags.NonPublic | BindingFlags.Static);

        /// <summary>실제 CSV를 읽는 검증은 무거우므로 한 번만 돌리고 결과를 나눠 쓴다. 읽기 전용이라
        /// 시험 사이에 상태가 새지 않는다.</summary>
        private static TableDataValidationResult liveResult;

        /// <summary>시험이 메모리에서 만든 ScriptableObject. 디스크에는 아무것도 남지 않는다.</summary>
        private readonly List<UnityEngine.Object> created = new List<UnityEngine.Object>();

        [SetUp]
        public void SetUpFixture()
        {
            Assert.IsNotNull(ValidateBuildingsMethod,
                "TableDataValidator.ValidateBuildings를 찾지 못했습니다 - 이름이 바뀌었다면 이 시험도 함께 고치세요.");
        }

        [TearDown]
        public void TearDown()
        {
            foreach (UnityEngine.Object asset in created)
            {
                if (asset != null) UnityEngine.Object.DestroyImmediate(asset);
            }

            created.Clear();
        }

        // ---- 스키마와 경로 ----

        [Test]
        public void Schema_IsExactlyTheAgreedColumns()
        {
            CollectionAssert.AreEqual(
                new[]
                {
                    "building_id", "name_category", "name_key", "function_category", "function_key",
                    "build_time", "cost_currency_id", "cost_currency_amount", "cost_item_ids",
                    "cost_item_counts", "display_order", "enabled", "memo",
                },
                TableDataColumns.Building,
                "Building.csv의 필수 컬럼과 순서가 약속과 달라졌습니다.");
        }

        [Test]
        public void Schema_DoesNotIncludeTheReferenceOnlyColumns()
        {
            // $ 컬럼은 사람이 보라고 붙인 칸이라 <b>필수 컬럼이 되면 안 된다</b> - 되는 순간 임포터가
            // 값을 읽는 칸처럼 보이고, 그 칸을 지우면 파일 전체가 실패한다.
            foreach (string reference in new[]
                     {
                         "$building_name", "$function_description", "$build_time", "$cost_currency", "$cost_item",
                     })
            {
                CollectionAssert.DoesNotContain(TableDataColumns.Building, reference,
                    $"{reference}는 작업자용 참조 컬럼이라 필수 컬럼이 되면 안 된다.");
                Assert.IsTrue(TableDataCsvReader.IsReferenceOnlyColumn(reference),
                    $"{reference}는 기존 참조 컬럼 정책으로 통과해야 한다.");
            }
        }

        [Test]
        public void Schema_UsesItsOwnCostCurrencyColumnName_NotMonstersRewardColumn()
        {
            // 같은 표에 "보상 재화"와 "비용 재화"가 함께 오면 이름만으로 갈리지 않으므로 처음부터 다르다.
            Assert.AreEqual("cost_currency_id", TableDataColumns.CostCurrencyId);
            CollectionAssert.DoesNotContain(TableDataColumns.Building, TableDataColumns.CurrencyId);
        }

        [Test]
        public void Paths_AreTheAgreedLocations()
        {
            Assert.AreEqual("Assets/TableData/Game/Building.csv", TableDataPaths.BuildingCsvPath);
            Assert.AreEqual("Assets/Generated/TableData/Building", TableDataPaths.BuildingOutputFolder);
            Assert.AreEqual("Assets/Generated/TableData/Building/BuildingCatalog.asset",
                TableDataPaths.BuildingCatalogAssetPath);
        }

        [Test]
        public void AssetPath_UsesTheRawIdWithoutNormalizing()
        {
            Assert.AreEqual("Assets/Generated/TableData/Building/Building_1.asset",
                TableDataPaths.BuildingAssetPath("1"));
            Assert.AreEqual("Assets/Generated/TableData/Building/Building_guild_hall.asset",
                TableDataPaths.BuildingAssetPath("guild_hall"));
        }

        // ---- 행 검증: 통과하는 행 ----

        [Test]
        public void ValidRow_EntersSnapshotWithItsAuthoredValues()
        {
            TableDataSnapshot snapshot = Validate(
                out TableDataDiagnosticLog log,
                Row("1", "7", "1", "1", "1001", "60", "jewel", "2000", "", "", "10", "1"));

            Assert.AreEqual(0, log.ErrorCount, Describe(log));
            Assert.AreEqual(1, snapshot.Buildings.Count);

            BuildingRow row = snapshot.Buildings[0];
            Assert.AreEqual("1", row.Id);
            Assert.AreEqual(60, row.BuildTimeSeconds);
            Assert.AreEqual("jewel", row.CostCurrencyId);
            Assert.AreEqual(2000, row.CostCurrencyAmount);
            Assert.AreEqual(0, row.ItemCosts.Count, "아이템 비용이 없는 행은 칸을 하나도 만들지 않는다.");
            Assert.AreEqual(10, row.DisplayOrder);
            Assert.IsTrue(row.Enabled);
            Assert.AreSame(row, snapshot.BuildingsById["1"]);
        }

        [Test]
        public void EmptyItemCost_IsNormalAndReportsNothing()
        {
            // 재화만 내는 건물은 흔하다. 경고조차 남기지 않아야 정상 상태가 매번 눈에 걸리지 않는다.
            TableDataSnapshot snapshot = Validate(
                out TableDataDiagnosticLog log,
                Row("1", "7", "1", "1", "1001", "60", "jewel", "2000", "", "", "10", "1"));

            Assert.AreEqual(0, log.ErrorCount, Describe(log));
            Assert.AreEqual(0, CountWarnings(log, TableDataColumns.CostItemIds), Describe(log));
            Assert.AreEqual(0, CountWarnings(log, TableDataColumns.CostItemCounts), Describe(log));
            Assert.AreEqual(0, snapshot.Buildings[0].ItemCosts.Count);
        }

        [Test]
        public void NoCostAtAll_IsAllowed_AndLeavesEveryCostFieldEmpty()
        {
            TableDataSnapshot snapshot = Validate(
                out TableDataDiagnosticLog log,
                Row("1", "7", "1", "1", "1001", "0", "", "", "", "", "10", "1"));

            Assert.AreEqual(0, log.ErrorCount, Describe(log));

            BuildingRow row = snapshot.Buildings[0];
            Assert.AreEqual(string.Empty, row.CostCurrencyId);
            Assert.AreEqual(0, row.CostCurrencyAmount);
            Assert.AreEqual(0, row.ItemCosts.Count);
            Assert.AreEqual(0, row.BuildTimeSeconds, "build_time 0은 '즉시 완성'이라는 정상적인 값이다.");
        }

        [Test]
        public void ItemCosts_AreReadInTheAuthoredOrderWithTheirCounts()
        {
            TableDataSnapshot snapshot = Validate(
                out TableDataDiagnosticLog log,
                Row("1", "7", "1", "1", "1001", "60", "jewel", "2000", "50001|50000", "3|7", "10", "1"));

            Assert.AreEqual(0, log.ErrorCount, Describe(log));

            List<BuildingItemCostRow> costs = snapshot.Buildings[0].ItemCosts;
            Assert.AreEqual(2, costs.Count);
            Assert.AreEqual("50001", costs[0].ItemId, "CSV에 적힌 차례를 정렬하지 않는다.");
            Assert.AreEqual(3, costs[0].Count);
            Assert.AreEqual("50000", costs[1].ItemId);
            Assert.AreEqual(7, costs[1].Count);
        }

        // ---- 행 검증: 걸러야 하는 행 ----

        [Test]
        public void EmptyId_IsAnErrorAndTheRowDoesNotEnterTheSnapshot()
        {
            TableDataSnapshot snapshot = Validate(
                out TableDataDiagnosticLog log,
                Row("", "7", "1", "1", "1001", "60", "jewel", "2000", "", "", "10", "1"));

            Assert.AreEqual(1, CountErrors(log, TableDataColumns.BuildingId), Describe(log));
            Assert.AreEqual(0, snapshot.Buildings.Count);
        }

        [Test]
        public void DuplicateId_KeepsTheFirstRowOnly()
        {
            TableDataSnapshot snapshot = Validate(
                out TableDataDiagnosticLog log,
                Row("1", "7", "1", "1", "1001", "60", "jewel", "2000", "", "", "10", "1"),
                Row("1", "7", "1", "1", "1001", "90", "jewel", "3000", "", "", "20", "1"));

            Assert.AreEqual(1, CountErrors(log, TableDataColumns.BuildingId), Describe(log));
            Assert.AreEqual(1, snapshot.Buildings.Count);
            Assert.AreEqual(60, snapshot.Buildings[0].BuildTimeSeconds, "먼저 나온 행이 남아야 한다.");
        }

        [Test]
        public void NegativeBuildTime_IsAnError()
        {
            Validate(out TableDataDiagnosticLog log,
                Row("1", "7", "1", "1", "1001", "-1", "jewel", "2000", "", "", "10", "1"));

            Assert.AreEqual(1, CountErrors(log, TableDataColumns.BuildTime), Describe(log));
        }

        [Test]
        public void BuildTimeInTheHumanReadableNotation_IsAnError()
        {
            // '00:01:00'은 $build_time 참조 컬럼의 표기다. 초 칸에 그 표기가 들어오면 조용히 0으로
            // 읽어서는 안 된다 - 건설 시간이 통째로 사라진다.
            Validate(out TableDataDiagnosticLog log,
                Row("1", "7", "1", "1", "1001", "00:01:00", "jewel", "2000", "", "", "10", "1"));

            Assert.AreEqual(1, CountErrors(log, TableDataColumns.BuildTime), Describe(log));
        }

        [Test]
        public void MissingFunctionReference_IsAnErrorOnEnabledRows()
        {
            Validate(out TableDataDiagnosticLog log,
                Row("1", "7", "1", "", "", "60", "jewel", "2000", "", "", "10", "1"));

            Assert.AreEqual(1, CountErrors(log, TableDataColumns.FunctionCategory), Describe(log));
        }

        [Test]
        public void HalfFilledFunctionReference_IsAnError()
        {
            Validate(out TableDataDiagnosticLog log,
                Row("1", "7", "1", "1", "", "60", "jewel", "2000", "", "", "10", "1"));

            Assert.AreEqual(1, CountErrors(log, TableDataColumns.FunctionKey), Describe(log));
        }

        [Test]
        public void FunctionReference_ThatPointsAtAMissingEntry_IsAnError()
        {
            Validate(out TableDataDiagnosticLog log,
                Row("1", "7", "1", "1", "999999", "60", "jewel", "2000", "", "", "10", "1"));

            Assert.AreEqual(1, CountErrors(log, TableDataColumns.FunctionKey), Describe(log));
        }

        [Test]
        public void CurrencyAmountWithoutAnId_IsAnError()
        {
            Validate(out TableDataDiagnosticLog log,
                Row("1", "7", "1", "1", "1001", "60", "", "2000", "", "", "10", "1"));

            Assert.AreEqual(1, CountErrors(log, TableDataColumns.CostCurrencyId), Describe(log));
        }

        [Test]
        public void CurrencyIdWithoutAnAmount_IsAnError()
        {
            Validate(out TableDataDiagnosticLog log,
                Row("1", "7", "1", "1", "1001", "60", "jewel", "", "", "", "10", "1"));

            Assert.AreEqual(1, CountErrors(log, TableDataColumns.CostCurrencyAmount), Describe(log));
        }

        [Test]
        public void NegativeCurrencyAmount_IsAnError()
        {
            Validate(out TableDataDiagnosticLog log,
                Row("1", "7", "1", "1", "1001", "60", "jewel", "-1", "", "", "10", "1"));

            Assert.AreEqual(1, CountErrors(log, TableDataColumns.CostCurrencyAmount), Describe(log));
        }

        [Test]
        public void ZeroCurrencyAmount_IsAllowed()
        {
            TableDataSnapshot snapshot = Validate(
                out TableDataDiagnosticLog log,
                Row("1", "7", "1", "1", "1001", "60", "jewel", "0", "", "", "10", "1"));

            Assert.AreEqual(0, log.ErrorCount, Describe(log));
            Assert.AreEqual("jewel", snapshot.Buildings[0].CostCurrencyId);
            Assert.AreEqual(0, snapshot.Buildings[0].CostCurrencyAmount);
        }

        [Test]
        public void UnknownCurrency_IsAnError()
        {
            Validate(out TableDataDiagnosticLog log,
                Row("1", "7", "1", "1", "1001", "60", "gold", "10", "", "", "10", "1"));

            Assert.AreEqual(1, CountErrors(log, TableDataColumns.CostCurrencyId), Describe(log));
        }

        [Test]
        public void DisabledCurrency_IsAnError_EvenWhenTheBuildingItselfIsDisabled()
        {
            // 비활성 행의 잘못된 참조를 통과시키면 다시 켜는 순간 조용히 깨진다.
            TableDataSnapshot snapshot = SeededSnapshot(currencyEnabled: false);
            RunValidate(snapshot, out TableDataDiagnosticLog log,
                Row("1", "7", "1", "1", "1001", "60", "jewel", "2000", "", "", "10", "0"));

            Assert.AreEqual(1, CountErrors(log, TableDataColumns.CostCurrencyId), Describe(log));
        }

        [Test]
        public void ItemIdsWithoutCounts_IsAnError()
        {
            Validate(out TableDataDiagnosticLog log,
                Row("1", "7", "1", "1", "1001", "60", "jewel", "2000", "50000", "", "10", "1"));

            Assert.AreEqual(1, CountErrors(log, TableDataColumns.CostItemCounts), Describe(log));
        }

        [Test]
        public void CountsWithoutItemIds_IsAnError()
        {
            Validate(out TableDataDiagnosticLog log,
                Row("1", "7", "1", "1", "1001", "60", "jewel", "2000", "", "3", "10", "1"));

            Assert.AreEqual(1, CountErrors(log, TableDataColumns.CostItemIds), Describe(log));
        }

        [Test]
        public void MismatchedItemAndCountLengths_IsASingleError()
        {
            // 원인 하나에 진단 하나 - 길이가 다른 것을 알린 뒤 항목별 오류를 덧붙이지 않는다.
            TableDataSnapshot snapshot = Validate(
                out TableDataDiagnosticLog log,
                Row("1", "7", "1", "1", "1001", "60", "jewel", "2000", "50000|50001", "3", "10", "1"));

            Assert.AreEqual(1, log.ErrorCount, Describe(log));
            Assert.AreEqual(1, CountErrors(log, TableDataColumns.CostItemCounts), Describe(log));
            Assert.AreEqual(0, snapshot.Buildings[0].ItemCosts.Count);
        }

        [Test]
        public void ZeroItemCount_IsAnError()
        {
            TableDataSnapshot snapshot = Validate(
                out TableDataDiagnosticLog log,
                Row("1", "7", "1", "1", "1001", "60", "jewel", "2000", "50000", "0", "10", "1"));

            Assert.AreEqual(1, CountErrors(log, TableDataColumns.CostItemCounts), Describe(log));
            Assert.AreEqual(0, snapshot.Buildings[0].ItemCosts.Count,
                "0개짜리 비용 칸은 만들어지면 안 된다 - 비용이 적혀 있는데 공짜인 행이 된다.");
        }

        [Test]
        public void UnknownItemCost_IsAnError()
        {
            Validate(out TableDataDiagnosticLog log,
                Row("1", "7", "1", "1", "1001", "60", "jewel", "2000", "99999", "1", "10", "1"));

            Assert.AreEqual(1, CountErrors(log, TableDataColumns.CostItemIds), Describe(log));
        }

        [Test]
        public void DisabledItemCost_IsAnError()
        {
            Validate(out TableDataDiagnosticLog log,
                Row("1", "7", "1", "1", "1001", "60", "jewel", "2000", "50002", "1", "10", "1"));

            Assert.AreEqual(1, CountErrors(log, TableDataColumns.CostItemIds), Describe(log));
        }

        [Test]
        public void EnabledColumn_AcceptsOnlyOneOrZero()
        {
            Validate(out TableDataDiagnosticLog log,
                Row("1", "7", "1", "1", "1001", "60", "jewel", "2000", "", "", "10", "true"));

            Assert.AreEqual(1, CountErrors(log, TableDataColumns.Enabled), Describe(log));
        }

        // ---- 실제 CSV ----

        [Test]
        public void LiveCsv_HasExactlyTheInnRow_WithItsAuthoredValues()
        {
            TableDataSnapshot snapshot = Live().Snapshot;
            Assert.IsNotNull(snapshot, "아홉 표가 모두 읽혀야 스냅샷이 만들어진다: " + Live().Summary);

            Assert.AreEqual(1, snapshot.Buildings.Count, "지금 Building.csv에는 여관 한 행만 있다.");

            BuildingRow row = snapshot.Buildings[0];
            Assert.AreEqual(LiveBuildingId, row.Id);
            Assert.AreEqual(LiveBuildTimeSeconds, row.BuildTimeSeconds);
            Assert.AreEqual(LiveCostCurrencyId, row.CostCurrencyId);
            Assert.AreEqual(LiveCostCurrencyAmount, row.CostCurrencyAmount);
            Assert.AreEqual(0, row.ItemCosts.Count, "여관은 아이템 비용이 없다.");
            Assert.AreEqual(LiveDisplayOrder, row.DisplayOrder);
            Assert.IsTrue(row.Enabled);
        }

        [Test]
        public void LiveCsv_BindsTheNameAndFunctionToTheExpectedEntries()
        {
            // 두 참조가 <b>다른 표</b>를 가리키는 것이 이 행의 핵심이다 - 이름은 07_Building,
            // 기능은 01_UI다. GUID나 Key Id가 조금이라도 어긋나면 화면에 빈 문구가 나오는데,
            // "행이 하나 있다" 같은 검사로는 그 상태를 절대 잡을 수 없다.
            BuildingRow row = Live().Snapshot.Buildings[0];

            Assert.IsTrue(row.Name.Resolved, "여관 이름 참조가 해석되어야 한다.");
            Assert.AreEqual(BuildingTableGuid, row.Name.TableGuid.ToString("N"));
            Assert.AreEqual(BuildingNameKeyId, row.Name.KeyId);

            Assert.IsTrue(row.FunctionName.Resolved, "해금 기능 이름 참조가 해석되어야 한다.");
            Assert.AreEqual(UiTableGuid, row.FunctionName.TableGuid.ToString("N"));
            Assert.AreEqual(UiFunctionKeyId, row.FunctionName.KeyId);

            Assert.AreNotEqual(row.Name.TableGuid, row.FunctionName.TableGuid,
                "전제 확인 - 두 참조가 서로 다른 표를 가리키는 행이어야 이 시험이 의미가 있다.");
        }

        [Test]
        public void LiveCsv_CostCurrency_PointsAtAnActiveCurrencyRow()
        {
            TableDataSnapshot snapshot = Live().Snapshot;
            BuildingRow row = snapshot.Buildings[0];

            Assert.IsTrue(snapshot.CurrenciesById.TryGetValue(row.CostCurrencyId, out CurrencyRow currency),
                $"Building.csv의 '{row.CostCurrencyId}'가 Currency.csv에 없다.");
            Assert.IsTrue(currency.Enabled, "건설 비용은 활성 재화만 가리킬 수 있다.");
        }

        [Test]
        public void LiveValidation_HasNoErrors()
        {
            Assert.AreEqual(0, Live().ErrorCount, Describe(Live().Log));
        }

        // ---- 카탈로그 정렬 ----

        [Test]
        public void CatalogOrder_IsDisplayOrderThenIdOrdinal_AndSkipsDisabledRows()
        {
            var rows = new List<BuildingRow>
            {
                CatalogRow("guild_hall", 60, enabled: true),
                CatalogRow("hidden", 5, enabled: false),
                CatalogRow("2", 10, enabled: true),
                CatalogRow("1", 10, enabled: true),
                CatalogRow("blacksmith", 20, enabled: true),
            };

            var assets = new Dictionary<string, BuildingDefinition>(StringComparer.Ordinal);
            foreach (BuildingRow row in rows) assets[row.Id] = NewBuilding(row.Id);

            CollectionAssert.AreEqual(
                new[] { "1", "2", "blacksmith", "guild_hall" },
                IdsOf(SortBuildings(rows, assets)),
                "display_order 오름차순 → 같으면 building_id Ordinal 오름차순이어야 하고, " +
                "enabled=0인 행은 들어가면 안 된다.");
        }

        [Test]
        public void CatalogOrder_IsDeterministicAcrossRepeatedSorts()
        {
            var rows = new List<BuildingRow>
            {
                CatalogRow("guild_hall", 10, enabled: true),
                CatalogRow("1", 10, enabled: true),
                CatalogRow("blacksmith", 10, enabled: true),
            };

            var assets = new Dictionary<string, BuildingDefinition>(StringComparer.Ordinal);
            foreach (BuildingRow row in rows) assets[row.Id] = NewBuilding(row.Id);

            List<string> first = IdsOf(SortBuildings(rows, assets));
            List<string> second = IdsOf(SortBuildings(rows, assets));

            CollectionAssert.AreEqual(first, second, "같은 입력은 언제나 같은 순서를 내야 한다.");
            CollectionAssert.AreEqual(new[] { "1", "blacksmith", "guild_hall" }, first,
                "동률은 Ordinal id로만 갈린다 - 목록에 적힌 차례가 끼어들면 안 된다.");
        }

        // ---- 표의 enabled가 정의 에셋까지 온다 ----

        [Test]
        public void WrittenDefinition_CarriesTheEnabledValueOfItsRow()
        {
            // enabled는 카탈로그에 들어가는지만 정하는 값이 아니라 <b>표가 이 건물에 대해 적어 둔 값</b>
            // 이므로, 켜진 행이든 꺼진 행이든 정의 에셋에 그대로 적혀 있어야 한다.
            BuildingDefinition on = NewBuilding("1");
            BuildingDefinition off = NewBuilding("hidden");

            WriteBuilding(on, CatalogRow("1", 10, enabled: true));
            WriteBuilding(off, CatalogRow("hidden", 5, enabled: false));

            Assert.IsTrue(on.Enabled, "enabled=1인 행의 정의는 Enabled가 true여야 한다.");
            Assert.IsFalse(off.Enabled, "enabled=0인 행의 정의는 Enabled가 false로 남아야 한다.");
        }

        [Test]
        public void WrittenDefinition_OverwritesAStaleEnabledValue()
        {
            // 표에서 건물을 껐는데 예전 에셋이 켜진 채로 남아 있으면 "표에는 없는데 켜져 있는" 정의가
            // 된다 - 비용 재화 세 칸과 같은 규칙으로 매번 덮어쓴다.
            BuildingDefinition building = NewBuilding("1");

            WriteBuilding(building, CatalogRow("1", 10, enabled: true));
            Assert.IsTrue(building.Enabled, "전제 확인 - 먼저 켜진 값으로 적어 둔다.");

            WriteBuilding(building, CatalogRow("1", 10, enabled: false));
            Assert.IsFalse(building.Enabled, "표가 꺼지면 에셋의 값도 함께 꺼져야 한다.");
        }

        [Test]
        public void DisabledRow_KeepsEnabledFalseOnItsAsset_ButStaysOutOfTheCatalog()
        {
            // 두 규칙이 <b>함께</b> 성립해야 한다: 꺼진 행도 정의 에셋은 남고(다시 켤 때 GUID와 참조가
            // 살아난다), 그 에셋은 목록에 나오지 않는다.
            var rows = new List<BuildingRow>
            {
                CatalogRow("1", 10, enabled: true),
                CatalogRow("hidden", 5, enabled: false),
            };

            var assets = new Dictionary<string, BuildingDefinition>(StringComparer.Ordinal);
            foreach (BuildingRow row in rows)
            {
                BuildingDefinition asset = NewBuilding(row.Id);
                WriteBuilding(asset, row);
                assets[row.Id] = asset;
            }

            CollectionAssert.AreEqual(new[] { "1" }, IdsOf(SortBuildings(rows, assets)),
                "enabled=0인 행은 카탈로그에 들어가면 안 된다.");

            Assert.IsFalse(assets["hidden"].Enabled,
                "목록에서 빠졌다고 정의 에셋이 사라지거나 값이 뒤집히면 안 된다 - Enabled는 false로 남는다.");
            Assert.AreEqual("hidden", assets["hidden"].BuildingId,
                "꺼진 행의 정의도 building_id를 그대로 들고 있어야 다시 켤 때 같은 에셋이 쓰인다.");
            Assert.IsTrue(assets["1"].Enabled, "카탈로그에 들어간 건물의 Enabled는 true다.");
        }

        // ---- 비용 요청 변환 ----

        [Test]
        public void CostRequest_CarriesTheCurrencyAmount_AndNoItemsWhenThereAreNone()
        {
            BuildingDefinition building = NewBuilding("1", currencyId: "jewel", currencyAmount: 2000);

            InventoryCostRequest request = building.ToCostRequest();

            Assert.AreEqual(2000, request.Currency);
            Assert.AreEqual(0, request.ItemCosts.Count);
            Assert.IsNotNull(request.ItemCosts, "빈 목록이지 null이 아니다.");
        }

        [Test]
        public void CostRequest_CarriesEveryItemCostInOrder_WithItsIdAndDefinition()
        {
            ItemDefinition potion = NewItem("50000");
            ItemDefinition herb = NewItem("50001");

            BuildingDefinition building = NewBuilding("1", currencyId: "jewel", currencyAmount: 100,
                itemCosts: new[] { ("50001", herb, 3), ("50000", potion, 7) });

            InventoryCostRequest request = building.ToCostRequest();

            Assert.AreEqual(100, request.Currency);
            Assert.AreEqual(2, request.ItemCosts.Count);

            Assert.AreEqual("50001", request.ItemCosts[0].ItemId, "적힌 차례를 바꾸지 않는다.");
            Assert.AreEqual(3, request.ItemCosts[0].Count);
            Assert.AreSame(herb, request.ItemCosts[0].Definition);

            Assert.AreEqual("50000", request.ItemCosts[1].ItemId);
            Assert.AreEqual(7, request.ItemCosts[1].Count);
            Assert.AreSame(potion, request.ItemCosts[1].Definition);
        }

        [Test]
        public void CostRequest_KeepsTheAuthoredItemId_EvenWhenTheLinkedDefinitionDisagrees()
        {
            // 저장 파일의 키는 언제나 id 문자열이다. 참조가 다른 아이템을 가리키고 있으면 그 참조를
            // 따라가지 않고 id 쪽을 남긴다 - 조용히 다른 아이템을 깎는 것보다 낫다.
            ItemDefinition other = NewItem("50004");

            BuildingDefinition building = NewBuilding("1",
                itemCosts: new[] { ("50000", other, 2) });

            InventoryCostRequest request = building.ToCostRequest();

            Assert.AreEqual(1, request.ItemCosts.Count);
            Assert.AreEqual("50000", request.ItemCosts[0].ItemId);
            Assert.IsNull(request.ItemCosts[0].Definition,
                "id가 어긋난 참조는 실어 보내지 않는다 - 판정의 기준은 언제나 Item Id다.");
        }

        [Test]
        public void CostRequest_IsSideEffectFree_AndRepeatable()
        {
            ItemDefinition potion = NewItem("50000");
            BuildingDefinition building = NewBuilding("1", currencyId: "jewel", currencyAmount: 2000,
                itemCosts: new[] { ("50000", potion, 4) });

            InventoryCostRequest first = building.ToCostRequest();
            InventoryCostRequest second = building.ToCostRequest();

            Assert.AreNotSame(first, second, "매번 새 값을 만들어 돌려준다(공유 상태를 두지 않는다).");
            Assert.AreEqual(first.Currency, second.Currency);
            Assert.AreEqual(first.ItemCosts.Count, second.ItemCosts.Count);
            Assert.AreEqual(first.ItemCosts[0].ItemId, second.ItemCosts[0].ItemId);
            Assert.AreEqual(first.ItemCosts[0].Count, second.ItemCosts[0].Count);

            // 정의 자체도 달라지지 않는다 - 변환은 읽기만 한다.
            Assert.AreEqual("1", building.BuildingId);
            Assert.AreEqual(2000, building.CostCurrencyAmount);
            Assert.AreEqual(1, building.CostItems.Count);
            Assert.AreEqual(4, building.CostItems[0].Count);
        }

        [Test]
        public void CostRequest_NeverEvaluatesOrSpendsOrSaves()
        {
            // 판정과 차감은 InventoryManager 하나의 몫이다. 정의 에셋이 그 경로를 직접 부르기 시작하면
            // "인벤토리를 바꾸는 경로는 하나"라는 규칙이 코드가 아니라 약속으로만 남는다.
            string source = StripComments(ReadDefinitionSource());

            foreach (string forbidden in new[]
                     {
                         "EvaluateCost", "TrySpendCost", "TrySpendCurrency", "AddItem", "AddCurrency",
                         "SaveSystem", "ApplyRewards",
                     })
            {
                Assert.IsFalse(source.Contains(forbidden),
                    $"BuildingDefinition.cs가 '{forbidden}'을 참조합니다 - 비용 변환은 값을 만들기만 해야 합니다.");
            }
        }

        [Test]
        public void SaveVersion_IsUnchanged()
        {
            // 이번 단계는 저장 형식을 건드리지 않는다 - 건물의 진행 상태는 아직 저장되지 않는다.
            Assert.AreEqual(2, SaveData.CurrentSaveVersion,
                "Building 표를 더하면서 저장 형식 번호가 올라가면 안 됩니다.");
        }

        // ---- 도우미 ----

        private static TableDataValidationResult Live()
        {
            return liveResult ?? (liveResult = TableDataValidator.Validate());
        }

        /// <summary>주석 줄을 걷어 낸 소스. 문서 주석은 <c>InventoryManager.EvaluateCost</c> 같은
        /// 이름을 <b>설명하려고</b> 적어 두므로, 그대로 훑으면 설명이 호출로 읽힌다.</summary>
        private static string StripComments(string source)
        {
            var kept = new List<string>();
            foreach (string line in source.Split('\n'))
            {
                string trimmed = line.TrimStart();
                if (trimmed.StartsWith("//", StringComparison.Ordinal)) continue;
                kept.Add(line);
            }

            return string.Join("\n", kept);
        }

        private static string ReadDefinitionSource()
        {
            string path = Path.Combine(
                Directory.GetParent(Application.dataPath).FullName,
                Path.Combine("Assets", Path.Combine("Scripts", Path.Combine("Building", "BuildingDefinition.cs"))));

            Assert.IsTrue(System.IO.File.Exists(path), $"'{path}'를 찾지 못했습니다.");
            return System.IO.File.ReadAllText(path);
        }

        /// <summary>메모리 위의 표로 행 검증만 돌린다. 파일도 에셋도 건드리지 않는다.</summary>
        private static TableDataSnapshot Validate(out TableDataDiagnosticLog log, params string[][] rows)
        {
            TableDataSnapshot snapshot = SeededSnapshot(currencyEnabled: true);
            RunValidate(snapshot, out log, rows);
            return snapshot;
        }

        private static void RunValidate(
            TableDataSnapshot snapshot, out TableDataDiagnosticLog log, params string[][] rows)
        {
            var records = new List<CsvRecord>();
            for (int i = 0; i < rows.Length; i++) records.Add(new CsvRecord(i + 2, rows[i]));

            var table = new CsvTable(File, TableDataColumns.Building, records);
            log = new TableDataDiagnosticLog();

            ValidateBuildingsMethod.Invoke(null, new object[] { table, snapshot, log });
        }

        /// <summary>
        /// Building이 가리킬 Currency / Item 행을 미리 심어 둔 스냅샷. Building.csv는 이 두 표 뒤에
        /// 읽히므로, 행 검증만 따로 돌릴 때도 앞의 표가 완성되어 있어야 실제와 같은 조건이 된다.
        /// 50002는 <b>일부러 비활성</b>이다 - 비활성 참조를 막는 규칙을 확인하기 위한 것이다.
        /// </summary>
        private static TableDataSnapshot SeededSnapshot(bool currencyEnabled)
        {
            var snapshot = new TableDataSnapshot();

            var jewel = new CurrencyRow { Line = 2, Id = "jewel", Enabled = currencyEnabled };
            snapshot.Currencies.Add(jewel);
            snapshot.CurrenciesById[jewel.Id] = jewel;

            foreach ((string id, bool enabled) in new[]
                     {
                         ("50000", true), ("50001", true), ("50002", false), ("50004", true),
                     })
            {
                var item = new ItemRow { Line = 2, Id = id, Enabled = enabled };
                snapshot.Items.Add(item);
                snapshot.ItemsById[id] = item;
            }

            return snapshot;
        }

        /// <summary>컬럼 순서는 <see cref="TableDataColumns.Building"/>과 같다(memo는 늘 비운다).</summary>
        private static string[] Row(
            string id, string nameCategory, string nameKey, string functionCategory, string functionKey,
            string buildTime, string costCurrencyId, string costCurrencyAmount, string costItemIds,
            string costItemCounts, string displayOrder, string enabled)
        {
            return new[]
            {
                id, nameCategory, nameKey, functionCategory, functionKey, buildTime,
                costCurrencyId, costCurrencyAmount, costItemIds, costItemCounts,
                displayOrder, enabled, string.Empty,
            };
        }

        private static BuildingRow CatalogRow(string id, int displayOrder, bool enabled)
        {
            return new BuildingRow { Line = 2, Id = id, DisplayOrder = displayOrder, Enabled = enabled };
        }

        /// <summary>프로덕션의 쓰기 규칙을 그대로 부른다. 메모리 위의 ScriptableObject에만 적으므로
        /// 디스크에는 아무것도 남지 않는다 - 참조 사전은 비워 둔다(이 시험이 보는 것은 enabled 칸뿐이다).</summary>
        private static void WriteBuilding(BuildingDefinition asset, BuildingRow row)
        {
            MethodInfo write = typeof(TableDataRebuilder).GetMethod(
                "WriteBuilding", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(write, "TableDataRebuilder.WriteBuilding을 찾지 못했습니다.");

            write.Invoke(null, new object[]
            {
                asset, row,
                new Dictionary<string, CurrencyDefinition>(StringComparer.Ordinal),
                new Dictionary<string, ItemDefinition>(StringComparer.Ordinal),
            });
        }

        /// <summary>프로덕션의 정렬 규칙을 그대로 부른다 - 시험이 자기 규칙을 다시 적으면 아무것도
        /// 증명하지 못한다.</summary>
        private static List<BuildingDefinition> SortBuildings(
            List<BuildingRow> rows, Dictionary<string, BuildingDefinition> assets)
        {
            MethodInfo sort = typeof(TableDataRebuilder).GetMethod(
                "SortForCatalog", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(sort, "TableDataRebuilder.SortForCatalog를 찾지 못했습니다.");

            MethodInfo generic = sort.MakeGenericMethod(typeof(BuildingRow), typeof(BuildingDefinition));

            Func<BuildingRow, bool> enabled = r => r.Enabled;
            Func<BuildingRow, int> order = r => r.DisplayOrder;
            Func<BuildingRow, string> id = r => r.Id;

            return (List<BuildingDefinition>)generic.Invoke(null, new object[] { rows, enabled, order, id, assets });
        }

        private static List<string> IdsOf(List<BuildingDefinition> buildings)
        {
            var ids = new List<string>();
            foreach (BuildingDefinition building in buildings) ids.Add(building.BuildingId);
            return ids;
        }

        /// <summary>메모리에만 존재하는 BuildingDefinition. 에셋으로 저장하지 않으므로 프로젝트에 남지 않는다.</summary>
        private BuildingDefinition NewBuilding(
            string id, string currencyId = "", int currencyAmount = 0,
            (string ItemId, ItemDefinition Item, int Count)[] itemCosts = null)
        {
            var building = ScriptableObject.CreateInstance<BuildingDefinition>();
            created.Add(building);

            var serialized = new SerializedObject(building);
            serialized.FindProperty("buildingId").stringValue = id;
            serialized.FindProperty("costCurrencyId").stringValue = currencyId;
            serialized.FindProperty("costCurrencyAmount").intValue = currencyAmount;

            SerializedProperty list = serialized.FindProperty("costItems");
            list.arraySize = itemCosts?.Length ?? 0;

            for (int i = 0; i < (itemCosts?.Length ?? 0); i++)
            {
                SerializedProperty element = list.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("itemId").stringValue = itemCosts[i].ItemId;
                element.FindPropertyRelative("item").objectReferenceValue = itemCosts[i].Item;
                element.FindPropertyRelative("count").intValue = itemCosts[i].Count;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            return building;
        }

        private ItemDefinition NewItem(string id)
        {
            var item = ScriptableObject.CreateInstance<ItemDefinition>();
            created.Add(item);

            var serialized = new SerializedObject(item);
            serialized.FindProperty("itemId").stringValue = id;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return item;
        }

        private static int CountErrors(TableDataDiagnosticLog log, string column)
        {
            return Count(log, TableDataSeverity.Error, column);
        }

        private static int CountWarnings(TableDataDiagnosticLog log, string column)
        {
            return Count(log, TableDataSeverity.Warning, column);
        }

        private static int Count(TableDataDiagnosticLog log, TableDataSeverity severity, string column)
        {
            int count = 0;
            foreach (TableDataDiagnostic diagnostic in log.Entries)
            {
                if (diagnostic.Severity == severity
                    && string.Equals(diagnostic.Column, column, StringComparison.Ordinal))
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>실패 메시지에 진단 전문을 붙인다 - "오류 1건을 기대했는데 2건"만으로는 원인을 알 수 없다.</summary>
        private static string Describe(TableDataDiagnosticLog log)
        {
            var lines = new List<string>();
            foreach (TableDataDiagnostic diagnostic in log.Entries) lines.Add(diagnostic.ToString());
            return lines.Count == 0 ? "(진단 없음)" : "\n" + string.Join("\n", lines);
        }
    }
}
