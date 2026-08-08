using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using CommonEditor;
using Dungeon;

namespace TableDataEditor
{
    /// <summary>
    /// 칸 하나를 읽는 규칙들. <b>어떤 규칙도 값을 고쳐서 통과시키지 않는다</b> - 소문자로 바꾸거나
    /// 공백을 떼어 내고 넘어가면, CSV에 적힌 것과 에셋에 들어간 것이 달라져 나중에 원인을 찾을 수 없다.
    /// 유일한 예외는 <c>|</c> 목록의 토큰이며, 그때도 <b>원본 값을 담은 경고를 남기고</b> 다듬은 값을
    /// 참조에 쓴다(규칙에 명시된 동작).
    /// </summary>
    public static class TableDataFieldRules
    {
        /// <summary>
        /// ID 형식. 두 가지 중 하나여야 한다 - (1) 앞자리 0이 없는 양의 정수 문자열, 또는
        /// (2) 소문자로 시작하고 소문자/숫자와 밑줄 구분만 쓰는 snake_case.
        /// 런타임 ID 타입은 계속 string이며, 숫자 ID도 문자열 그대로 다룬다.
        /// </summary>
        public const string IdPatternText = "^(?:[1-9][0-9]*|[a-z][a-z0-9]*(?:_[a-z0-9]+)*)$";

        private static readonly Regex IdPattern =
            new Regex(IdPatternText, RegexOptions.CultureInvariant);

        public static bool IsValidId(string value)
        {
            return !string.IsNullOrEmpty(value) && IdPattern.IsMatch(value);
        }

        /// <summary>
        /// 필수 ID 칸을 읽는다. 비어 있으면 오류, 형식이 다르면 오류이며 <b>값을 다듬지 않는다</b> -
        /// 앞뒤 공백이 있는 ID는 형식 오류로 걸린다(공백을 떼고 통과시키면 CSV와 에셋의 ID가 달라진다).
        /// </summary>
        public static bool TryReadRequiredId(
            string file, int line, string column, string raw, TableDataDiagnosticLog log, out string id)
        {
            id = raw ?? string.Empty;

            if (id.Length == 0)
            {
                log.Error(file, line, column, id, "필수 ID가 비어 있습니다.");
                return false;
            }

            if (!IdPattern.IsMatch(id))
            {
                log.Error(file, line, column, id,
                    $"ID 형식이 맞지 않습니다 - {IdPatternText} 를 정확히 만족해야 합니다" +
                    "(앞자리 0 없는 양의 정수, 또는 소문자/숫자와 밑줄만 쓰는 snake_case. 앞뒤 공백 불가). " +
                    "값은 자동으로 고치지 않습니다.");
                return false;
            }

            return true;
        }

        /// <summary>enabled 칸. <b>정확히 "1" 또는 "0"</b>만 허용한다 - true/TRUE/공백 섞임을 받아 주면
        /// 어느 행이 실제로 카탈로그에 들어가는지가 흐려진다.</summary>
        public static bool TryReadEnabled(
            string file, int line, string column, string raw, TableDataDiagnosticLog log, out bool enabled)
        {
            enabled = false;

            if (string.Equals(raw, "1", StringComparison.Ordinal))
            {
                enabled = true;
                return true;
            }

            if (string.Equals(raw, "0", StringComparison.Ordinal)) return true;

            log.Error(file, line, column, raw ?? string.Empty,
                "enabled는 정확히 1 또는 0이어야 합니다.");
            return false;
        }

        /// <summary>정수 칸. InvariantCulture로만 읽고 자릿수 구분 기호나 앞뒤 공백을 허용하지 않는다.</summary>
        public static bool TryReadInt(
            string file, int line, string column, string raw, TableDataDiagnosticLog log, out int value)
        {
            if (int.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value)) return true;

            log.Error(file, line, column, raw ?? string.Empty,
                "정수가 아닙니다 - 부호 없는/있는 10진 정수만 허용하며 공백이나 천 단위 구분 기호는 쓸 수 없습니다.");
            value = 0;
            return false;
        }

        /// <summary>확률의 단위. <b>10000이 100%</b>이고 1은 0.01%다(만분율, basis points).
        /// 런타임 클래스의 상수를 그대로 쓴다 - CSV의 단위와 에셋의 단위는 같은 하나여야 한다.</summary>
        public const int BasisPointsScale = MonsterDefinition.DropEntry.ChanceBasisPointsScale;

        /// <summary>
        /// 확률 칸. <b>만분율 10진 정수</b>만 받는다 - 0 이상 <see cref="BasisPointsScale"/> 이하이며,
        /// 10000이 100%, 1이 0.01%다.
        ///
        /// <b>소수도 지수도 부호도 공백도 받지 않는다.</b> 0.5 같은 값을 반올림해 통과시키면 CSV에 적힌
        /// 값과 에셋의 확률이 달라지고, 지역 설정에 따라 소수점이 <c>,</c>가 되는 파일을 조용히 읽으면
        /// 값이 통째로 달라진다 - 표기가 하나뿐이면 그런 경로 자체가 없다. NaN/Infinity 같은 낱말도
        /// 정수 파서가 애초에 받지 않는다.
        ///
        /// <b>0은 형식 오류가 아니다.</b> "지금은 떨어지지 않는 칸"이라는 뜻이며, 그 판단(경고와 제외)은
        /// 슬롯의 의미를 아는 호출하는 쪽이 한다.
        /// </summary>
        public static bool TryReadBasisPoints(
            string file, int line, string column, string raw, TableDataDiagnosticLog log, out int value)
        {
            value = 0;
            string text = raw ?? string.Empty;

            if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed))
            {
                // 음수는 "정수가 아니다"보다 "범위를 벗어났다"가 원인에 가까우므로 따로 안내한다.
                bool negative = text.Length > 1 && text[0] == '-'
                    && int.TryParse(text.Substring(1), NumberStyles.None, CultureInfo.InvariantCulture, out _);

                log.Error(file, line, column, text, negative
                    ? $"확률은 0 이상 {BasisPointsScale} 이하여야 합니다 - 음수는 쓸 수 없습니다."
                    : $"확률은 만분율 정수여야 합니다 - 0 이상 {BasisPointsScale} 이하의 10진 정수만 " +
                      "허용하며(10000 = 100%, 1 = 0.01%) 소수점/지수 표기/부호/공백은 쓸 수 없습니다.");
                return false;
            }

            if (parsed > BasisPointsScale)
            {
                log.Error(file, line, column, text,
                    $"확률이 {BasisPointsScale}(=100%)을 넘습니다 - 만분율이므로 100%보다 큰 값은 있을 수 없습니다.");
                return false;
            }

            value = parsed;
            return true;
        }

        /// <summary>
        /// <c>|</c>로 구분한 ID 목록을 읽는다. 빈 값이면 아무것도 담지 않고 true를 돌려주므로,
        /// "목록이 비었다"는 경고는 목록의 의미를 아는 호출하는 쪽이 낸다.
        ///
        /// 토큰 주변 공백은 <b>경고 후 다듬어서</b> 쓴다(원본 값을 경고에 넣는다). 같은 목록 안의
        /// 중복은 오류이며 그 토큰은 버린다 - 같은 몬스터/보상이 두 칸을 차지하는 것은 표시 버그다.
        /// </summary>
        public static void ReadIdList(
            string file, int line, string column, string raw, TableDataDiagnosticLog log, List<string> output)
        {
            output.Clear();
            if (string.IsNullOrEmpty(raw)) return;

            string[] tokens = raw.Split('|');
            var seen = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < tokens.Length; i++)
            {
                string token = tokens[i] ?? string.Empty;
                string trimmed = token.Trim();

                if (trimmed.Length == 0)
                {
                    log.Error(file, line, column, token,
                        $"{i + 1}번째 항목이 비어 있습니다 - 구분 기호(|)가 연달아 있거나 끝에 남아 있습니다.");
                    continue;
                }

                if (!string.Equals(token, trimmed, StringComparison.Ordinal))
                {
                    log.Warning(file, line, column, token,
                        $"{i + 1}번째 항목의 앞뒤에 공백이 있어 '{trimmed}'로 다듬어 참조합니다 - CSV에서도 공백을 지우세요.");
                }

                if (!IdPattern.IsMatch(trimmed))
                {
                    log.Error(file, line, column, token,
                        $"{i + 1}번째 항목의 ID 형식이 맞지 않습니다 - {IdPatternText} 를 만족해야 합니다.");
                    continue;
                }

                if (!seen.Add(trimmed))
                {
                    log.Error(file, line, column, token,
                        $"{i + 1}번째 항목 '{trimmed}'이 같은 행에서 중복됩니다 - 한 번만 적으세요.");
                    continue;
                }

                output.Add(trimmed);
            }
        }

        /// <summary>
        /// 같은 파일 안에서 display_order가 겹치는지 본다. 오름차순 정렬의 동률은 ID로 갈리므로
        /// 데이터가 깨지지는 않지만, 표시 순서를 사람이 의도한 대로 읽을 수 없게 되므로 경고한다.
        /// </summary>
        public static void CheckDuplicateDisplayOrder(
            string file, int line, int displayOrder, Dictionary<int, int> firstLineByOrder, TableDataDiagnosticLog log)
        {
            if (firstLineByOrder.TryGetValue(displayOrder, out int firstLine))
            {
                log.Warning(file, line, TableDataColumns.DisplayOrder,
                    displayOrder.ToString(CultureInfo.InvariantCulture),
                    $"display_order가 {firstLine}행과 같습니다 - 동률은 ID 오름차순으로 갈리므로 의도한 순서가 아닐 수 있습니다.");
                return;
            }

            firstLineByOrder[displayOrder] = line;
        }

        /// <summary>
        /// 카테고리 번호 + 숫자 키를 실제 Localization Entry로 해석한다. 프로젝트에 그 카테고리나
        /// 키가 <b>실제로 있는지</b>까지 확인하며, 없으면 오류다 - 빈 문구가 화면에 나가는 것보다
        /// 임포트 단계에서 걸리는 편이 낫다.
        ///
        /// 두 칸이 모두 비어 있는 경우는 여기서 다루지 않는다. "필수인지 선택인지"는 표마다 다르므로
        /// 호출하는 쪽이 판단한다.
        /// </summary>
        public static bool TryResolveLocalizedEntry(
            string file,
            int line,
            string categoryColumn,
            string categoryRaw,
            string keyColumn,
            string keyRaw,
            TableDataDiagnosticLog log,
            out LocalizedEntryRef entryRef)
        {
            entryRef = LocalizedEntryRef.None;

            if (!int.TryParse(categoryRaw, NumberStyles.None, CultureInfo.InvariantCulture, out int categoryCode)
                || categoryCode <= 0)
            {
                log.Error(file, line, categoryColumn, categoryRaw ?? string.Empty,
                    "카테고리 번호는 1 이상의 정수여야 합니다(Localization Table 이름의 숫자 접두사).");
                return false;
            }

            if (!int.TryParse(keyRaw, NumberStyles.None, CultureInfo.InvariantCulture, out int keyNumber)
                || keyNumber <= 0)
            {
                log.Error(file, line, keyColumn, keyRaw ?? string.Empty,
                    "숫자 키는 1 이상의 정수여야 합니다.");
                return false;
            }

            var category = LocalizationCategoryCatalog.FindCategoryByCode(categoryCode);
            if (category == null)
            {
                log.Error(file, line, categoryColumn, categoryRaw ?? string.Empty,
                    $"카테고리 {categoryCode}에 해당하는 String Table Collection이 프로젝트에 없습니다.");
                return false;
            }

            if (category.TableCollectionNameGuid == Guid.Empty)
            {
                log.Error(file, line, categoryColumn, categoryRaw ?? string.Empty,
                    $"카테고리 {categoryCode}('{category.CollectionName}')의 Table GUID를 읽을 수 없습니다 - " +
                    "Localization Table 에셋을 확인하세요.");
                return false;
            }

            if (!category.EntriesByNumber.TryGetValue(keyNumber, out var entry) || entry == null)
            {
                log.Error(file, line, keyColumn, keyRaw ?? string.Empty,
                    $"카테고리 {categoryCode}('{category.CollectionName}')에 숫자 키 {keyNumber} Entry가 없습니다.");
                return false;
            }

            entryRef = new LocalizedEntryRef
            {
                Resolved = true,
                TableGuid = category.TableCollectionNameGuid,
                KeyId = entry.KeyId,
            };
            return true;
        }
    }
}
