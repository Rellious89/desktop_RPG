using Inventory;
using UnityEngine;

namespace Common
{
    /// <summary>
    /// 인벤토리 슬롯이 특정 화면의 임시 목록에 아이템을 넘길 때 쓰는 작은 입력 문맥이다.
    /// 인벤토리 자체는 상점이나 다른 소비 화면을 알지 않고, 열린 화면만 이 문맥에 등록한다.
    /// </summary>
    public interface IInventoryItemRegistrationTarget
    {
        bool CanRegisterInventoryItem(ItemDefinition item);
        bool IsInventoryItemRegistrationDrop(Vector2 screenPosition, Camera eventCamera);
        void RegisterInventoryItem(ItemDefinition item);
    }

    public static class InventoryItemRegistrationContext
    {
        private static IInventoryItemRegistrationTarget activeTarget;

        public static IInventoryItemRegistrationTarget ActiveTarget => activeTarget;

        public static void SetActiveTarget(IInventoryItemRegistrationTarget target)
        {
            activeTarget = target;
        }

        public static void ClearActiveTarget(IInventoryItemRegistrationTarget target)
        {
            if (ReferenceEquals(activeTarget, target)) activeTarget = null;
        }

        public static void TryRegister(ItemDefinition item)
        {
            IInventoryItemRegistrationTarget target = activeTarget;
            if (target != null && target.CanRegisterInventoryItem(item)) target.RegisterInventoryItem(item);
        }

        public static void TryRegisterAt(ItemDefinition item, Vector2 screenPosition, Camera eventCamera)
        {
            IInventoryItemRegistrationTarget target = activeTarget;
            if (target == null || !target.IsInventoryItemRegistrationDrop(screenPosition, eventCamera)) return;
            if (target.CanRegisterInventoryItem(item)) target.RegisterInventoryItem(item);
        }
    }
}
