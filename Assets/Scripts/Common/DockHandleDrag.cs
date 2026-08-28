using UnityEngine;
using UnityEngine.EventSystems;

namespace Common
{
    /// <summary>두 공용 패널의 접합부를 함께 옮기는 작은 입력 핸들.</summary>
    [RequireComponent(typeof(RectTransform))]
    [DisallowMultipleComponent]
    public sealed class DockHandleDrag : MonoBehaviour, IPointerDownHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private PanelDockManager manager;
        private RectTransform parentRect;
        private Vector2 dragStartLocalPoint;
        private bool dragging;

        public void Configure(PanelDockManager owner)
        {
            manager = owner;
            parentRect = transform.parent as RectTransform;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            manager?.FocusDockHandle(this);
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            dragging = manager != null && parentRect != null &&
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    parentRect, eventData.position, eventData.pressEventCamera, out dragStartLocalPoint);

            if (dragging) manager.BeginDockHandleDrag(this);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!dragging || manager == null) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    parentRect, eventData.position, eventData.pressEventCamera, out Vector2 localPoint)) return;

            manager.MoveDockHandleDrag(this, localPoint - dragStartLocalPoint);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (dragging) manager?.EndDockHandleDrag(this);
            dragging = false;
        }

        private void OnDisable()
        {
            dragging = false;
        }
    }
}
