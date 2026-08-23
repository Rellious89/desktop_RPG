using Common;
using UnityEngine;
using UnityEngine.UI;

namespace CharacterArchive
{
    /// <summary>명부 드래그 중에만 보이는 비상호작용 시각 복제본. 원본 카드와 저장/UI 로직에는 손대지 않는다.</summary>
    internal static class CharacterArchiveDragPreview
    {
        private static GameObject previewRoot;
        private static Canvas rootCanvas;

        public static bool HasActivePreview => previewRoot != null;

        public static void Begin(GameObject visualSource, Vector2 screenPosition)
        {
            End();
            if (visualSource == null) return;

            Canvas sourceCanvas = visualSource.GetComponentInParent<Canvas>();
            rootCanvas = sourceCanvas != null ? sourceCanvas.rootCanvas : Object.FindFirstObjectByType<Canvas>();
            if (rootCanvas == null) return;

            previewRoot = new GameObject("CharacterArchiveDragPreview", typeof(RectTransform), typeof(CanvasGroup));
            previewRoot.SetActive(false);
            RectTransform rootRect = (RectTransform)previewRoot.transform;
            rootRect.SetParent(rootCanvas.transform, false);
            rootRect.anchorMin = rootRect.anchorMax = new Vector2(0.5f, 0.5f);
            rootRect.pivot = new Vector2(0f, 1f);

            RectTransform sourceRect = visualSource.transform as RectTransform;
            Vector2 sourceSize = sourceRect != null ? sourceRect.rect.size : Vector2.zero;
            if (sourceSize.x <= 0f || sourceSize.y <= 0f)
                sourceSize = sourceRect != null ? sourceRect.sizeDelta : Vector2.zero;
            rootRect.sizeDelta = sourceSize;

            CanvasGroup group = previewRoot.GetComponent<CanvasGroup>();
            group.alpha = 0.6f;
            group.blocksRaycasts = false;
            group.interactable = false;

            // 비활성 부모 아래에서 복제하면 OnEnable/버튼 클릭/현지화 구독이 실행되지 않는다.
            GameObject clone = Object.Instantiate(visualSource, previewRoot.transform, false);
            DisableInteractionComponents(clone);
            RectTransform cloneRect = clone.transform as RectTransform;
            if (cloneRect != null)
            {
                // Stretch anchors on the original card would otherwise collapse its root Graphic
                // into the initially zero-sized preview parent. Keep both card roots top-left aligned.
                cloneRect.anchorMin = cloneRect.anchorMax = new Vector2(0f, 1f);
                cloneRect.pivot = new Vector2(0f, 1f);
                cloneRect.anchoredPosition = Vector2.zero;
                cloneRect.sizeDelta = sourceSize;
            }

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

        public static void End()
        {
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
        }

        private static void DisableInteractionComponents(GameObject clone)
        {
            foreach (Button button in clone.GetComponentsInChildren<Button>(true)) button.enabled = false;
            foreach (CharacterArchiveCardView card in clone.GetComponentsInChildren<CharacterArchiveCardView>(true)) card.enabled = false;
            foreach (PartySlotView slot in clone.GetComponentsInChildren<PartySlotView>(true)) slot.enabled = false;
            foreach (LocalizedTMPText localizer in clone.GetComponentsInChildren<LocalizedTMPText>(true)) localizer.enabled = false;
        }
    }
}
