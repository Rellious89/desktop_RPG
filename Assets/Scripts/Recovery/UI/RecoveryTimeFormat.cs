using System.Globalization;

namespace Recovery
{
    /// <summary>
    /// 회복 남은 시간을 화면에 쓰는 문자열로 바꾸는 <b>단일 지점</b>. 슬롯 표시와 캐릭터 교체 리스트의
    /// "회복중 {0}" 문구가 같은 서식을 쓰도록 여기 하나만 둔다.
    ///
    /// 숫자 배치(mm:ss)라 번역 대상이 아니며, 자릿수 표기가 사용자의 지역 설정에 따라 달라지지 않도록
    /// InvariantCulture로 만든다.
    /// </summary>
    public static class RecoveryTimeFormat
    {
        /// <summary>초를 mm:ss로 만든다. 1시간이 넘으면 h:mm:ss가 된다(회복이 그렇게 길어질 일은
        /// 없지만 값이 잘려 보이지 않게 한다). 음수는 0으로 취급한다.</summary>
        public static string Format(int totalSeconds)
        {
            if (totalSeconds < 0) totalSeconds = 0;

            int hours = totalSeconds / 3600;
            int minutes = totalSeconds / 60 % 60;
            int seconds = totalSeconds % 60;

            return hours > 0
                ? string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}", hours, minutes, seconds)
                : string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}", minutes, seconds);
        }
    }
}
