using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace TableDataEditor.Tests
{
    /// <summary>
    /// Monster.csv 보상 칸(드롭 3세트 + 재화 3칸)의 규칙 시험. <b>파일을 읽지도 쓰지도 않는다</b> -
    /// <see cref="MonsterRewardRules"/>가 칸을 이름으로만 읽으므로, 여기서는 칸 값을 직접 넘겨
    /// 규칙 하나하나를 확인한다(프로젝트의 CSV와 생성 에셋은 이 시험으로 바뀌지 않는다).
    /// </summary>
    public sealed class MonsterRewardRulesTests
    {
        private const string File = "Monster.csv";
        private const int Line = 2;

        // ---- 드롭 확률: 만분율 정수만 ----

        [Test]
        public void DropChance_RejectsDecimal()
        {
            var log = Read(Cells(Slot(1, "50000", "0.5", "1")), Items(("50000", true)), out MonsterRow row);

            Assert.AreEqual(1, log.ErrorCount, "0.5는 만분율 정수가 아니므로 오류여야 한다.");
            Assert.AreEqual(0, row.Drops.Count);
        }

        [Test]
        public void DropChance_RejectsExponentAndSign()
        {
            foreach (string raw in new[] { "1e2", "+50", "-1", " 50", "5 0" })
            {
                var log = Read(Cells(Slot(1, "50000", raw, "1")), Items(("50000", true)), out MonsterRow row);

                Assert.AreEqual(1, log.ErrorCount, $"'{raw}'는 만분율 정수가 아니므로 오류여야 한다.");
                Assert.AreEqual(0, row.Drops.Count, $"'{raw}' 슬롯은 만들어지지 않아야 한다.");
            }
        }

        [Test]
        public void DropChance_Zero_WarnsAndOmitsEntry()
        {
            var log = Read(Cells(Slot(1, "50000", "0", "1")), Items(("50000", true)), out MonsterRow row);

            Assert.AreEqual(0, log.ErrorCount, "확률 0은 형식 오류가 아니다.");
            Assert.AreEqual(1, log.WarningCount, "확률 0은 경고로 알려야 한다.");
            Assert.AreEqual(0, row.Drops.Count, "확률 0인 칸은 생성 드롭 목록에 넣지 않는다.");
        }

        [Test]
        public void DropChance_MaxIsAccepted()
        {
            var log = Read(Cells(Slot(1, "50000", "10000", "1")), Items(("50000", true)), out MonsterRow row);

            Assert.AreEqual(0, log.ErrorCount);
            Assert.AreEqual(1, row.Drops.Count);
            Assert.AreEqual(10000, row.Drops[0].ChanceBasisPoints);
            Assert.AreEqual("50000", row.Drops[0].ItemId);
            Assert.AreEqual(1, row.Drops[0].Count);
        }

        [Test]
        public void DropChance_AboveMaxIsError()
        {
            var log = Read(Cells(Slot(1, "50000", "10001", "1")), Items(("50000", true)), out MonsterRow row);

            Assert.AreEqual(1, log.ErrorCount);
            Assert.AreEqual(0, row.Drops.Count);
        }

        // ---- 활성 슬롯 확률 합 ----

        [Test]
        public void ActiveChanceTotal_ExactlyMaxIsValid()
        {
            var cells = Merge(Slot(1, "50000", "6000", "1"), Slot(2, "50001", "4000", "2"));
            var log = Read(Cells(cells), Items(("50000", true), ("50001", true)), out MonsterRow row);

            Assert.AreEqual(0, log.ErrorCount, "합이 정확히 10000이면 유효하다.");
            Assert.AreEqual(2, row.Drops.Count, "슬롯 순서가 그대로 유지되어야 한다.");
            Assert.AreEqual("50000", row.Drops[0].ItemId);
            Assert.AreEqual("50001", row.Drops[1].ItemId);
            Assert.AreEqual(2, row.Drops[1].Count);
        }

        [Test]
        public void ActiveChanceTotal_AboveMaxIsError()
        {
            var cells = Merge(Slot(1, "50000", "6000", "1"), Slot(2, "50001", "4001", "1"));
            var log = Read(Cells(cells), Items(("50000", true), ("50001", true)), out MonsterRow _);

            Assert.AreEqual(1, log.ErrorCount, "합이 10000을 넘으면 오류여야 한다.");
        }

        [Test]
        public void ActiveChanceTotal_IgnoresZeroChanceSlots()
        {
            var cells = Merge(Slot(1, "50000", "10000", "1"), Slot(2, "50001", "0", "1"));
            var log = Read(Cells(cells), Items(("50000", true), ("50001", true)), out MonsterRow row);

            Assert.AreEqual(0, log.ErrorCount, "확률 0은 활성 슬롯이 아니므로 합에 들어가지 않는다.");
            Assert.AreEqual(1, row.Drops.Count);
        }

        // ---- 같은 아이템이 여러 슬롯에 ----

        [Test]
        public void DuplicateItem_WarnsAboutCumulativeIntervals_NotAboutTwoRolls()
        {
            var cells = Merge(Slot(1, "50000", "2000", "1"), Slot(2, "50000", "3000", "2"));
            var log = Read(Cells(cells), Items(("50000", true)), out MonsterRow row);

            Assert.AreEqual(0, log.ErrorCount, "같은 아이템을 나눠 적는 것 자체는 오류가 아니다.");
            Assert.AreEqual(1, log.WarningCount, "뒤에 나온 슬롯 하나만 경고해야 한다.");

            string message = log.Entries[0].Message;
            StringAssert.Contains("누적 구간", message, "누적 구간을 두 곳 차지한다고 설명해야 한다.");
            StringAssert.Contains("최대 한 종류", message, "한 몬스터가 주는 아이템이 최대 한 종류임을 말해야 한다.");
            Assert.IsFalse(message.Contains("두 번 일어납니다"),
                "판정이 두 번 일어난다는 잘못된 설명이 남아 있으면 안 된다.");

            // 두 슬롯의 개수가 1과 2로 다르므로, 경고는 "확률만 달라진다"로 끝나면 안 된다 -
            // 뽑힌 슬롯의 개수를 그대로 쓰기 때문에 받는 개수의 분포까지 달라진다.
            StringAssert.Contains("뽑힌 슬롯의 개수", message, "지급 개수가 뽑힌 슬롯의 것임을 말해야 한다.");
            StringAssert.Contains("분포", message, "받는 개수의 분포가 달라진다는 점을 말해야 한다.");
            Assert.IsFalse(message.Contains("전체 확률뿐"),
                "확률만 달라진다는 설명은 개수가 다른 중복 슬롯에서 사실이 아니다.");
            StringAssert.StartsWith("1번 슬롯과", message, "먼저 나온 슬롯 번호를 가리켜야 한다.");
            Assert.AreEqual("50000", log.Entries[0].Value, "문제가 된 값은 중복된 item_id다.");
            Assert.AreEqual(TableDataColumns.DropItemId(2), log.Entries[0].Column,
                "경고는 뒤에 나온(중복된) 슬롯의 칸을 가리켜야 한다.");

            // 경고는 알림일 뿐 데이터를 바꾸지 않는다 - 두 슬롯 모두 적힌 순서와 값 그대로 남는다.
            Assert.AreEqual(2, row.Drops.Count);
            Assert.AreEqual("50000", row.Drops[0].ItemId);
            Assert.AreEqual(2000, row.Drops[0].ChanceBasisPoints);
            Assert.AreEqual(1, row.Drops[0].Count);
            Assert.AreEqual("50000", row.Drops[1].ItemId);
            Assert.AreEqual(3000, row.Drops[1].ChanceBasisPoints);
            Assert.AreEqual(2, row.Drops[1].Count);
        }

        [Test]
        public void DuplicateItem_StillCountsBothSlotsInActiveTotal()
        {
            var cells = Merge(Slot(1, "50000", "6000", "1"), Slot(2, "50000", "4001", "1"));
            var log = Read(Cells(cells), Items(("50000", true)), out MonsterRow _);

            Assert.AreEqual(1, log.ErrorCount,
                "같은 아이템이어도 누적 구간의 합은 10000을 넘을 수 없다.");
        }

        // ---- 빈 아이템 칸 ----

        [Test]
        public void EmptyItem_WithConfiguredChanceAndCount_IsError()
        {
            var log = Read(Cells(Slot(1, string.Empty, "5000", "1")), Items(), out MonsterRow row);

            Assert.AreEqual(2, log.ErrorCount, "확률과 개수 각각이 오류여야 한다.");
            Assert.AreEqual(0, row.Drops.Count);
        }

        [Test]
        public void EmptyItem_WithMalformedChance_IsStillError()
        {
            var log = Read(Cells(Slot(1, string.Empty, "0.5", string.Empty)), Items(), out MonsterRow _);

            Assert.AreEqual(1, log.ErrorCount, "아이템이 없어도 형식이 깨진 값은 오류다.");
        }

        // ---- 아이템 참조 ----

        [Test]
        public void DropItem_MissingFromItemTable_IsError()
        {
            var log = Read(Cells(Slot(1, "59999", "5000", "1")), Items(("50000", true)), out MonsterRow row);

            Assert.AreEqual(1, log.ErrorCount);
            Assert.AreEqual(0, row.Drops.Count);
        }

        [Test]
        public void DropItem_Disabled_IsError()
        {
            var log = Read(Cells(Slot(1, "50000", "5000", "1")), Items(("50000", false)), out MonsterRow row);

            Assert.AreEqual(1, log.ErrorCount);
            Assert.AreEqual(0, row.Drops.Count);
        }

        [Test]
        public void DropCount_MustBeAtLeastOne()
        {
            var log = Read(Cells(Slot(1, "50000", "5000", "0")), Items(("50000", true)), out MonsterRow row);

            Assert.AreEqual(1, log.ErrorCount);
            Assert.AreEqual(0, row.Drops.Count);
        }

        // ---- 재화 ----

        [Test]
        public void Currency_AllBlank_IsValidAndLeavesRowEmpty()
        {
            var log = Read(Cells(Currency(string.Empty, string.Empty, string.Empty)), Items(), out MonsterRow row);

            Assert.AreEqual(0, log.ErrorCount);
            Assert.AreEqual(string.Empty, row.CurrencyId);
            Assert.AreEqual(0, row.CurrencyAmountMin);
            Assert.AreEqual(0, row.CurrencyAmountMax);
        }

        [Test]
        public void Currency_EmptyIdWithAmounts_IsError()
        {
            var log = Read(Cells(Currency(string.Empty, "0", "0")), Items(), out MonsterRow row);

            Assert.AreEqual(2, log.ErrorCount, "id가 없으면 0을 적어 둔 것도 오류다.");
            Assert.AreEqual(string.Empty, row.CurrencyId);
        }

        [Test]
        public void Currency_IdWithMissingAmount_IsError()
        {
            var log = Read(Cells(Currency("gold", "10", string.Empty)), Items(), out MonsterRow row);

            Assert.AreEqual(1, log.ErrorCount);
            Assert.AreEqual(string.Empty, row.CurrencyId, "실패한 재화 지정은 행에 남지 않는다.");
        }

        [Test]
        public void Currency_NegativeAmount_IsError()
        {
            var log = Read(Cells(Currency("gold", "-1", "10")), Items(), out MonsterRow row);

            Assert.AreEqual(1, log.ErrorCount);
            Assert.AreEqual(string.Empty, row.CurrencyId);
        }

        [Test]
        public void Currency_MaxBelowMin_IsError()
        {
            var log = Read(Cells(Currency("gold", "10", "9")), Items(), out MonsterRow row);

            Assert.AreEqual(1, log.ErrorCount);
            Assert.AreEqual(string.Empty, row.CurrencyId);
        }

        [Test]
        public void Currency_MinEqualsMax_IsValid()
        {
            var log = Read(Cells(Currency("gold", "10", "10")), Items(), out MonsterRow row);

            Assert.AreEqual(0, log.ErrorCount);
            Assert.AreEqual("gold", row.CurrencyId);
            Assert.AreEqual(10, row.CurrencyAmountMin);
            Assert.AreEqual(10, row.CurrencyAmountMax);
        }

        [Test]
        public void Currency_ZeroToZero_IsValid()
        {
            var log = Read(Cells(Currency("gold", "0", "0")), Items(), out MonsterRow row);

            Assert.AreEqual(0, log.ErrorCount);
            Assert.AreEqual("gold", row.CurrencyId);
            Assert.AreEqual(0, row.CurrencyAmountMax);
        }

        [Test]
        public void Currency_IdIsPreservedExactly()
        {
            var log = Read(Cells(Currency("soul_shard", "1", "3")), Items(), out MonsterRow row);

            Assert.AreEqual(0, log.ErrorCount);
            Assert.AreEqual("soul_shard", row.CurrencyId, "id는 다듬거나 바꾸지 않고 그대로 보관한다.");
            Assert.AreEqual(1, row.CurrencyAmountMin);
            Assert.AreEqual(3, row.CurrencyAmountMax);
        }

        [Test]
        public void Currency_MalformedId_IsError()
        {
            var log = Read(Cells(Currency("Gold Coin", "1", "3")), Items(), out MonsterRow row);

            Assert.AreEqual(1, log.ErrorCount);
            Assert.AreEqual(string.Empty, row.CurrencyId);
        }

        // ---- 헤더 ----

        [Test]
        public void MonsterColumns_ContainCurrencyColumnsButNotReferenceColumn()
        {
            var columns = new List<string>(TableDataColumns.Monster);

            Assert.Contains(TableDataColumns.CurrencyId, columns);
            Assert.Contains(TableDataColumns.CurrencyAmountMin, columns);
            Assert.Contains(TableDataColumns.CurrencyAmountMax, columns);

            Assert.IsFalse(columns.Contains("$currency_name"),
                "$currency_name은 작업자용 참조 컬럼이라 임포터가 읽는 컬럼 목록에 있으면 안 된다.");
            Assert.IsTrue(TableDataCsvReader.IsReferenceOnlyColumn("$currency_name"));
        }

        // ---- 도우미 ----

        private static TableDataDiagnosticLog Read(
            Func<string, string> getCell, TableDataSnapshot snapshot, out MonsterRow row)
        {
            var log = new TableDataDiagnosticLog();
            row = new MonsterRow { Line = Line, Id = "1" };

            MonsterRewardRules.ReadDrops(File, Line, getCell, snapshot, row, log);
            MonsterRewardRules.ReadCurrency(File, Line, getCell, row, log);
            return log;
        }

        /// <summary>CsvTable과 같은 규칙 - 적지 않은 칸은 빈 문자열이다.</summary>
        private static Func<string, string> Cells(Dictionary<string, string> cells)
        {
            return column => cells.TryGetValue(column, out string value) ? value : string.Empty;
        }

        private static Dictionary<string, string> Slot(int slot, string item, string chance, string count)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { TableDataColumns.DropItemId(slot), item },
                { TableDataColumns.DropChance(slot), chance },
                { TableDataColumns.DropCount(slot), count },
            };
        }

        private static Dictionary<string, string> Currency(string id, string min, string max)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { TableDataColumns.CurrencyId, id },
                { TableDataColumns.CurrencyAmountMin, min },
                { TableDataColumns.CurrencyAmountMax, max },
            };
        }

        private static Dictionary<string, string> Merge(params Dictionary<string, string>[] parts)
        {
            var merged = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (Dictionary<string, string> part in parts)
            {
                foreach (KeyValuePair<string, string> cell in part) merged[cell.Key] = cell.Value;
            }

            return merged;
        }

        private static TableDataSnapshot Items(params (string Id, bool Enabled)[] items)
        {
            var snapshot = new TableDataSnapshot();
            foreach ((string id, bool enabled) in items)
            {
                var row = new ItemRow { Line = 2, Id = id, Enabled = enabled };
                snapshot.Items.Add(row);
                snapshot.ItemsById[id] = row;
            }

            return snapshot;
        }
    }
}
