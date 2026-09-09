using System;
using Character;
using Recovery;

namespace Inventory
{
    public interface ICharacterItemUseInventory
    {
        InventoryCostResult TrySpendCostWithoutSave(
            InventoryCostRequest request, out InventoryCostReceipt receipt);
        void RefundCostWithoutSave(InventoryCostReceipt receipt);
        void NotifyChangedAfterExternalSave();
    }

    public enum CharacterItemUseResultCode
    {
        Used = 0,
        InvalidRequest = 1,
        TargetUnavailable = 2,
        StaminaFull = 3,
        ItemUnavailable = 4,
        SaveFailed = 5,
    }

    public readonly struct CharacterItemUseResult
    {
        public CharacterItemUseResult(CharacterItemUseResultCode code, int staminaRecovered = 0)
        {
            Code = code;
            StaminaRecovered = staminaRecovered;
        }

        public CharacterItemUseResultCode Code { get; }
        public int StaminaRecovered { get; }
        public bool Success => Code == CharacterItemUseResultCode.Used;
    }

    /// <summary>
    /// 캐릭터 대상 아이템 1개 소비와 행동력 회복을 저장 한 번으로 확정한다. 저장에 실패하면
    /// 인벤토리와 행동력을 모두 이전 값으로 되돌리고 어떤 변경 이벤트도 보내지 않는다.
    /// </summary>
    public sealed class CharacterItemUseService
    {
        private readonly ICharacterItemUseInventory inventory;
        private readonly IRecoveryRoster roster;
        private readonly Func<bool> saveAction;

        public CharacterItemUseService(
            ICharacterItemUseInventory inventory, IRecoveryRoster roster, Func<bool> saveAction)
        {
            this.inventory = inventory;
            this.roster = roster;
            this.saveAction = saveAction;
        }

        public CharacterItemUseResult TryUse(ItemDefinition item, CharacterDefinition target)
        {
            if (inventory == null || roster == null || saveAction == null || item == null ||
                !item.CanTargetCharacter || item.UseEffectType != ItemUseEffectType.RestoreStamina)
            {
                return new CharacterItemUseResult(CharacterItemUseResultCode.InvalidRequest);
            }

            if (target == null || !roster.Contains(target))
                return new CharacterItemUseResult(CharacterItemUseResultCode.TargetUnavailable);

            int max = roster.GetMaxStamina(target);
            if (max <= 0) return new CharacterItemUseResult(CharacterItemUseResultCode.TargetUnavailable);

            int before = Math.Max(0, Math.Min(max, roster.GetStamina(target)));
            if (before >= max) return new CharacterItemUseResult(CharacterItemUseResultCode.StaminaFull);

            int recovered = Math.Min(max - before, item.UseEffectValue);
            if (recovered <= 0) return new CharacterItemUseResult(CharacterItemUseResultCode.InvalidRequest);

            InventoryCostResult spend = inventory.TrySpendCostWithoutSave(
                InventoryCostRequest.ForItem(item, 1), out InventoryCostReceipt receipt);
            if (spend == null || !spend.Success)
                return new CharacterItemUseResult(CharacterItemUseResultCode.ItemUnavailable);

            if (!roster.ApplyRecoveryStamina(target, before + recovered))
            {
                inventory.RefundCostWithoutSave(receipt);
                return new CharacterItemUseResult(CharacterItemUseResultCode.TargetUnavailable);
            }

            bool saved;
            try
            {
                saved = saveAction();
            }
            catch
            {
                saved = false;
            }

            if (!saved)
            {
                roster.ApplyRecoveryStamina(target, before);
                inventory.RefundCostWithoutSave(receipt);
                return new CharacterItemUseResult(CharacterItemUseResultCode.SaveFailed);
            }

            inventory.NotifyChangedAfterExternalSave();
            roster.RaiseCharacterStateChanged(target);
            return new CharacterItemUseResult(CharacterItemUseResultCode.Used, recovered);
        }
    }
}
