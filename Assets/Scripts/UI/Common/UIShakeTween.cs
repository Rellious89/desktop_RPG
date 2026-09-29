using DG.Tweening;
using UnityEngine;

namespace Common
{
    /// <summary>
    /// A short, pixel-snapped uGUI shake played explicitly by its owner. Assign a
    /// visual child as the target so an interactive control's own rectangle stays put.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    [DisallowMultipleComponent]
    public sealed class UIShakeTween : MonoBehaviour
    {
        [Tooltip("Visual RectTransform to shake. For a Slider, assign a child rather than its interactive root.")]
        [SerializeField] private RectTransform visualTarget;

        [Header("Shake")]
        [Tooltip("Maximum displacement in UI pixels when Shake is 1.")]
        [Min(0f)] [SerializeField] private float radius = 4f;

        [Tooltip("Total shake time in seconds. Uses unscaled time.")]
        [Min(0f)] [SerializeField] private float duration = 0.24f;

        [Tooltip("Shake intensity: 0 disables movement, 1 uses the full Radius.")]
        [Range(0f, 1f)] [SerializeField] private float shake = 1f;

        private Vector2 basePosition;
        private Tween activeTween;

        // Slider input can use a descendant of the visual target as its drag
        // coordinate frame. Expose the rounded peak so callers can distinguish
        // actual user movement from value jitter caused by this visual motion.
        public float MaximumHorizontalDisplacement =>
            Mathf.Ceil(Mathf.Max(0f, radius) * Mathf.Clamp01(shake));

        public void PlayShake()
        {
            if (!isActiveAndEnabled) return;
            if (visualTarget == null) return;

            StopShake();
            basePosition = visualTarget.anchoredPosition;

            float amplitude = Mathf.Max(0f, radius) * Mathf.Clamp01(shake);
            float seconds = Mathf.Max(0f, duration);
            if (amplitude < 0.5f || seconds <= 0f) return;

            // DOTween drives progress, while the two differing frequencies produce
            // movement on both axes. The envelope settles exactly at the origin.
            activeTween = DOTween.To(() => 0f, ApplyProgress, 1f, seconds)
                .SetEase(Ease.Linear)
                .SetUpdate(true)
                .OnComplete(() =>
                {
                    activeTween = null;
                    if (visualTarget != null) visualTarget.anchoredPosition = basePosition;
                });
        }

        public void StopShake()
        {
            if (activeTween == null) return;
            Tween tween = activeTween;
            activeTween = null;
            if (tween.IsActive()) tween.Kill(false);
            if (visualTarget != null) visualTarget.anchoredPosition = basePosition;
        }

        private void ApplyProgress(float progress)
        {
            if (visualTarget == null) return;
            float amplitude = Mathf.Max(0f, radius) * Mathf.Clamp01(shake) * (1f - progress);
            float x = Mathf.Sin(progress * Mathf.PI * 16f) * amplitude;
            float y = Mathf.Sin(progress * Mathf.PI * 21f) * amplitude;
            visualTarget.anchoredPosition = basePosition + new Vector2(Mathf.Round(x), Mathf.Round(y));
        }

        private void OnDisable()
        {
            StopShake();
        }

        private void OnDestroy()
        {
            StopShake();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            radius = Mathf.Max(0f, radius);
            duration = Mathf.Max(0f, duration);
            shake = Mathf.Clamp01(shake);
        }
#endif
    }
}
