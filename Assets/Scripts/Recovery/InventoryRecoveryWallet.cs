using Inventory;

namespace Recovery
{
    /// <summary>
    /// 회복소가 요구하는 <see cref="IRecoveryWallet"/>을 실제 <see cref="InventoryManager"/>에 연결하는
    /// 어댑터. 재화 값의 소유자는 여전히 InventoryManager이며, 여기서는 "부족하면 아무것도 바꾸지 않는"
    /// 차감 경로만 골라 쓴다 - 기존 AddCurrency(음수)는 결과를 0으로 자르기 때문에 회복 비용 지불에
    /// 쓸 수 없다.
    /// </summary>
    public class InventoryRecoveryWallet : IRecoveryWallet
    {
        private readonly InventoryManager inventory;

        public InventoryRecoveryWallet(InventoryManager inventory, string currencyId)
        {
            this.inventory = inventory;
            CurrencyId = currencyId;
        }

        /// <summary>이 지갑이 다루는 재화의 식별자. 지금 게임의 재화는 SaveData.currency 하나뿐이라
        /// 밸런스 테이블이 지정한 id를 그대로 받아 두고, 회복소가 시작 직전에 대조한다 - 나중에 재화가
        /// 여러 종류가 되면 그때 이 어댑터가 종류별로 갈라진다.</summary>
        public string CurrencyId { get; }

        public int Balance => inventory.Currency;

        public bool TrySpendWithoutSave(int amount)
        {
            return inventory.TrySpendCurrencyWithoutSave(amount);
        }

        public void RefundWithoutSave(int amount)
        {
            inventory.RefundCurrencyWithoutSave(amount);
        }

        public void NotifyChangedAfterExternalSave()
        {
            inventory.NotifyChangedAfterExternalSave();
        }
    }
}
