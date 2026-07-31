namespace Recovery
{
    /// <summary>
    /// 회복소가 보는 캐릭터 한 명의 상태. <b>이 값은 어디에도 저장하지 않는다</b> - 매번 아래 원천에서
    /// 파생시킨다. 상태 문자열을 따로 저장하면 실제 데이터와 어긋난 상태가 남기 때문이다.
    ///
    /// 원천(소유자)은 상태마다 정확히 하나다.
    ///   - <see cref="Recovering"/>/<see cref="RecoveryComplete"/> : SaveData.recoverySlots (회복소 소유)
    ///   - <see cref="Active"/>                                     : CharacterRoster.Current (로스터 소유)
    ///   - <see cref="PendingRecovery"/>                            : RecoveryStation의 런타임 목록 (저장 안 함)
    ///   - <see cref="Exhausted"/>/<see cref="Available"/>          : SaveData.characters[].currentStamina (로스터 소유)
    ///
    /// 판정 우선순위도 위 순서 그대로다. 특히 <b>전투 중 캐릭터의 행동력이 0이면 Active</b>이며
    /// Exhausted가 아니다 - 전투에 나가 있는 캐릭터를 회복소에 넣지 않는다는 규칙이 우선한다.
    /// </summary>
    public enum RecoveryCharacterState
    {
        /// <summary>대기 중이고 행동력이 남아 있다. 최대치 미만이면 회복 등록이 가능하고, 교체도 가능하다.</summary>
        Available,

        /// <summary>지금 전투에 나가 있는 캐릭터. 회복 등록은 불가하고, 교체 대상으로 고를 수도 없다
        /// (이미 그 캐릭터다).</summary>
        Active,

        /// <summary>행동력이 0인 대기 캐릭터. 교체 불가, 회복 등록 가능.</summary>
        Exhausted,

        /// <summary>회복 슬롯에서 시간이 흐르는 중. 교체 불가.</summary>
        Recovering,

        /// <summary>회복이 끝났지만 아직 슬롯에 남아 있다. 자동으로 합류하지 않으며, 사용자가
        /// 합류를 눌러야 <see cref="Available"/>이 된다. 교체 불가.</summary>
        RecoveryComplete,

        /// <summary><b>런타임/UI 전용 임시 상태.</b> 회복소 패널에서 슬롯에 올려두기만 하고 아직
        /// 시작 버튼을 누르지 않은 상태다. 저장하지 않고, 재화를 차감하지 않으며, 실제
        /// <see cref="Recovering"/>으로 취급하지 않는다 - 패널을 닫거나 앱을 껐다 켜면 사라진다.</summary>
        PendingRecovery,
    }
}
