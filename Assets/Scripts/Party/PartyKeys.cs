namespace Party
{
    /// <summary>
    /// 파티 표가 쓰는 <b>고정 낱말</b>들. CSV에도 코드에도 같은 문자열이 나오는 값이라 한 곳에만
    /// 적어 둔다 - 두 곳에 적어 두면 한쪽만 고쳐졌을 때 아무도 알 수 없고, 그때 생기는 결과는
    /// "표에는 있는데 코드가 못 찾는 설정"이라 조용히 정원을 못 읽는 것이다.
    ///
    /// 비교는 언제나 <see cref="System.StringComparison.Ordinal"/>이므로 <b>대소문자가 다르면 다른
    /// 키다</b> - 그래서 여기 적힌 철자가 CSV의 철자와 글자 하나까지 같아야 한다.
    /// </summary>
    public static class PartyConfigIds
    {
        /// <summary>기본 파티 설정. 지금 표에 있는 <b>유일한</b> 설정이며, 코드가 정원을 물어볼 때
        /// 쓰는 키다.</summary>
        public const string Default = "default";
    }

    /// <summary>파티 표의 값이 지켜야 하는 <b>수의 규칙</b>. 표 검증과 런타임 판정이 같은 상수를 보게
    /// 해서, 한쪽만 느슨해지는 일이 없게 한다.</summary>
    public static class PartyConfigRules
    {
        /// <summary>기본 정원의 하한. 0명짜리 파티는 설정으로서 뜻이 없으므로 <b>표에서 오류로</b>
        /// 막고, 런타임도 같은 기준으로 그 정의를 목록에서 뺀다.</summary>
        public const int MinimumBaseCapacity = 1;
    }
}
