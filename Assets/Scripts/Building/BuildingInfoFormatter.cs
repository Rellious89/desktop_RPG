using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Building
{
    /// <summary>
    /// 건물 정보 팝업이 보여 줄 <b>문자열을 만드는 순수 함수</b>들만 모아 둔 곳. 여기에는 씬도,
    /// 컴포넌트도, 인벤토리도, 저장 데이터도 없다 - 들어온 값으로 문자열을 만들어 돌려주기만 하므로
    /// 같은 입력에 언제나 같은 답이 나오고 EditMode 테스트로 전부 확인할 수 있다.
    ///
    /// <b>여기에 한국어/영어 UI 문구를 적지 않는다.</b> 화면에 보일 낱말(기능 이름, 재화 이름, 문구 틀)은
    /// 전부 호출하는 쪽이 <see cref="Common.LocalizedTextReference"/>에서 받아 넘겨준다 - 이 파일이
    /// 아는 것은 숫자를 어떻게 늘어놓는가(시:분:초, 천 단위 구분)와 조각들을 어떤 순서로 잇는가뿐이다.
    ///
    /// <b>비용은 조각(<see cref="CostComponent"/>)의 목록으로 다룬다.</b> 지금 표에 있는 비용은 재화
    /// 하나뿐이지만, 나중에 아이템 비용이 함께 붙어도 조각을 하나 더 만들어 목록에 넣기만 하면 되도록
    /// 조립 규칙(구분자, 순서)을 이 한 곳에 둔다 - 비용 종류가 늘어날 때 문자열을 잇는 코드가
    /// 화면 컴포넌트마다 복제되지 않게 하기 위함이다.
    /// </summary>
    public static class BuildingInfoFormatter
    {
        /// <summary>건설 시간이 0초일 때의 표시. 시간 표시는 로컬라이징 대상이 아니다(숫자와 콜론뿐).</summary>
        public const string ZeroTime = "00:00:00";

        /// <summary>비용 조각을 잇는 구분자. 재화 하나뿐인 지금은 쓰이지 않지만, 아이템 비용이 붙는
        /// 순간 이 값 하나만 바꾸면 모든 화면이 함께 따라온다.</summary>
        public const string CostSeparator = ", ";

        /// <summary>금액과 이름 사이의 구분자.</summary>
        private const string AmountNameSeparator = " ";

        /// <summary>
        /// 비용 한 조각 - "얼마를 무엇으로 내는가". <b>금액은 이미 만들어진 문자열</b>이고
        /// <b>이름은 이미 번역된 문자열</b>이다. 이 구조체는 그 둘을 어떤 순서로 붙일지만 안다.
        ///
        /// 재화 조각과 아이템 조각이 같은 모양을 쓰는 이유는, 화면 쪽에서 "재화면 이렇게, 아이템이면
        /// 저렇게" 분기하지 않고 <see cref="ComposeCost"/>에 목록으로 넘기게 하기 위함이다.
        /// </summary>
        public readonly struct CostComponent
        {
            /// <summary>표시할 금액/개수 문자열(<see cref="FormatAmount"/>가 만든 값).</summary>
            public readonly string Amount;

            /// <summary>번역된 재화/아이템 이름. 아직 번역이 도착하지 않았으면 null일 수 있다.</summary>
            public readonly string Name;

            public CostComponent(string amount, string name)
            {
                Amount = amount;
                Name = name;
            }

            /// <summary>금액과 이름이 모두 있는 조각인지. 하나라도 비어 있으면 화면에 내보내지 않는다 -
            /// 번역이 아직 도착하지 않은 중간 상태를 보여 주지 않기 위함이다.</summary>
            public bool IsComplete => !string.IsNullOrEmpty(Amount) && !string.IsNullOrEmpty(Name);

            /// <summary>"2,000 주얼"처럼 금액 뒤에 이름을 붙인 한 조각. 이름이 없으면 금액만 돌려준다.</summary>
            public string ToDisplayString()
            {
                if (string.IsNullOrEmpty(Amount)) return Name ?? string.Empty;
                if (string.IsNullOrEmpty(Name)) return Amount;
                return Amount + AmountNameSeparator + Name;
            }
        }

        /// <summary>
        /// 건설 시간을 <c>HH:mm:ss</c>로 만든다. <b>24시간에서 되감기지 않는다</b> - 90000초는
        /// "01:00:00"(하루를 버린 값)이 아니라 "25:00:00"이다. 건설 시간은 하루를 넘길 수 있는 값이고,
        /// 되감긴 표시는 "곧 끝난다"로 잘못 읽히기 때문이다.
        ///
        /// 시간 자리는 두 자리를 <b>최소</b>로 하며 100시간이 넘으면 세 자리로 늘어난다(잘라내지 않는다).
        /// 음수는 들어올 일이 없지만(<see cref="BuildingDefinition.BuildTimeSeconds"/>가 0 이상을
        /// 보장한다) 들어와도 예외 없이 <see cref="ZeroTime"/>이 된다.
        /// </summary>
        public static string FormatBuildTime(long totalSeconds)
        {
            if (totalSeconds <= 0L) return ZeroTime;

            long hours = totalSeconds / 3600L;
            long minutes = totalSeconds % 3600L / 60L;
            long seconds = totalSeconds % 60L;

            return string.Format(
                CultureInfo.InvariantCulture, "{0:00}:{1:00}:{2:00}", hours, minutes, seconds);
        }

        /// <summary>
        /// 남은 시간을 화면에 적을 <b>초 수</b>로 만든다. <b>올림</b>이다 - 0.4초가 남았을 때
        /// "00:00:00"이 보이면 이미 끝난 것으로 읽히지만 실제로는 아직 끝나지 않았기 때문이다
        /// (회복소 타이머가 남은 시간을 올림으로 보여 주는 규칙과 같다).
        ///
        /// 0 이하는 0이다 - 완성 판정은 표시가 아니라 시각 비교가 하므로, 여기서 음수를 그대로
        /// 흘려보내면 "-00:00:01" 같은 표시가 생긴다.
        /// </summary>
        public static long ToDisplaySeconds(TimeSpan remaining)
        {
            double totalSeconds = remaining.TotalSeconds;
            if (totalSeconds <= 0d) return 0L;
            if (totalSeconds >= long.MaxValue) return long.MaxValue;

            return (long)Math.Ceiling(totalSeconds);
        }

        /// <summary>
        /// 남은 시간을 <c>HH:mm:ss</c>로 만든다. 서식 규칙은 <see cref="FormatBuildTime"/>과 <b>같은 것
        /// 하나</b>를 쓴다 - 건설 시간과 남은 시간이 서로 다른 모양으로 보이면 안 되고, 24시간에서
        /// 되감기지 않는 성질도 그대로 따라와야 한다(25시간은 "25:00:00"이다).
        /// </summary>
        public static string FormatRemaining(TimeSpan remaining)
        {
            return FormatBuildTime(ToDisplaySeconds(remaining));
        }

        /// <summary>
        /// 금액/개수를 천 단위 구분과 함께 만든다. "N0" + <see cref="CultureInfo.InvariantCulture"/>라서
        /// 실행 환경의 지역 설정이 점(.)이나 공백을 쓰더라도 언제나 쉼표로 고정된다
        /// (<see cref="Common.InventoryPanel"/>의 재화 표시와 같은 규칙).
        /// </summary>
        public static string FormatAmount(long amount)
        {
            return amount.ToString("N0", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// 비용 조각들을 하나의 문자열로 잇는다. <b>완성되지 않은 조각(<see cref="CostComponent.IsComplete"/>가
        /// false)은 건너뛴다</b> - 번역이 아직 도착하지 않은 조각을 반쪽짜리로 내보내지 않기 위함이다.
        /// 내보낼 조각이 하나도 없으면 빈 문자열을 돌려주며, <b>null을 돌려주지 않는다</b>.
        /// </summary>
        public static string ComposeCost(IReadOnlyList<CostComponent> components)
        {
            if (components == null || components.Count == 0) return string.Empty;

            var builder = new StringBuilder();
            for (int i = 0; i < components.Count; i++)
            {
                CostComponent component = components[i];
                if (!component.IsComplete) continue;

                if (builder.Length > 0) builder.Append(CostSeparator);
                builder.Append(component.ToDisplayString());
            }

            return builder.ToString();
        }

        /// <summary>
        /// 01_UI / 40 문구 틀에 <c>{0}=해금 기능 이름</c>, <c>{1}=건설 시간</c>, <c>{2}=비용</c>을 채운다.
        ///
        /// <b>예외를 밖으로 던지지 않는다.</b> 틀의 자리표시자가 셋과 맞지 않으면(번역이 잘못 저작된 경우)
        /// 틀을 그대로 돌려주고 <paramref name="formatFailed"/>를 true로 알린다 - 화면이 비어 버리는 대신
        /// 저작된 문구가 그대로 보이고, 원인은 호출하는 쪽이 로그로 한 번만 남긴다.
        ///
        /// 틀이 비어 있으면 빈 문자열을 돌려준다 - <b>코드가 한국어/영어 대체 문구를 지어내지 않는다</b>.
        /// </summary>
        public static string ComposeDescription(
            string format, string functionName, string buildTime, string cost, out bool formatFailed)
        {
            formatFailed = false;
            if (string.IsNullOrEmpty(format)) return string.Empty;

            try
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    format,
                    functionName ?? string.Empty,
                    buildTime ?? string.Empty,
                    cost ?? string.Empty);
            }
            catch (FormatException)
            {
                formatFailed = true;
                return format;
            }
        }
    }
}
