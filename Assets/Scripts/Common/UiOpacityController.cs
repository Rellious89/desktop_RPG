using System.Collections.Generic;
using UnityEngine;

namespace Common
{
    /// <summary>
    /// 기기별 UI 불투명도를 Canvas와 StageVisualRoot의 SpriteRenderer에 함께 적용한다.
    /// Windows 컬러키 배경이나 카메라 출력은 건드리지 않는다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Canvas))]
    [DefaultExecutionOrder(10000)]
    public sealed class UiOpacityController : MonoBehaviour
    {
        private struct SpriteAlphaState
        {
            public float Original;
            public float Applied;
        }

        public static UiOpacityController Instance { get; private set; }

        [Header("Overall UI Opacity")]
        [Tooltip("슬라이더의 최저 불투명도. 화면과 설정 조작이 완전히 보이지 않게 되는 것을 방지한다.")]
        [SerializeField, Range(0.1f, 1f)] private float minimumOpacity = 0.3f;

        [Tooltip("캐릭터, 몬스터, 공격 이펙트가 들어 있는 StageVisualRoot.")]
        [SerializeField] private Transform stageVisualRoot;

        private readonly Dictionary<SpriteRenderer, SpriteAlphaState> spriteStates =
            new Dictionary<SpriteRenderer, SpriteAlphaState>();
        private readonly List<SpriteRenderer> renderers = new List<SpriteRenderer>();
        private readonly List<SpriteRenderer> removedRenderers = new List<SpriteRenderer>();
        private CanvasGroup canvasGroup;
        private float opacity = 1f;

        public float MinimumOpacity => Mathf.Clamp(minimumOpacity, 0.1f, 1f);
        public float Opacity => opacity;

        private void Awake()
        {
            Instance = this;
            canvasGroup = GetComponent<CanvasGroup>();
            if (canvasGroup == null) canvasGroup = gameObject.AddComponent<CanvasGroup>();
            if (stageVisualRoot == null && StageVisualRootController.Instance != null)
                stageVisualRoot = StageVisualRootController.Instance.transform;

            UiSettingsData saved = UiSettingsSaveSystem.Load();
            SetOpacity(saved != null ? saved.uiOpacity : 1f);
        }

        private void LateUpdate()
        {
            if (stageVisualRoot == null) return;

            // 스폰/풀링된 이펙트까지 포함한다. 다른 애니메이션이 alpha를 새로 쓰면
            // 그 값을 원본으로 받아들여 다음 렌더에만 전체 불투명도를 곱한다.
            renderers.Clear();
            // 비활성 프리팹/풀은 아직 Awake에서 자체 원본 알파를 읽지 않았을 수 있다.
            stageVisualRoot.GetComponentsInChildren(false, renderers);
            foreach (SpriteRenderer renderer in renderers)
            {
                if (renderer == null) continue;
                float current = renderer.color.a;
                SpriteAlphaState state;
                if (!spriteStates.TryGetValue(renderer, out state))
                    state.Original = current;
                else if (!Mathf.Approximately(current, state.Applied))
                    state.Original = current;

                state.Applied = state.Original * opacity;
                if (!Mathf.Approximately(current, state.Applied))
                {
                    Color color = renderer.color;
                    color.a = state.Applied;
                    renderer.color = color;
                }
                spriteStates[renderer] = state;
            }

            // 파괴된 임시 이펙트의 참조를 오래 보유하지 않는다.
            if (Time.frameCount % 120 != 0) return;
            removedRenderers.Clear();
            foreach (KeyValuePair<SpriteRenderer, SpriteAlphaState> pair in spriteStates)
                if (pair.Key == null) removedRenderers.Add(pair.Key);
            foreach (SpriteRenderer renderer in removedRenderers) spriteStates.Remove(renderer);
        }

        private void OnEnable()
        {
            if (canvasGroup != null) canvasGroup.alpha = opacity;
        }

        public void SetOpacity(float value)
        {
            opacity = float.IsNaN(value) || float.IsInfinity(value)
                ? 1f : Mathf.Clamp(value, MinimumOpacity, 1f);
            if (canvasGroup != null) canvasGroup.alpha = opacity;
        }

        public void SaveOpacity()
        {
            UiSettingsSaveSystem.SaveUiOpacity(opacity);
        }

        private void OnDisable()
        {
            if (canvasGroup != null) canvasGroup.alpha = 1f;
            foreach (KeyValuePair<SpriteRenderer, SpriteAlphaState> pair in spriteStates)
            {
                if (pair.Key == null) continue;
                Color color = pair.Key.color;
                color.a = pair.Value.Original;
                pair.Key.color = color;
            }
            spriteStates.Clear();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            minimumOpacity = Mathf.Clamp(minimumOpacity, 0.1f, 1f);
        }
#endif
    }
}
