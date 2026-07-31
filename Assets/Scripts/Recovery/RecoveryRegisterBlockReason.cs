namespace Recovery
{
    /// <summary>
    /// 회복 등록(드래그해서 슬롯에 올리기)이 막히는 이유. CharacterRoster.SwapBlockReason과 같은
    /// 목적이다 - "선택했는데 아무 반응 없이 실패"하는 경로를 만들지 않기 위해 항상 이유를 돌려준다.
    ///
    /// <b>교체 가능 판정과는 별개의 API다.</b> 교체는 CharacterRoster.GetSwapBlockReason,
    /// 회복 등록은 RecoveryStation.GetRegisterBlockReason이 소유한다 - 두 판정 규칙이 다르기 때문에
    /// (예: 행동력 0은 교체 불가지만 회복 등록은 가능) 하나로 합치지 않는다.
    /// </summary>
    public enum RecoveryRegisterBlockReason
    {
        /// <summary>지금 등록할 수 있다.</summary>
        None,

        /// <summary>로스터에 없는 캐릭터이거나 null.</summary>
        NotInRoster,

        /// <summary>밸런스 테이블 값이 잘못돼 회복소 전체가 멈춰 있다.</summary>
        InvalidBalance,

        /// <summary>지금 전투에 나가 있는 캐릭터다. 먼저 다른 캐릭터로 교체해야 한다.</summary>
        Active,

        /// <summary>이미 슬롯에 올려둔(PendingRecovery) 캐릭터다.</summary>
        AlreadyPending,

        /// <summary>이미 회복 중이거나 회복이 끝나 합류를 기다리는 캐릭터다.</summary>
        AlreadyInRecovery,

        /// <summary>행동력이 이미 최대치라 회복할 것이 없다.</summary>
        StaminaFull,

        /// <summary>빈 슬롯이 없다(캐릭터 자체는 등록 가능하다).</summary>
        NoFreeSlot,

        /// <summary>지정한 슬롯 번호가 범위를 벗어났거나 이미 차 있다.</summary>
        SlotUnavailable,
    }
}
