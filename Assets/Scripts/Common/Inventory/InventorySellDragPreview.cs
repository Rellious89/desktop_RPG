using UnityEngine;
using UnityEngine.UI;

namespace Common
{
    /// <summary>판매 등록 드래그에서만 보이는 list_item의 비상호작용 복제본이다.
    /// 원본 슬롯은 레이아웃과 데이터를 그대로 유지하고, 미리보기만 최상위 Canvas에 올린다.</summary>
    public static class InventorySellDragPreview
    {
        private static GameObject previewRoot;
        private static Canvas rootCanvas;
        private static InventorySlotView owner;

        public static bool HasActivePreview => previewRoot != null;

        public static void Begin(InventorySlotView source, Vector2 screenPosition)
        {
            End();
            if (source == null) return;

            Canvas sourceCanvas = source.GetComponentInParent<Canvas>();
            rootCanvas = sourceCanvas != null ? sourceCanvas.rootCanvas : Object.FindFirstObjectByType<Canvas>();
            if (rootCanvas == null) return;

            previewRoot = new GameObject("InventorySellDragPreview", typeof(RectTransform), typeof(CanvasGroup));
            previewRoot.SetActive(false);
            previewRoot.layer = rootCanvas.gameObject.layer;
            RectTransform previewRect = (RectTransform)previewRoot.transform;
            previewRect.SetParent(rootCanvas.transform, false);
            previewRect.SetAsLastSibling();
            previewRect.anchorMin = previewRect.anchorMax = new Vector2(0.5f, 0.5f);
            previewRect.pivot = new Vector2(0f, 1f);

            RectTransform sourceRect = source.transform as RectTransform;
            Vector2 size = sourceRect != null ? sourceRect.rect.size : Vector2.zero;
            if (size.x <= 0f || size.y <= 0f) size = sourceRect != null ? sourceRect.sizeDelta : Vector2.zero;
            previewRect.sizeDelta = size;

            CanvasGroup group = previewRoot.GetComponent<CanvasGroup>();
            group.alpha = 0.6f;
            group.blocksRaycasts = false;
            group.interactable = false;

            // list_item 전체를 복제하므로 기존 배경·아이콘·수량 표시가 함께 보인다.
            GameObject clone = Object.Instantiate(source.gameObject, previewRoot.transform, false);
            DisableInteraction(clone);
            RectTransform cloneRect = clone.transform as RectTransform;
            if (cloneRect != null)
            {
                cloneRect.anchorMin = cloneRect.anchorMax = new Vector2(0f, 1f);
                cloneRect.pivot = new Vector2(0f, 1f);
                cloneRect.anchoredPosition = Vector2.zero;
                cloneRect.sizeDelta = size;
            }

            owner = source;
            previewRoot.SetActive(true);
            UpdatePosition(screenPosition);
        }

        public static void UpdatePosition(Vector2 screenPosition)
        {
            if (previewRoot == null || rootCanvas == null) return;
            RectTransform canvasRect = rootCanvas.transform as RectTransform;
            Camera camera = rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : rootCanvas.worldCamera;
            if (canvasRect != null && RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPosition, camera, out Vector2 local))
                ((RectTransform)previewRoot.transform).anchoredPosition = local;
        }

        public static void End(InventorySlotView expectedOwner = null)
        {
            if (expectedOwner != null && !ReferenceEquals(owner, expectedOwner)) return;
            if (previewRoot != null)
            {
                previewRoot.SetActive(false);
#if UNITY_EDITOR
                if (!Application.isPlaying) Object.DestroyImmediate(previewRoot);
                else Object.Destroy(previewRoot);
#else
                Object.Destroy(previewRoot);
#endif
            }
            previewRoot = null;
            rootCanvas = null;
            owner = null;
        }

        private static void DisableInteraction(GameObject clone)
        {
            foreach (Selectable selectable in clone.GetComponentsInChildren<Selectable>(true)) selectable.enabled = false;
            foreach (InventorySlotView slot in clone.GetComponentsInChildren<InventorySlotView>(true)) slot.enabled = false;
            foreach (Graphic graphic in clone.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
        }
    }
}
