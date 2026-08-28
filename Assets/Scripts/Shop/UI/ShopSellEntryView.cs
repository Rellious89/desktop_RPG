using System;
using Common;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Shop.UI
{
    /// <summary>판매 세션의 런타임 행 입력만 담당한다. 표시 프리팹의 크기나 레이아웃은 바꾸지 않는다.</summary>
    [DisallowMultipleComponent]
    public sealed class ShopSellEntryView : MonoBehaviour, IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private string itemId;
        private RectTransform listRoot;
        private Action<string> remove;

        public void Bind(string value, RectTransform targetList, Action<string> removeAction)
        {
            itemId = value ?? string.Empty;
            listRoot = targetList;
            remove = removeAction;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData != null && eventData.button == PointerEventData.InputButton.Right) Remove();
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (eventData != null) InventorySellDragPreview.Begin(this, eventData.position);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (eventData != null) InventorySellDragPreview.UpdatePosition(eventData.position);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            try
            {
                if (eventData == null || listRoot == null) return;
                if (!RectTransformUtility.RectangleContainsScreenPoint(listRoot, eventData.position, eventData.pressEventCamera))
                    Remove();
            }
            finally
            {
                InventorySellDragPreview.End(this);
            }
        }

        private void OnDisable()
        {
            InventorySellDragPreview.End(this);
        }

        private void OnDestroy()
        {
            InventorySellDragPreview.End(this);
        }

        private void Remove()
        {
            if (!string.IsNullOrEmpty(itemId)) remove?.Invoke(itemId);
        }
    }
}
