using System;
using System.Collections.Generic;
using System.Globalization;

namespace TableDataEditor
{
    /// <summary>
    /// Monster.csv의 <b>처치 보상 칸</b>(드롭 슬롯 3세트 + 재화 3칸)만 읽는 규칙. 몬스터 행의 나머지
    /// 칸은 <see cref="TableDataValidator"/>가 계속 읽고, 여기서는 보상 규칙 하나만 다룬다 - 확률의
    /// 단위와 "세 칸이 한 덩어리"라는 규칙이 한 파일에 모여 있어야 나중에 규칙을 고칠 때 두 곳을
    /// 뒤지지 않는다.
    ///
    /// <b>칸은 이름으로만 읽는다.</b> CsvTable을 직접 받지 않고 컬럼 이름 → 값 함수를 받으므로,
    /// 표의 칸 순서에도 참조 컬럼($로 시작하는 칸)의 위치에도 영향을 받지 않는다.
    /// </summary>
    public static class MonsterRewardRules
    {
        /// <summary>
        /// 드롭 슬롯 3세트를 읽는다. 슬롯 하나는 <b>세 칸이 한 덩어리</b>라서, 셋 다 비어 있거나 셋 다
        /// 채워져 있어야 한다 - item_id 없이 확률만 적힌 칸은 "적었는데 아무 일도 일어나지 않는" 값이고,
        /// item_id만 있고 확률이 없는 칸은 드롭되지 않는 드롭이라 둘 다 오류로 잡는다. 아이템이 비어
        /// 있어도 <b>형식이 깨진 값은 그대로 오류</b>다 - 읽을 수 없는 값을 "어차피 안 쓰는 칸"이라고
        /// 넘기면 나중에 아이템만 채웠을 때 갑자기 실패한다.
        ///
        /// <b>확률의 단위는 만분율 정수</b>다(10000 = 100%, 1 = 0.01%). 0은 형식 오류가 아니라
        /// "지금은 떨어지지 않는 칸"이며, 경고를 남기고 <b>생성 슬롯을 만들지 않는다</b> - 아이템과 개수를
        /// 적어 둔 채 확률만 0으로 내려 두는 편집을 CSV에서 그대로 표현할 수 있게 하되, 런타임에는
        /// "확률 0인 드롭"이 남지 않게 한다.
        ///
        /// <b>슬롯들은 하나의 누적 구간을 나눠 가진다.</b> 한 몬스터의 처치 보상은 한 번의 판정으로
        /// 정해지므로 아이템은 <b>최대 한 종류</b>만 나오며, 슬롯 확률의 합이 10000을 넘을 수 없는 이유도
        /// 그것이다(구간이 겹치면 어느 아이템이 나올지가 정해지지 않는다). 합이 10000보다 작으면 남은
        /// 구간은 "아무 아이템도 나오지 않음"이다.
        ///
        /// 슬롯이 잘못된 경우 <b>그 슬롯만</b> 버린다. 같은 행의 다른 슬롯은 정상이면 그대로 살아 있고,
        /// 오류가 하나라도 있으면 Rebuild 자체가 멈추므로 반쪽짜리 에셋이 만들어지지는 않는다.
        /// </summary>
        public static void ReadDrops(
            string file, int line, Func<string, string> getCell,
            TableDataSnapshot snapshot, MonsterRow row, TableDataDiagnosticLog log)
        {
            var seenItemIds = new Dictionary<string, int>(StringComparer.Ordinal);

            // 활성 슬롯(아이템이 있고 확률이 0을 넘는 칸)의 확률 합. 슬롯이 다른 이유로 걸렸더라도
            // "사람이 적은 확률"은 그대로 더한다 - 합이 100%를 넘는 것은 그 자체로 표의 오류다.
            int activeChanceTotal = 0;
            int lastActiveSlot = 0;

            for (int slot = 1; slot <= TableDataColumns.MonsterDropSlotCount; slot++)
            {
                string itemColumn = TableDataColumns.DropItemId(slot);
                string chanceColumn = TableDataColumns.DropChance(slot);
                string countColumn = TableDataColumns.DropCount(slot);

                string itemRaw = getCell(itemColumn);
                string chanceRaw = getCell(chanceColumn);
                string countRaw = getCell(countColumn);

                if (string.IsNullOrEmpty(itemRaw))
                {
                    ReadEmptySlotLeftovers(file, line, itemColumn, chanceColumn, chanceRaw, countColumn, countRaw, log);
                    continue;
                }

                bool slotOk = true;

                if (!TableDataFieldRules.IsValidId(itemRaw))
                {
                    log.Error(file, line, itemColumn, itemRaw,
                        $"drop item_id 형식이 맞지 않습니다 - {TableDataFieldRules.IdPatternText} 를 만족해야 합니다.");
                    slotOk = false;
                }
                else if (!snapshot.ItemsById.TryGetValue(itemRaw, out ItemRow item))
                {
                    log.Error(file, line, itemColumn, itemRaw,
                        $"{TableDataPaths.ItemCsvFileName}에 없는 item_id입니다.");
                    slotOk = false;
                }
                else if (!item.Enabled)
                {
                    log.Error(file, line, itemColumn, itemRaw,
                        $"enabled=0인 아이템({TableDataPaths.ItemCsvFileName} {item.Line}행)을 드롭합니다 - " +
                        "드롭 목록에는 활성 아이템만 넣을 수 있습니다.");
                    slotOk = false;
                }

                int chance = 0;
                bool chanceRead = false;
                if (string.IsNullOrEmpty(chanceRaw))
                {
                    log.Error(file, line, chanceColumn, chanceRaw,
                        $"{itemColumn}이 채워진 슬롯에는 확률이 필요합니다 - " +
                        $"0 이상 {TableDataFieldRules.BasisPointsScale} 이하의 만분율 정수를 적으세요" +
                        $"({TableDataFieldRules.BasisPointsScale} = 100%).");
                    slotOk = false;
                }
                else if (!TableDataFieldRules.TryReadBasisPoints(file, line, chanceColumn, chanceRaw, log, out chance))
                {
                    slotOk = false;
                }
                else
                {
                    chanceRead = true;
                }

                if (chanceRead && chance > 0)
                {
                    activeChanceTotal += chance;
                    lastActiveSlot = slot;
                }

                int count = 0;
                if (string.IsNullOrEmpty(countRaw))
                {
                    log.Error(file, line, countColumn, countRaw,
                        $"{itemColumn}이 채워진 슬롯에는 개수가 필요합니다 - 1 이상의 정수를 적으세요.");
                    slotOk = false;
                }
                else if (!TableDataFieldRules.TryReadInt(file, line, countColumn, countRaw, log, out count))
                {
                    slotOk = false;
                }
                else if (count < 1)
                {
                    log.Error(file, line, countColumn, countRaw,
                        "드롭 개수는 1 이상이어야 합니다 - 0개를 주는 슬롯은 세 칸을 모두 비우세요.");
                    slotOk = false;
                }

                // 같은 아이템을 두 슬롯에 나눠 적는 것 자체는 동작한다. 난수는 <b>한 번</b>만 뽑고 그
                // 값이 들어간 누적 구간 <b>하나</b>가 뽑히므로, 같은 아이템이 구간을 두 곳 차지할 뿐 두 번
                // 나오지는 않는다. 다만 지급 개수는 <b>뽑힌 그 슬롯의 개수</b>를 쓰므로, 개수가 서로 다른
                // 중복 슬롯은 확률만이 아니라 <b>몇 개를 받는지의 분포까지</b> 바꾼다(2000bp x1 / 3000bp x2면
                // 20%로 1개, 30%로 2개다). 대부분은 복사한 뒤 고치는 것을 잊은 경우라 경고로 알린다.
                if (seenItemIds.TryGetValue(itemRaw, out int firstSlot))
                {
                    log.Warning(file, line, itemColumn, itemRaw,
                        $"{firstSlot}번 슬롯과 같은 아이템입니다 - 이 아이템이 누적 구간을 두 곳 차지합니다. " +
                        "난수는 한 번만 뽑고 그 값이 들어간 구간 하나만 뽑히므로 한 몬스터가 주는 아이템은 " +
                        "최대 한 종류이고 같은 아이템이 두 번 나오지는 않습니다. 다만 지급 개수는 뽑힌 " +
                        "슬롯의 개수를 그대로 쓰므로, 개수가 다른 중복 슬롯은 이 아이템이 나올 전체 확률과 " +
                        "함께 몇 개를 받게 되는지의 분포도 바꿉니다. 의도한 것이 아니라면 한쪽을 비우거나 " +
                        "확률과 개수를 한 슬롯으로 합치세요.");
                }
                else
                {
                    seenItemIds[itemRaw] = slot;
                }

                if (!slotOk) continue;

                if (chance == 0)
                {
                    log.Warning(file, line, chanceColumn, chanceRaw,
                        $"확률이 0이라 이 슬롯은 드롭되지 않습니다 - 생성 에셋의 드롭 목록에 넣지 않습니다" +
                        $"(아이템 '{itemRaw}'과 개수는 CSV에만 남습니다). 다시 쓰려면 1 이상" +
                        $"({TableDataFieldRules.BasisPointsScale} = 100%)을 적으세요.");
                    continue;
                }

                row.Drops.Add(new MonsterDropRow
                {
                    ItemId = itemRaw,
                    ChanceBasisPoints = chance,
                    Count = count,
                });
            }

            if (activeChanceTotal > TableDataFieldRules.BasisPointsScale)
            {
                log.Error(file, line, TableDataColumns.DropChance(lastActiveSlot),
                    activeChanceTotal.ToString(CultureInfo.InvariantCulture),
                    $"한 몬스터의 드롭 확률 합이 {TableDataFieldRules.BasisPointsScale}(=100%)을 넘습니다" +
                    $"(합 {activeChanceTotal}) - 확률이 0을 넘는 슬롯의 합은 " +
                    $"{TableDataFieldRules.BasisPointsScale} 이하여야 합니다.");
            }
        }

        /// <summary>
        /// 아이템이 비어 있는 슬롯에 남은 확률/개수를 본다. <b>형식부터 본다</b> - 읽을 수 없는 값은
        /// 아이템 유무와 무관하게 오류이고(TryRead 쪽이 보고한다), 읽을 수 있는데 0을 넘는 값은
        /// "적었는데 아무 일도 일어나지 않는" 칸이라 오류다. 0 이하가 남아 있는 것은 지우다 만 흔적이라
        /// 경고만 남긴다.
        /// </summary>
        private static void ReadEmptySlotLeftovers(
            string file, int line, string itemColumn,
            string chanceColumn, string chanceRaw, string countColumn, string countRaw,
            TableDataDiagnosticLog log)
        {
            if (!string.IsNullOrEmpty(chanceRaw)
                && TableDataFieldRules.TryReadBasisPoints(file, line, chanceColumn, chanceRaw, log, out int chance))
            {
                if (chance > 0)
                {
                    log.Error(file, line, chanceColumn, chanceRaw,
                        $"{itemColumn}이 비어 있는데 확률만 적혀 있습니다 - 슬롯을 쓰려면 아이템을 적고, " +
                        "쓰지 않으려면 세 칸을 모두 비우세요.");
                }
                else
                {
                    log.Warning(file, line, chanceColumn, chanceRaw,
                        $"{itemColumn}이 비어 있어 이 확률 값은 쓰이지 않습니다 - 세 칸을 모두 비우세요.");
                }
            }

            if (!string.IsNullOrEmpty(countRaw)
                && TableDataFieldRules.TryReadInt(file, line, countColumn, countRaw, log, out int count))
            {
                if (count > 0)
                {
                    log.Error(file, line, countColumn, countRaw,
                        $"{itemColumn}이 비어 있는데 개수만 적혀 있습니다 - 슬롯을 쓰려면 아이템을 적고, " +
                        "쓰지 않으려면 세 칸을 모두 비우세요.");
                }
                else
                {
                    log.Warning(file, line, countColumn, countRaw,
                        $"{itemColumn}이 비어 있어 이 개수 값은 쓰이지 않습니다 - 세 칸을 모두 비우세요.");
                }
            }
        }

        /// <summary>
        /// 처치 재화 보상 세 칸을 읽는다. <b>id가 기준</b>이다 - id가 비어 있으면 금액 두 칸도 반드시
        /// 비어 있어야 하고(0을 적어 두는 것도 오류다), id가 있으면 금액 두 칸이 모두 있어야 한다.
        /// 한쪽만 적힌 칸을 통과시키면 "얼마를 주는지 표가 말하지 않는" 재화가 생긴다.
        ///
        /// <b>여기서는 재화 id의 실재를 확인하지 않는다</b> - 형식과 세 칸의 짝만 본다. 그 id가
        /// Currency.csv에 실제로 있는 활성 행인지는 표 사이의 참조라서
        /// <see cref="TableDataValidator"/>가 스냅샷의 Currency 표로 확인한다(없거나 enabled=0이면 오류).
        /// 나눠 둔 이유는 이 규칙 시험이 Currency 표 없이도 돌 수 있게 하기 위함이다.
        /// </summary>
        public static void ReadCurrency(
            string file, int line, Func<string, string> getCell, MonsterRow row, TableDataDiagnosticLog log)
        {
            string idRaw = getCell(TableDataColumns.CurrencyId);
            string minRaw = getCell(TableDataColumns.CurrencyAmountMin);
            string maxRaw = getCell(TableDataColumns.CurrencyAmountMax);

            if (string.IsNullOrEmpty(idRaw))
            {
                // 금액만 남은 칸은 지우다 만 흔적이다. 0도 "0을 준다"는 지정이므로 그냥 통과시키지 않는다.
                if (!string.IsNullOrEmpty(minRaw))
                {
                    log.Error(file, line, TableDataColumns.CurrencyAmountMin, minRaw,
                        $"{TableDataColumns.CurrencyId}가 비어 있는데 금액만 적혀 있습니다 - 재화를 주려면 " +
                        "id를 적고, 주지 않으려면 세 칸을 모두 비우세요(0도 지정입니다).");
                }

                if (!string.IsNullOrEmpty(maxRaw))
                {
                    log.Error(file, line, TableDataColumns.CurrencyAmountMax, maxRaw,
                        $"{TableDataColumns.CurrencyId}가 비어 있는데 금액만 적혀 있습니다 - 재화를 주려면 " +
                        "id를 적고, 주지 않으려면 세 칸을 모두 비우세요(0도 지정입니다).");
                }

                return;
            }

            bool ok = true;

            if (!TableDataFieldRules.IsValidId(idRaw))
            {
                log.Error(file, line, TableDataColumns.CurrencyId, idRaw,
                    $"currency_id 형식이 맞지 않습니다 - {TableDataFieldRules.IdPatternText} 를 만족해야 합니다.");
                ok = false;
            }

            bool minOk = ReadCurrencyAmount(
                file, line, TableDataColumns.CurrencyAmountMin, minRaw, log, out int min);
            bool maxOk = ReadCurrencyAmount(
                file, line, TableDataColumns.CurrencyAmountMax, maxRaw, log, out int max);
            ok &= minOk && maxOk;

            if (minOk && maxOk && max < min)
            {
                log.Error(file, line, TableDataColumns.CurrencyAmountMax, maxRaw,
                    $"{TableDataColumns.CurrencyAmountMax}({max})가 " +
                    $"{TableDataColumns.CurrencyAmountMin}({min})보다 작습니다 - 같아도 되지만 작을 수는 없습니다.");
                ok = false;
            }

            if (!ok) return;

            row.CurrencyId = idRaw;
            row.CurrencyAmountMin = min;
            row.CurrencyAmountMax = max;
        }

        /// <summary>재화 금액 한 칸. 비어 있으면 오류(id가 있는 행에서만 부른다), 정수가 아니면 오류,
        /// 음수면 오류다. 0은 유효하다 - "이 재화를 0만큼 준다"는 지정도 표가 말할 수 있어야 한다.</summary>
        private static bool ReadCurrencyAmount(
            string file, int line, string column, string raw, TableDataDiagnosticLog log, out int value)
        {
            value = 0;

            if (string.IsNullOrEmpty(raw))
            {
                log.Error(file, line, column, raw ?? string.Empty,
                    $"{TableDataColumns.CurrencyId}가 채워진 행에는 {column}이 필요합니다 - 0 이상의 정수를 적으세요.");
                return false;
            }

            if (!TableDataFieldRules.TryReadInt(file, line, column, raw, log, out int parsed)) return false;

            if (parsed < 0)
            {
                log.Error(file, line, column, raw,
                    $"{column}은 0 이상이어야 합니다 - 음수 재화 보상은 없습니다.");
                return false;
            }

            value = parsed;
            return true;
        }
    }
}
