namespace Recovery
{
    /// <summary>
    /// 회복 비용을 지불하는 재화 창구. 실제 구현은 InventoryManager 하나뿐이다.
    ///
    /// <b>왜 기존 AddCurrency(-금액)를 쓰지 않는가:</b> 그 경로는 결과를 0으로 자르기 때문에 잔액이
    /// 모자라도 "성공한 것처럼" 0이 되고 만다(부분 지불). 회복 시작은 총액을 한 번에 내거나 아무것도
    /// 내지 않아야 하므로, 부족하면 <b>아무것도 바꾸지 않고</b> false를 돌려주는 전용 경로를 쓴다.
    ///
    /// 저장을 분리해 둔 이유도 같다 - 재화 차감과 회복 슬롯 기록은 한 트랜잭션이라
    /// SaveSystem.Save()가 그 사이에 두 번 일어나면 안 된다.
    /// </summary>
    public interface IRecoveryWallet
    {
        /// <summary>이 지갑이 다루는 재화의 식별자. 밸런스 테이블의 Currency Id와 대조한다.</summary>
        string CurrencyId { get; }

        /// <summary>현재 보유액.</summary>
        int Balance { get; }

        /// <summary>
        /// 잔액이 충분할 때만 <b>메모리에서</b> 차감한다. 저장도 알림도 하지 않는다.
        /// 부족하면 아무것도 바꾸지 않고 false를 돌려준다(0으로 자르지 않는다).
        /// </summary>
        bool TrySpendWithoutSave(int amount);

        /// <summary>차감했던 금액을 되돌린다. 저장에 실패해 트랜잭션 전체를 취소할 때만 쓴다.</summary>
        void RefundWithoutSave(int amount);

        /// <summary>회복소가 저장까지 마친 뒤 재화 표시를 갱신하라고 알린다.</summary>
        void NotifyChangedAfterExternalSave();
    }
}
