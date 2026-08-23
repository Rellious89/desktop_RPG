namespace Recruitment
{
    /// <summary>
    /// 모집 표들이 쓰는 <b>고정 낱말</b>들. CSV에도 코드에도 같은 문자열이 나오는 값이라 한 곳에만
    /// 적어 둔다 - 두 곳에 적어 두면 한쪽만 고쳐졌을 때 아무도 알 수 없고, 그때 생기는 결과는
    /// "표에는 있는데 런타임이 모르는 값"이라 조용히 후보에서 빠지는 것이다.
    ///
    /// <b>여기 없는 값은 모르는 값이다.</b> 런타임은 모르는 낱말을 추측해서 해석하지 않고
    /// 후보에서 빼며(<see cref="RecruitmentCandidateSelector"/>), 그것이 "표가 앞서가고 코드가
    /// 따라온다"를 안전하게 만드는 유일한 방법이다.
    /// </summary>
    public static class RecruitmentAcquisitionTypes
    {
        /// <summary>모집으로만 얻을 수 있는 캐릭터. 지금 런타임이 아는 <b>유일한</b> 획득 방식이다.</summary>
        public const string RecruitOnly = "RECRUIT_ONLY";
    }

    /// <summary>
    /// 모집 창구가 <b>무엇에 붙어 있는가</b>를 나타내는 낱말. 지금은 건물 하나뿐이지만, 창구를
    /// 건물이 아닌 것(퀘스트/아이템 등)에 붙일 수 있게 처음부터 종류와 id 두 칸으로 나눠 두었다 -
    /// 그래서 <see cref="RecruitmentAccessDefinition"/>은 <c>BuildingDefinition</c>을 참조하지 않는다.
    /// </summary>
    public static class RecruitmentSourceTypes
    {
        /// <summary>마을 건물. <c>source_id</c>는 Building.csv의 <c>building_id</c>다.</summary>
        public const string Building = "BUILDING";
    }
}
