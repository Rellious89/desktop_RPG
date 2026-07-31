namespace Common
{
    /// <summary>
    /// 전투 무대 위에서 이 Target이 지금 맡고 있는 역할. "보이는가"와 "때릴 수 있는가"를 분리하기 위한
    /// 단일 기준점이다 - 알파나 위치 같은 시각 표현으로 역할을 대신 표현하지 않는다(알파가 1이어도
    /// Standby면 공격 대상이 아니고, 알파가 0이어도 Current면 공격 대상이다).
    ///
    /// <see cref="Target.HasAttackableTarget"/>/<see cref="Target.TryGetAttackableTarget"/>가 세는
    /// 공격 가능 대상은 오직 <see cref="Current"/> 뿐이다 - Standby/Exiting은 살아 있고 화면에 보여도
    /// 공격 가능 수에 포함되지 않으며 정적 HitPoint도 처리하지 않는다.
    /// </summary>
    public enum TargetEngagementRole
    {
        /// <summary>지금 플레이어가 때리는 대상. 씬 전체에서 동시에 하나만 있어야 한다(관리자가 보장한다).</summary>
        Current = 0,

        /// <summary>다음 차례를 기다리는 대기열 몬스터. 살아 있고 화면에도 보이지만 공격 대상이 아니다.</summary>
        Standby = 1,

        /// <summary>처치되어 무대에서 빠지는 중인 몬스터. 다시 Current가 되는 일은 없고, 관리자가
        /// 명시적으로 prepare할 때까지 공격 대상이 아니다.</summary>
        Exiting = 2
    }
}
