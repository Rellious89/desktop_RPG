namespace Character
{
    /// <summary>
    /// 던전 접근 판정이 캐릭터 레벨을 물어볼 때 쓰는 <b>읽기 전용 이음매</b>. Dungeon 네임스페이스가
    /// SaveData나 CharacterRoster의 구체 타입을 직접 참조하지 않도록 하기 위한 최소 계약이다.
    /// </summary>
    public interface IPartyCharacterLevelSource
    {
        /// <summary>
        /// <b>재생 가능한 출전 파티원</b> 중 가장 높은 레벨. 모션 프로필 검증을 통과하지 못한 정의는
        /// 제외하고, 저장 레벨이 1 미만이면 런타임 1로 읽되 저장 값은 바꾸지 않는다. 쓸 수 있는
        /// 출전 파티원이 하나도 없으면 0이다.
        /// </summary>
        int HighestPartyCharacterLevel { get; }
    }
}
