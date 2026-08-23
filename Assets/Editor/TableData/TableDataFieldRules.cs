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
        /// 소문자 키 형식. ID 형식에서 <b>숫자만으로 이루어진 형태를 뺀</b> 것으로, 소문자로 시작하고
        /// 소문자/숫자와 밑줄 구분만 쓴다. 분류 키(<c>skill_type</c>)나 동작 키(<c>behavior_key</c>)처럼
        /// "사람이 읽는 낱말"이어야 하는 칸에 쓴다 - 이런 칸에 <c>1</c> 같은 값이 들어가면 나중에
        /// 그 키를 무엇으로 읽어야 할지 알 수 없다.
        /// </summary>
        public const string LowercaseKeyPatternText = "^[a-z][a-z0-9]*(?:_[a-z0-9]+)*$";

        private static readonly Regex LowercaseKeyPattern =
            new Regex(LowercaseKeyPatternText, RegexOptions.CultureInvariant);

        /// <summary>
        /// <b>Character.csv 전용</b>으로 예외를 인정하는 기존 캐릭터 id 여섯 개. 이 id들은 저장
        /// 데이터와 씬의 로스터가 이미 쓰고 있는 값이라 <b>한 글자도 바꿀 수 없다</b> - snake_case로
        /// 고치는 순간 기존 저장 항목과의 연결이 끊긴다.
        ///
        /// <b>여기 적힌 여섯 개만 예외다.</b> 다른 PascalCase는 전부 형식 오류이며(테스트용
        /// <c>IceMage</c> / <c>Leopard</c>도 포함된다), 이 목록이 늘어나는 일도 없어야 한다 - 새로
        /// 만드는 캐릭터는 표준 ID 형식을 쓴다. 예외를 표준 규칙 쪽에 넣지 않고 이 표에만 두는 이유는,
        /// <see cref="IdPatternText"/>를 조금이라도 느슨하게 만들면 다섯 개 기존 표의 id 검사가 함께
        /// 헐거워지기 때문이다.
        /// </summary>
        public static readonly string[] LegacyCharacterIds =
        {
            "Barbarian", "CatKnight", "CatMage", "ElfArcher", "ElfGuardian", "RabbitHealer",
        };

        private static readonly HashSet<string> LegacyCharacterIdSet =
            new HashSet<string>(LegacyCharacterIds, StringComparer.Ordinal);

        /// <summary>
        /// <b>대문자 낱말</b> 형식. 소문자 키를 그대로 대문자로 뒤집은 것으로, 대문자로 시작하고
        /// 대문자/숫자와 밑줄 구분만 쓴다(<c>RECRUIT_ONLY</c>, <c>BUILDING</c>). 값이 <b>낱말이지
        /// 식별자가 아닌</b> 칸에 쓴다 - 획득 방식이나 대상 종류처럼, 표가 적는 것은 "이름"이고 그
        /// 이름을 무엇으로 읽을지는 런타임이 정하는 칸들이다.
        ///
        /// 형식만 본다. <b>런타임이 아는 낱말인지는 여기서 보지 않는다</b> - 표가 코드보다 먼저
        /// 앞서가는 것을 오류로 막으면, 아직 지원하지 않는 방식을 미리 적어 둘 수조차 없다.
        /// 모르는 낱말을 어떻게 다룰지는 그 값을 읽는 런타임의 몫이다
        /// (<see cref="Recruitment.RecruitmentCandidateSelector"/>는 후보에서 뺀다).
        /// </summary>
        public const string UppercaseKeyPatternText = "^[A-Z][A-Z0-9]*(?:_[A-Z0-9]+)*$";

        private static readonly Regex UppercaseKeyPattern =
            new Regex(UppercaseKeyPatternText, RegexOptions.CultureInvariant);

        /// <summary>
        /// <b>모집 표 전용</b> ID 형식. 표준 ID(<see cref="IdPatternText"/>)에서 <b>대문자를 허용</b>한
        /// 것으로, 영문자로 시작하고 영문자/숫자와 밑줄 구분만 쓴다(<c>Inn_Normal</c>,
        /// <c>Inn_Normal_Access</c>).
        ///
        /// 표준 형식을 <b>느슨하게 만들지 않고</b> 따로 둔 이유는
        /// <see cref="LegacyCharacterIds"/>와 같다 - <see cref="IdPatternText"/>를 조금이라도 넓히면
        /// 기존 아홉 표의 id 검사가 함께 헐거워진다. 모집 id에 대소문자를 허용하는 것은 이 id들이
        /// 사람이 읽는 표기(<c>Inn_Normal</c>)로 이미 저작되어 있고, 그것을 소문자로 고치면 표와
        /// 코드와 문서에 흩어진 값을 한꺼번에 바꿔야 하기 때문이다.
        ///
        /// <b>숫자만으로 이루어질 수 없다.</b> 첫 글자가 영문자여야 하므로 <c>1</c>은 모집 id가 될
        /// 수 없다 - 모집 id와 건물 id를 눈으로 구분할 수 있게 남겨 둔 성질이다.
        /// </summary>
        public const string RecruitmentIdPatternText = "^[A-Za-z][A-Za-z0-9]*(?:_[A-Za-z0-9]+)*$";

        private static readonly Regex RecruitmentIdPattern =
            new Regex(RecruitmentIdPatternText, RegexOptions.CultureInvariant);

        /// <summary>대문자 낱말 형식을 만족하는지. 빈 값은 false다.</summary>
        public static bool IsValidUppercaseKey(string value)
        {
            return !string.IsNullOrEmpty(value) && UppercaseKeyPattern.IsMatch(value);
        }

        /// <summary>모집 표의 id 형식을 만족하는지. 빈 값은 false다.</summary>
        public static bool IsValidRecruitmentId(string value)
        {
            return !string.IsNullOrEmpty(value) && RecruitmentIdPattern.IsMatch(value);
        }

        /// <summary>
        /// 모집 표의 필수 id 칸을 읽는다. 규칙은 <see cref="TryReadRequiredId"/>와 같고 허용 집합만
        /// <see cref="IsValidRecruitmentId"/>로 넓다 - <b>값은 어떤 경우에도 고치지 않는다</b>.
        /// </summary>
        public static bool TryReadRequiredRecruitmentId(
            string file, int line, string column, string raw, TableDataDiagnosticLog log, out string id)
        {
            id = raw ?? string.Empty;

            if (id.Length == 0)
            {
                log.Error(file, line, column, id, "필수 ID가 비어 있습니다.");
                return false;
            }

            if (!RecruitmentIdPattern.IsMatch(id))
            {
                log.Error(file, line, column, id,
                    $"{column} 형식이 맞지 않습니다 - {RecruitmentIdPatternText} 를 정확히 만족해야 합니다" +
                    "(영문자로 시작하고 영문자/숫자와 밑줄 구분만 쓰며, 숫자로 시작할 수 없고 " +
                    "앞뒤 공백도 쓸 수 없습니다). 값은 자동으로 고치지 않습니다.");
                return false;
            }

            return true;
        }

        /// <summary>
        /// 대문자 낱말 칸을 읽는다. <b>필수</b>이며 비어 있으면 오류다 - 이 칸이 비면 그 행이 무엇을
        /// 뜻하는지 알 수 없다. 형식만 확인하고 <b>값은 고치지 않는다</b>(소문자를 대문자로 올려
        /// 통과시키지 않는다).
        /// </summary>
        public static bool TryReadRequiredUppercaseKey(
            string file, int line, string column, string raw, TableDataDiagnosticLog log, out string key)
        {
            key = raw ?? string.Empty;

            if (key.Length == 0)
            {
                log.Error(file, line, column, key, $"{column}는 비워 둘 수 없습니다.");
                return false;
            }

            if (IsValidUppercaseKey(key)) return true;

            log.Error(file, line, column, key,
                $"낱말 형식이 맞지 않습니다 - {UppercaseKeyPatternText} 를 정확히 만족해야 합니다" +
                "(대문자로 시작하고 대문자/숫자와 밑줄 구분만 쓰며 앞뒤 공백은 쓸 수 없습니다). " +
                "값은 자동으로 고치지 않습니다.");
            key = string.Empty;
            return false;
        }

        /// <summary>
        /// 비어 있어도 되는 ID 칸. 비어 있으면 빈 문자열을 돌려주고 아무것도 알리지 않는다 -
        /// "아직 조건이 없다"는 정상적인 상태다. 값이 있으면 <see cref="IdPatternText"/>를 정확히
        /// 만족해야 하며 <b>값을 다듬지 않는다</b>.
        /// </summary>
        public static bool TryReadOptionalId(
            string file, int line, string column, string raw, TableDataDiagnosticLog log, out string id)
        {
            id = raw ?? string.Empty;
            if (id.Length == 0) return true;

            if (IdPattern.IsMatch(id)) return true;

            log.Error(file, line, column, id,
                $"ID 형식이 맞지 않습니다 - {IdPatternText} 를 정확히 만족해야 합니다. " +
                "값은 자동으로 고치지 않습니다.");
            id = string.Empty;
            return false;
        }

        /// <summary>소문자 키 형식을 만족하는지. 빈 값은 false다(비어도 되는지는 호출하는 쪽이 정한다).</summary>
        public static bool IsValidLowercaseKey(string value)
        {
            return !string.IsNullOrEmpty(value) && LowercaseKeyPattern.IsMatch(value);
        }

        /// <summary>
        /// Character.csv의 id로 쓸 수 있는 값인지. <b>표준 ID이거나, 예외로 인정한 기존 여섯 개
        /// 중 하나</b>여야 한다. 비교는 <see cref="StringComparer.Ordinal"/> 완전 일치이며
        /// <b>값을 다듬거나 대소문자를 맞추지 않는다</b> - 'catknight'는 'CatKnight'가 아니다.
        /// </summary>
        public static bool IsValidCharacterId(string value)
        {
            return IsValidId(value) || (!string.IsNullOrEmpty(value) && LegacyCharacterIdSet.Contains(value));
        }

        /// <summary>
        /// Character.csv의 필수 id 칸을 읽는다. 규칙은 <see cref="TryReadRequiredId"/>와 같고
        /// 허용 집합만 <see cref="IsValidCharacterId"/>로 넓다 - <b>값은 어떤 경우에도 고치지 않는다</b>.
        /// </summary>
        public static bool TryReadRequiredCharacterId(
            string file, int line, string column, string raw, TableDataDiagnosticLog log, out string id)
        {
            id = raw ?? string.Empty;

            if (id.Length == 0)
            {
                log.Error(file, line, column, id, "필수 ID가 비어 있습니다.");
                return false;
            }

            if (!IsValidCharacterId(id))
            {
                log.Error(file, line, column, id,
                    $"character_id 형식이 맞지 않습니다 - {IdPatternText} 를 만족하거나, " +
                    "기존 캐릭터 id(" + string.Join(", ", LegacyCharacterIds) + ") 중 하나와 " +
                    "정확히 같아야 합니다(대소문자를 구분하며 앞뒤 공백은 쓸 수 없습니다). " +
                    "값은 자동으로 고치지 않습니다.");
                return false;
            }

            return true;
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
            return TryReadFlag(file, line, column, raw, log, out enabled);
        }

        /// <summary>
        /// 참/거짓 칸을 읽는 <b>유일한 규칙</b>. <c>enabled</c>와 <c>initially_owned</c>처럼 "둘 중
        /// 하나"인 칸은 전부 이 함수를 지난다 - 표마다 다른 표기를 받아 주기 시작하면 어느 칸이 무엇을
        /// 허용하는지 아무도 기억하지 못한다.
        ///
        /// <b>정확히 "1" 또는 "0"</b>만 허용한다. 빈 칸, <c>true</c>/<c>TRUE</c>/<c>false</c>,
        /// 앞뒤 공백이 붙은 값, 그 밖의 정수는 모두 오류이며 <b>값을 고쳐서 통과시키지 않는다</b>.
        /// </summary>
        public static bool TryReadFlag(
            string file, int line, string column, string raw, TableDataDiagnosticLog log, out bool value)
        {
            value = false;

            if (string.Equals(raw, "1", StringComparison.Ordinal))
            {
                value = true;
                return true;
            }

            if (string.Equals(raw, "0", StringComparison.Ordinal)) return true;

            log.Error(file, line, column, raw ?? string.Empty,
                $"{column}는 정확히 1 또는 0이어야 합니다 - 빈 칸이나 true/false 같은 다른 표기는 쓸 수 없습니다.");
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

        /// <summary>
        /// 하한이 있는 정수 칸. 정수로 읽히지 않거나 하한보다 작으면 <b>둘 다 오류</b>이며, 값을
        /// 하한으로 끌어올려 통과시키지 않는다 - 보정해 넘기면 CSV에 적힌 값과 에셋의 값이 달라진다
        /// (Monster.csv의 max_durability가 경고 후 런타임 보정에 맡기는 것과 <b>다른</b> 판정이다.
        /// 그쪽은 이미 그 규칙으로 쓰이고 있어 바꾸지 않았고, 새로 만드는 칸은 처음부터 오류로 막는다).
        /// </summary>
        public static bool TryReadIntAtLeast(
            string file, int line, string column, string raw, int minimum,
            TableDataDiagnosticLog log, out int value)
        {
            if (!TryReadInt(file, line, column, raw, log, out value)) return false;

            if (value >= minimum) return true;

            log.Error(file, line, column, raw ?? string.Empty,
                $"{minimum} 이상이어야 합니다 - 값을 자동으로 올려 통과시키지 않습니다.");
            value = 0;
            return false;
        }

        /// <summary>
        /// 비어 있어도 되는 정수 칸. <b>빈 칸과 값이 있는 칸은 서로 다른 상태</b>라, 빈 칸을 0으로
        /// 바꿔 읽지 않고 <paramref name="hasValue"/>로 구분해 돌려준다 - "아직 정하지 않았다"가
        /// 데이터에서 사라지지 않게 하기 위함이다. 값이 있으면 하한 검사까지 한다.
        /// </summary>
        public static bool TryReadOptionalIntAtLeast(
            string file, int line, string column, string raw, int minimum,
            TableDataDiagnosticLog log, out bool hasValue, out int value)
        {
            hasValue = false;
            value = 0;

            if (string.IsNullOrEmpty(raw)) return true;

            if (!TryReadIntAtLeast(file, line, column, raw, minimum, log, out value)) return false;

            hasValue = true;
            return true;
        }

        /// <summary>
        /// 비어 있어도 되는 소문자 키 칸. 비어 있으면 빈 문자열을 돌려주고 아무것도 알리지 않는다 -
        /// "아직 분류를 정하지 않았다"는 정상적인 상태다. 값이 있으면
        /// <see cref="LowercaseKeyPatternText"/>를 정확히 만족해야 하며, <b>대소문자를 맞추거나 공백을
        /// 떼어 통과시키지 않는다</b>.
        /// </summary>
        public static bool TryReadOptionalLowercaseKey(
            string file, int line, string column, string raw, TableDataDiagnosticLog log, out string key)
        {
            key = raw ?? string.Empty;
            if (key.Length == 0) return true;

            if (IsValidLowercaseKey(key)) return true;

            log.Error(file, line, column, key,
                $"키 형식이 맞지 않습니다 - {LowercaseKeyPatternText} 를 정확히 만족해야 합니다" +
                "(소문자로 시작하고 소문자/숫자와 밑줄 구분만 쓰며, 숫자만으로 이루어질 수 없고 " +
                "앞뒤 공백도 쓸 수 없습니다). 값은 자동으로 고치지 않습니다.");
            key = string.Empty;
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
