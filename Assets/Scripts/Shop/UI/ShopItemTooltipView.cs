using Common;
using Inventory;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Shop.UI
{
    /// <summary>상점의 런타임 상품 행이 씬의 공용 아이템 툴팁을 사용하도록 연결한다.</summary>
    [DisallowMultipleComponent]
    public sealed class ShopItemTooltipView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private ItemDefinition boundItem;
        private long boundCount;
        private ItemTooltipController tooltipController;
        private bool tooltipControllerResolved;

        public void Bind(ItemDefinition item, long count)
        {
            if (boundItem != item || boundCount != count) CancelTooltip();
            boundItem = item;
            boundCount = count;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (boundItem == null) return;
            ResolveTooltipController()?.RequestShow(this, boundItem, boundCount, transform as RectTransform);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            CancelTooltip();
        }

        private void OnDisable()
        {
            CancelTooltip();
        }

        private void OnDestroy()
        {
            CancelTooltip();
        }

        private void CancelTooltip()
        {
            if (tooltipControllerResolved) tooltipController?.CancelShow(this);
        }

        private ItemTooltipController ResolveTooltipController()
        {
            if (tooltipControllerResolved) return tooltipController;
            tooltipControllerResolved = true;
            tooltipController = ItemTooltipController.FindSharedController(this);
            return tooltipController;
        }
    }
}
