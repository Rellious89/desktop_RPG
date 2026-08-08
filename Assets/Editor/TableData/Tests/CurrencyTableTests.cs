using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace TableDataEditor.Tests
{
    /// <summary>
    /// Currency.csv 파이프라인 시험. <b>파일을 쓰지도 에셋을 만들지도 않는다</b> - 행 검증은 메모리에서
    /// 만든 <see cref="CsvTable"/>로 돌리고, 실제 데이터 확인은 읽기 전용인
    /// <see cref="TableDataValidator.Validate"/>만 쓴다. Rebuild는 프로젝트를 바꾸므로 여기서 부르지
    /// 않는다(생성 결과 확인은 클론에서 따로 돌린다).
    ///
    /// <b>private 정적 메서드 두 개를 리플렉션으로 부른다.</b> 시험을 위해 프로덕션에 공개 이음매를
    /// 새로 뚫는 대신 이 쪽을 골랐다 - 규칙 하나를 확인하려고 API 표면을 넓히면, 그 API가 실제로는
    /// 아무 데서도 쓰이지 않는데도 계약처럼 남는다. 이름이 바뀌면 <see cref="SetUpFixture"/>에서
    /// 곧바로 실패하므로 조용히 통과하는 경로는 없다.
    /// </summary>
    public sealed class CurrencyTableTests
    {
        private const string File = TableDataPaths.CurrencyCsvFileName;

        private static readonly MethodInfo ValidateCurrenciesMethod =
            typeof(TableDataValidator).GetMethod(
                "ValidateCurrencies", BindingFlags.NonPublic | BindingFlags.Static);

        private static readonly MethodInfo CheckCurrencyReferenceMethod =
            typeof(TableDataValidator).GetMethod(
                "CheckCurrencyReference", BindingFlags.NonPublic | BindingFlags.Static);

        /// <summary>실제 CSV를 읽는 검증은 프로젝트 전체 Sprite 인덱스를 만드는 무거운 동작이라
        /// 한 번만 돌리고 결과를 나눠 쓴다. 읽기 전용이므로 시험 사이에 상태가 새지 않는다.</summary>
        private static TableDataValidationResult liveResult;

        /// <summary>시험이 메모리에서 만든 Sprite/Texture. 디스크에는 아무것도 남지 않는다.</summary>
        private readonly List<UnityEngine.Object> created = new List<UnityEngine.Object>();

        [SetUp]
        public void SetUpFixture()
        {
            Assert.IsNotNull(ValidateCurrenciesMethod,
                "TableDataValidator.ValidateCurrencies를 찾지 못했습니다 - 이름이 바뀌었다면 이 시험도 함께 고치세요.");
            Assert.IsNotNull(CheckCurrencyReferenceMethod,
                "TableDataValidator.CheckCurrencyReference를 찾지 못했습니다 - 이름이 바뀌었다면 이 시험도 함께 고치세요.");
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
                new[] { "currency_id", "name_category", "name_key", "icon_key", "display_order", "enabled", "memo" },
                TableDataColumns.Currency,
                "Currency.csv의 필수 컬럼과 순서가 약속과 달라졌습니다.");
        }

        [Test]
        public void Schema_DoesNotIncludeTheReferenceOnlyNameColumn()
        {
            CollectionAssert.DoesNotContain(TableDataColumns.Currency, "$currency_name",
                "$currency_name은 작업자용 참조 컬럼이라 필수 컬럼이 되면 안 된다.");
            Assert.IsTrue(TableDataCsvReader.IsReferenceOnlyColumn("$currency_name"),
                "$currency_name은 기존 참조 컬럼 정책으로 통과해야 한다.");
        }

        [Test]
        public void Schema_SharesTheCurrencyIdColumnNameWithMonsterCsv()
        {
            // 두 표가 같은 상수를 가리켜야 참조가 성립한다 - 이름을 두 번 적어 두면 한쪽만 고쳐질 수 있다.
            CollectionAssert.Contains(TableDataColumns.Currency, TableDataColumns.CurrencyId);
            CollectionAssert.Contains(TableDataColumns.Monster, TableDataColumns.CurrencyId);
        }

        [Test]
        public void Paths_AreTheAgreedLocations()
        {
            Assert.AreEqual("Assets/TableData/Game/Currency.csv", TableDataPaths.CurrencyCsvPath);
            Assert.AreEqual("Assets/Art/Currency", TableDataPaths.CurrencyIconRoot);
            Assert.AreEqual("Assets/Generated/TableData/Currency", TableDataPaths.CurrencyOutputFolder);
            Assert.AreEqual("Assets/Generated/TableData/Currency/CurrencyCatalog.asset",
                TableDataPaths.CurrencyCatalogAssetPath);
        }

        [Test]
        public void AssetPath_UsesTheRawIdWithoutNormalizing()
        {
            Assert.AreEqual("Assets/Generated/TableData/Currency/Currency_jewel.asset",
                TableDataPaths.CurrencyAssetPath("jewel"));

            // 대소문자를 맞추거나 밑줄을 손보지 않는다 - 파일 이름은 언제나 '접두사 + 적힌 id'다.
            Assert.AreEqual("Assets/Generated/TableData/Currency/Currency_gold_bar.asset",
                TableDataPaths.CurrencyAssetPath("gold_bar"));
        }

        // ---- 행 검증 ----

        [Test]
        public void ValidRow_EntersSnapshotWithItsAuthoredValues()
        {
            TableDataSnapshot snapshot = Validate(out TableDataDiagnosticLog log, Row("jewel", "5", "1", "", "10", "1"));

            Assert.AreEqual(0, log.ErrorCount, Describe(log));
            Assert.AreEqual(1, snapshot.Currencies.Count);

            CurrencyRow row = snapshot.Currencies[0];
            Assert.AreEqual("jewel", row.Id);
            Assert.AreEqual(10, row.DisplayOrder);
            Assert.IsTrue(row.Enabled);
            Assert.IsTrue(row.Name.Resolved, "카테고리 5 / 키 1은 프로젝트에 실제로 있는 Entry여야 한다.");
            Assert.AreSame(row, snapshot.CurrenciesById["jewel"]);
        }

        [Test]
        public void BlankIcon_IsAWarningAndLeavesTheIconEmpty()
        {
            // Assets/Art/Currency 폴더가 없어도 이 행은 통과해야 한다 - 아이콘은 선택 항목이다.
            TableDataSnapshot snapshot = Validate(out TableDataDiagnosticLog log, Row("jewel", "5", "1", "", "10", "1"));

            Assert.AreEqual(0, log.ErrorCount, Describe(log));
            Assert.AreEqual(1, CountWarnings(log, TableDataColumns.IconKey),
                "빈 icon_key는 경고 한 건으로 알려야 한다.");
            Assert.IsNull(snapshot.Currencies[0].Icon);
        }

        [Test]
        public void NonBlankIcon_ThatResolvesToNothing_IsAWarningAndDoesNotBlock()
        {
            // 재화 아이콘은 완전한 선택 항목이다 - 폴더가 없어 이름을 찾지 못해도 경고에 그치고,
            // 행은 아이콘만 비운 채 그대로 살아남는다(아이템 아이콘과 일부러 다른 정책이다).
            TableDataSnapshot snapshot = Validate(
                out TableDataDiagnosticLog log, Row("jewel", "5", "1", "Icon_Jewel", "10", "1"));

            Assert.AreEqual(0, log.ErrorCount, "아이콘을 찾지 못한 것은 Rebuild를 막을 이유가 아니다: " + Describe(log));
            Assert.AreEqual(0, CountErrors(log, TableDataColumns.IconKey), Describe(log));
            Assert.AreEqual(1, CountWarnings(log, TableDataColumns.IconKey), Describe(log));

            Assert.AreEqual(1, snapshot.Currencies.Count, "아이콘이 없어도 행은 스냅샷에 들어간다.");
            Assert.IsNull(snapshot.Currencies[0].Icon, "찾지 못한 아이콘은 비워 둔다.");
        }

        [Test]
        public void AmbiguousIcon_IsAWarningAndLeavesTheIconEmpty()
        {
            // 같은 이름이 둘이면 어느 것을 쓸지 정할 수 없다 - 임의로 하나를 고르지 않고 비워 두되,
            // 이것도 Rebuild를 막지는 않는다.
            TableDataAssetIndex assets = IndexWithCurrencyIcons("Icon_Jewel", NewSprite(), NewSprite());

            TableDataSnapshot snapshot = Validate(
                assets, out TableDataDiagnosticLog log, Row("jewel", "5", "1", "Icon_Jewel", "10", "1"));

            Assert.AreEqual(0, log.ErrorCount, Describe(log));
            Assert.AreEqual(0, CountErrors(log, TableDataColumns.IconKey), Describe(log));
            Assert.AreEqual(1, CountWarnings(log, TableDataColumns.IconKey), Describe(log));

            Assert.AreEqual(1, snapshot.Currencies.Count);
            Assert.IsNull(snapshot.Currencies[0].Icon, "여럿 중 하나를 임의로 고르면 안 된다.");
        }

        [Test]
        public void IconFoundExactlyOnce_IsAssignedWithoutAnyDiagnostic()
        {
            Sprite only = NewSprite();
            TableDataAssetIndex assets = IndexWithCurrencyIcons("Icon_Jewel", only);

            TableDataSnapshot snapshot = Validate(
                assets, out TableDataDiagnosticLog log, Row("jewel", "5", "1", "Icon_Jewel", "10", "1"));

            Assert.AreEqual(0, log.Entries.Count, "정확히 하나를 찾으면 알릴 것이 없다: " + Describe(log));
            Assert.AreSame(only, snapshot.Currencies[0].Icon, "찾은 아이콘은 그대로 연결한다.");
        }

        [Test]
        public void BlankId_IsAnErrorAndTheRowIsDropped()
        {
            TableDataSnapshot snapshot = Validate(out TableDataDiagnosticLog log, Row("", "5", "1", "", "10", "1"));

            Assert.AreEqual(1, CountErrors(log, TableDataColumns.CurrencyId), Describe(log));
            Assert.AreEqual(0, snapshot.Currencies.Count, "id가 없는 행은 스냅샷에 들어가지 않는다.");
        }

        [Test]
        public void PaddedId_IsAFormatErrorAndIsNeverTrimmed()
        {
            TableDataSnapshot snapshot = Validate(out TableDataDiagnosticLog log, Row("  jewel  ", "5", "1", "", "10", "1"));

            Assert.AreEqual(1, CountErrors(log, TableDataColumns.CurrencyId), Describe(log));
            Assert.AreEqual(0, snapshot.Currencies.Count);
            Assert.IsFalse(snapshot.CurrenciesById.ContainsKey("jewel"),
                "공백을 떼어 통과시키면 CSV와 에셋의 id가 달라진다.");
        }

        [Test]
        public void DuplicateId_IsAnErrorAndTheFirstRowWins()
        {
            TableDataSnapshot snapshot = Validate(out TableDataDiagnosticLog log,
                Row("jewel", "5", "1", "", "10", "1"),
                Row("jewel", "5", "1", "", "20", "1"));

            Assert.AreEqual(1, CountErrors(log, TableDataColumns.CurrencyId), Describe(log));
            Assert.AreEqual(1, snapshot.Currencies.Count);
            Assert.AreEqual(10, snapshot.CurrenciesById["jewel"].DisplayOrder, "먼저 나온 행이 남아야 한다.");
        }

        [Test]
        public void IdsDifferingOnlyByCase_AreDistinctCurrencies()
        {
            // 'Jewel'은 ID 형식(소문자 snake_case)에 맞지 않으므로 형식 오류로 걸린다 - 대문자 id를
            // 조용히 소문자로 바꾸어 'jewel'과 겹치게 만드는 경로가 없다는 것이 여기서 확인하려는 것이다.
            TableDataSnapshot snapshot = Validate(out TableDataDiagnosticLog log,
                Row("jewel", "5", "1", "", "10", "1"),
                Row("Jewel", "5", "1", "", "20", "1"));

            Assert.AreEqual(1, CountErrors(log, TableDataColumns.CurrencyId), Describe(log));
            Assert.AreEqual(1, snapshot.Currencies.Count);
            Assert.AreEqual(10, snapshot.CurrenciesById["jewel"].DisplayOrder,
                "'Jewel'이 'jewel'을 밀어내거나 덮어쓰면 안 된다.");
        }

        [Test]
        public void DisabledRow_StaysInTheSnapshotSoItsAssetIsStillBuilt()
        {
            TableDataSnapshot snapshot = Validate(out TableDataDiagnosticLog log, Row("jewel", "5", "1", "", "10", "0"));

            Assert.AreEqual(0, log.ErrorCount, Describe(log));
            Assert.AreEqual(1, snapshot.Currencies.Count, "enabled=0이어도 Definition은 만들어야 하므로 행은 남는다.");
            Assert.IsFalse(snapshot.Currencies[0].Enabled);
        }

        [Test]
        public void EnabledMustBeExactlyOneOrZero()
        {
            Validate(out TableDataDiagnosticLog log, Row("jewel", "5", "1", "", "10", "true"));

            Assert.AreEqual(1, CountErrors(log, TableDataColumns.Enabled), Describe(log));
        }

        [Test]
        public void EnabledRow_RequiresBothLocalizationCells()
        {
            TableDataSnapshot snapshot = Validate(out TableDataDiagnosticLog log, Row("jewel", "", "", "", "10", "1"));

            Assert.AreEqual(1, CountErrors(log, TableDataColumns.NameCategory), Describe(log));
            Assert.IsFalse(snapshot.Currencies[0].Name.Resolved);
        }

        [Test]
        public void UnknownLocalizationKey_IsAnError()
        {
            Validate(out TableDataDiagnosticLog log, Row("jewel", "5", "999999", "", "10", "1"));

            Assert.AreEqual(1, CountErrors(log, TableDataColumns.NameKey), Describe(log));
        }

        [Test]
        public void UnknownLocalizationCategory_IsAnError()
        {
            Validate(out TableDataDiagnosticLog log, Row("jewel", "999", "1", "", "10", "1"));

            Assert.AreEqual(1, CountErrors(log, TableDataColumns.NameCategory), Describe(log));
        }

        [Test]
        public void DuplicateDisplayOrder_IsAWarningNotAnError()
        {
            TableDataSnapshot snapshot = Validate(out TableDataDiagnosticLog log,
                Row("jewel", "5", "1", "", "10", "1"),
                Row("gold", "5", "1", "", "10", "1"));

            Assert.AreEqual(0, log.ErrorCount, Describe(log));
            Assert.AreEqual(1, CountWarnings(log, TableDataColumns.DisplayOrder), Describe(log));
            Assert.AreEqual(2, snapshot.Currencies.Count, "순서가 겹쳐도 두 행 모두 살아 있어야 한다.");
        }

        // ---- Monster.csv -> Currency.csv 참조 ----

        [Test]
        public void MonsterCurrencyReference_ToAnEnabledRow_IsAccepted()
        {
            TableDataSnapshot snapshot = SnapshotWithCurrency("jewel", enabled: true);
            TableDataDiagnosticLog log = CheckReference(snapshot, "jewel");

            Assert.AreEqual(0, log.ErrorCount, Describe(log));
        }

        [Test]
        public void MonsterCurrencyReference_EmptyId_IsNotChecked()
        {
            TableDataSnapshot snapshot = SnapshotWithCurrency("jewel", enabled: true);
            TableDataDiagnosticLog log = CheckReference(snapshot, string.Empty);

            Assert.AreEqual(0, log.Entries.Count, "재화를 지정하지 않은 몬스터는 참조 검사 대상이 아니다.");
        }

        [Test]
        public void MonsterCurrencyReference_ToAMissingRow_IsAnError()
        {
            TableDataSnapshot snapshot = SnapshotWithCurrency("jewel", enabled: true);
            TableDataDiagnosticLog log = CheckReference(snapshot, "gold");

            Assert.AreEqual(1, CountErrors(log, TableDataColumns.CurrencyId), Describe(log));
        }

        [Test]
        public void MonsterCurrencyReference_ToADisabledRow_IsAnError()
        {
            TableDataSnapshot snapshot = SnapshotWithCurrency("jewel", enabled: false);
            TableDataDiagnosticLog log = CheckReference(snapshot, "jewel");

            Assert.AreEqual(1, CountErrors(log, TableDataColumns.CurrencyId), Describe(log));
        }

        [Test]
        public void MonsterCurrencyReference_IsCaseSensitive()
        {
            TableDataSnapshot snapshot = SnapshotWithCurrency("jewel", enabled: true);
            TableDataDiagnosticLog log = CheckReference(snapshot, "Jewel");

            Assert.AreEqual(1, CountErrors(log, TableDataColumns.CurrencyId),
                "'Jewel'은 'jewel'과 다른 id다 - Ordinal 완전 일치로만 찾는다.");
        }

        // ---- 실제 프로젝트 데이터(읽기 전용) ----

        [Test]
        public void LiveCsv_JewelRow_IsReadExactlyAsAuthored()
        {
            TableDataSnapshot snapshot = Live().Snapshot;
            Assert.IsNotNull(snapshot, "다섯 표가 모두 읽혀야 스냅샷이 만들어진다: " + Live().Summary);

            Assert.IsTrue(snapshot.CurrenciesById.TryGetValue("jewel", out CurrencyRow jewel),
                "실제 Currency.csv의 jewel 행이 스냅샷에 있어야 한다.");
            Assert.AreEqual("jewel", jewel.Id);
            Assert.AreEqual(10, jewel.DisplayOrder);
            Assert.IsTrue(jewel.Enabled);
            Assert.IsTrue(jewel.Name.Resolved, "카테고리 5 / 키 1이 실제 Entry로 해석되어야 한다.");
            Assert.IsNull(jewel.Icon, "icon_key가 비어 있으므로 아이콘은 비어 있어야 한다.");
        }

        [Test]
        public void LiveCsv_HasNoCurrencyErrors_AndOnlyTheBlankIconWarning()
        {
            TableDataValidationResult result = Live();

            var errors = new List<string>();
            int iconWarnings = 0;

            foreach (TableDataDiagnostic diagnostic in result.Diagnostics)
            {
                if (!string.Equals(diagnostic.File, File, StringComparison.Ordinal)) continue;

                if (diagnostic.Severity == TableDataSeverity.Error) errors.Add(diagnostic.ToString());
                else if (string.Equals(diagnostic.Column, TableDataColumns.IconKey, StringComparison.Ordinal)) iconWarnings++;
            }

            Assert.AreEqual(0, errors.Count,
                "Assets/Art/Currency 폴더가 없어도 Currency.csv는 오류 없이 통과해야 한다:\n" + string.Join("\n", errors));
            Assert.AreEqual(1, iconWarnings, "빈 icon_key 경고가 정확히 한 건 있어야 한다.");
        }

        [Test]
        public void LiveCsv_MonsterRowsReferenceAnExistingEnabledCurrency()
        {
            TableDataSnapshot snapshot = Live().Snapshot;
            Assert.IsNotNull(snapshot);

            int referencing = 0;
            foreach (MonsterRow monster in snapshot.Monsters)
            {
                if (string.IsNullOrEmpty(monster.CurrencyId)) continue;

                referencing++;
                Assert.IsTrue(snapshot.CurrenciesById.TryGetValue(monster.CurrencyId, out CurrencyRow currency),
                    $"Monster.csv {monster.Line}행의 '{monster.CurrencyId}'가 Currency.csv에 없다.");
                Assert.IsTrue(currency.Enabled,
                    $"Monster.csv {monster.Line}행이 비활성 재화 '{monster.CurrencyId}'를 가리킨다.");
            }

            Assert.Greater(referencing, 0, "재화를 지정한 몬스터가 하나도 없으면 이 시험은 아무것도 증명하지 못한다.");
        }

        // ---- 도우미 ----

        private static TableDataValidationResult Live()
        {
            return liveResult ?? (liveResult = TableDataValidator.Validate());
        }

        /// <summary>메모리 위의 표로 행 검증만 돌린다. 파일도 에셋도 건드리지 않는다.</summary>
        private static TableDataSnapshot Validate(out TableDataDiagnosticLog log, params string[][] rows)
        {
            return Validate(new TableDataAssetIndex(), out log, rows);
        }

        private static TableDataSnapshot Validate(
            TableDataAssetIndex assets, out TableDataDiagnosticLog log, params string[][] rows)
        {
            var records = new List<CsvRecord>();
            for (int i = 0; i < rows.Length; i++) records.Add(new CsvRecord(i + 2, rows[i]));

            var table = new CsvTable(File, TableDataColumns.Currency, records);
            var snapshot = new TableDataSnapshot();
            log = new TableDataDiagnosticLog();

            ValidateCurrenciesMethod.Invoke(null, new object[] { table, snapshot, assets, log });
            return snapshot;
        }

        /// <summary>
        /// 아이콘 조회 결과를 미리 심어 둔 인덱스. <b>디스크에는 아무것도 만들지 않는다</b> -
        /// Assets/Art/Currency에 시험용 Sprite를 넣으면 그건 프로젝트 자산을 고치는 일이고, 이름이
        /// 겹치는 "여럿" 상황은 그렇게 만들면 되돌리기도 어렵다. 대신 인덱스의 해석 캐시에 값을 넣고
        /// "이미 훑었다"고 표시해, 실제 조회 경로(<see cref="TableDataAssetIndex.FindCurrencyIcon"/>)를
        /// 그대로 지나가게 한다 - 판정(0개/1개/여럿)은 프로덕션 코드가 내린다.
        /// </summary>
        private static TableDataAssetIndex IndexWithCurrencyIcons(string key, params Sprite[] sprites)
        {
            var assets = new TableDataAssetIndex();
            Type type = typeof(TableDataAssetIndex);
            const BindingFlags Instance = BindingFlags.NonPublic | BindingFlags.Instance;

            FieldInfo builtField = type.GetField("currencyIconNamesBuilt", Instance);
            FieldInfo cacheField = type.GetField("resolvedCurrencyIcons", Instance);

            Assert.IsNotNull(builtField, "TableDataAssetIndex.currencyIconNamesBuilt를 찾지 못했습니다.");
            Assert.IsNotNull(cacheField, "TableDataAssetIndex.resolvedCurrencyIcons를 찾지 못했습니다.");

            // 폴더를 훑지 않게 막아 두어야, 시험이 프로젝트 상태에 따라 달라지지 않는다.
            builtField.SetValue(assets, true);
            ((Dictionary<string, List<Sprite>>)cacheField.GetValue(assets))[key] = new List<Sprite>(sprites);

            return assets;
        }

        /// <summary>메모리에만 존재하는 Sprite. 에셋으로 저장하지 않으므로 프로젝트에 남지 않는다.</summary>
        private Sprite NewSprite()
        {
            var texture = new Texture2D(4, 4);
            created.Add(texture);

            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0.5f));
            created.Add(sprite);
            return sprite;
        }

        /// <summary>컬럼 순서는 <see cref="TableDataColumns.Currency"/>와 같다(memo는 늘 비운다).</summary>
        private static string[] Row(
            string id, string category, string key, string iconKey, string displayOrder, string enabled)
        {
            return new[] { id, category, key, iconKey, displayOrder, enabled, string.Empty };
        }

        private static TableDataSnapshot SnapshotWithCurrency(string id, bool enabled)
        {
            var snapshot = new TableDataSnapshot();
            var row = new CurrencyRow { Line = 2, Id = id, Enabled = enabled };
            snapshot.Currencies.Add(row);
            snapshot.CurrenciesById[id] = row;
            return snapshot;
        }

        private static TableDataDiagnosticLog CheckReference(TableDataSnapshot snapshot, string currencyId)
        {
            var log = new TableDataDiagnosticLog();
            var monster = new MonsterRow { Line = 7, Id = "1", CurrencyId = currencyId };

            CheckCurrencyReferenceMethod.Invoke(
                null, new object[] { TableDataPaths.MonsterCsvFileName, snapshot, monster, log });

            return log;
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
