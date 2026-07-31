namespace Common
{
    /// <summary>
    /// 처치 이후의 흐름을 누가 책임지는지 - Target의 생명주기 소유권이다. 역할
    /// (<see cref="TargetEngagementRole"/>)과는 완전히 다른 축이라 따로 둔다: 역할은 "지금 때릴 수 있는
    /// 대상인가"이고, 이 값은 "처치된 뒤 스스로 되살아나는가"이다.
    /// </summary>
    public enum TargetLifecycleMode
    {
        /// <summary>기존 동작(기본값). 처치되면 Target이 스스로 Fade-out -> 대기 -> Fade-in 코루틴을 돌려
        /// 되살아난다. 관리자 없이 씬에 홀로 놓인 몬스터는 전부 이 모드이고, 직렬화된 값이 아니라
        /// 런타임 기본값이라 기존 씬/프리팹은 아무것도 바뀌지 않는다.</summary>
        StandaloneSelfRespawn = 0,

        /// <summary>대기열 관리자가 소유하는 모드. 처치 시 OnDefeated/AnyTargetDefeated는 기존과 똑같이
        /// 정확히 한 번 발생하지만 자체 리스폰 코루틴은 시작하지 않는다 - 이후 재사용 시점과 방법은
        /// 관리자가 <see cref="Target.PrepareForEncounter"/>로 명시적으로 결정한다(그 호출은 어떤
        /// 처치/보상 이벤트도 만들지 않는다).</summary>
        EncounterManaged = 1
    }
}
