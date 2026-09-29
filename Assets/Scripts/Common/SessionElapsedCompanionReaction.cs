using Character;
using TMPro;
using UnityEngine;

namespace Common
{
    /// <summary>One session-time situation and one generic, non-interactive reaction near the current actor.</summary>
    [DisallowMultipleComponent]
    public sealed class SessionElapsedCompanionReaction : MonoBehaviour
    {
        [Header("Scene References")]
        [SerializeField] private CharacterRuntimeActor actor;
        [SerializeField] private Camera stageCamera;
        [SerializeField] private RectTransform uiParent;
        [SerializeField] private RectTransform speechBubblePrefab;

        [Header("Reaction Tuning")]
        [Tooltip("Seconds since this component starts. Set to a few seconds for a quick Play Mode test.")]
        [Min(0f)] [SerializeField] private float reactionDelaySeconds = 300f;
        [Tooltip("One common line, regardless of which character is active.")]
        [TextArea] [SerializeField] private string reactionText = "잠깐 쉬어 가도 좋아요.";
        [Min(0.1f)] [SerializeField] private float visibleSeconds = 4f;
        [SerializeField] private Vector2 uiOffset = new Vector2(0f, 12f);

        private RectTransform bubble;
        private Canvas parentCanvas;
        private float elapsedSeconds;
        private float shownAt;
        private bool shown;

        public static bool ShouldShow(float elapsed, float delay, bool alreadyShown, bool actorReady)
        {
            return !alreadyShown && actorReady && elapsed >= Mathf.Max(0f, delay);
        }

        private void Awake()
        {
            if (actor == null || stageCamera == null || uiParent == null || speechBubblePrefab == null)
            {
                Debug.LogWarning("[SessionElapsedCompanionReaction] Assign Actor, Stage Camera, UI Parent, " +
                                 "and Speech Bubble Prefab in the Inspector before testing.", this);
                return;
            }

            parentCanvas = uiParent.GetComponentInParent<Canvas>();
            if (parentCanvas == null)
            {
                Debug.LogWarning("[SessionElapsedCompanionReaction] UI Parent needs a parent Canvas.", this);
                return;
            }
            bubble = Instantiate(speechBubblePrefab, uiParent, false);
            bubble.name = "SessionCompanionReaction";

            // The shared visual was authored as a clickable, localized rest bubble. This copy is neither.
            UnityEngine.UI.Button button = bubble.GetComponent<UnityEngine.UI.Button>();
            if (button != null) button.enabled = false;
            foreach (UnityEngine.UI.Graphic graphic in bubble.GetComponentsInChildren<UnityEngine.UI.Graphic>(true))
                graphic.raycastTarget = false;
            foreach (LocalizedTMPText localizer in bubble.GetComponentsInChildren<LocalizedTMPText>(true))
                localizer.enabled = false;

            TextMeshProUGUI label = bubble.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null) label.text = reactionText;
            bubble.gameObject.SetActive(false);
        }

        private void Update()
        {
            elapsedSeconds += Time.unscaledDeltaTime;
            bool actorReady = actor != null && actor.gameObject.activeInHierarchy &&
                              actor.CurrentDefinition != null;

            if (ShouldShow(elapsedSeconds, reactionDelaySeconds, shown, actorReady) &&
                bubble != null && stageCamera != null && parentCanvas != null && UpdatePosition())
            {
                shown = true;
                shownAt = elapsedSeconds;
                bubble.gameObject.SetActive(true);
            }

            if (bubble == null || !bubble.gameObject.activeSelf) return;
            if (!actorReady || elapsedSeconds - shownAt >= visibleSeconds || !UpdatePosition())
                bubble.gameObject.SetActive(false);
        }

        private bool UpdatePosition()
        {
            SpriteRenderer renderer = actor.GetComponent<SpriteRenderer>();
            if (renderer == null || !renderer.enabled) return false;

            Bounds bounds = renderer.bounds;
            Vector3 worldPoint = new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);
            Vector3 screenPoint = stageCamera.WorldToScreenPoint(worldPoint);
            if (screenPoint.z <= 0f ||
                !stageCamera.pixelRect.Contains(new Vector2(screenPoint.x, screenPoint.y))) return false;

            Camera uiCamera = parentCanvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null : parentCanvas.worldCamera;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    uiParent, screenPoint, uiCamera, out Vector2 localPoint)) return false;

            bubble.anchoredPosition = localPoint + uiOffset;
            return true;
        }

        private void OnDestroy()
        {
            if (bubble != null) Destroy(bubble.gameObject);
        }

        private void OnDisable()
        {
            if (bubble != null) bubble.gameObject.SetActive(false);
        }

        private void OnValidate()
        {
            reactionDelaySeconds = Mathf.Max(0f, reactionDelaySeconds);
            visibleSeconds = Mathf.Max(0.1f, visibleSeconds);
        }
    }
}
