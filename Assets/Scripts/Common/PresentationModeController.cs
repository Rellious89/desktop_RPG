using System;
using System.Collections.Generic;
using DesktopWindow;
using UnityEngine;

namespace Common
{
    public enum PresentationMode
    {
        Game = 0,
        Companion = 1,
    }

    /// <summary>
    /// 게임 상태는 그대로 둔 채 화면 표현과 기본 UI 입력만 Game/Companion 모드로 전환한다.
    /// 게임 로직을 포함할 수 있는 루트는 비활성화하지 않고 Renderer 또는 CanvasGroup만 숨긴다.
    /// </summary>
    [DefaultExecutionOrder(-80)]
    [DisallowMultipleComponent]
    public sealed class PresentationModeController : MonoBehaviour
    {
        public static PresentationModeController Instance { get; private set; }

        [Header("Initial State")]
        [SerializeField] private PresentationMode initialMode = PresentationMode.Game;

        [Header("Companion Mode - World Visuals")]
        [Tooltip("Companion 모드에서 Renderer만 숨길 환경 루트. 하위 로직 GameObject는 계속 활성 상태를 유지한다.")]
        [SerializeField] private GameObject[] companionHiddenWorldVisualRoots;

        [Header("Companion Mode - UI")]
        [Tooltip("Companion 모드에서 CanvasGroup으로만 숨길 UI 루트. 하위 관리자와 이벤트 구독은 계속 실행된다.")]
        [SerializeField] private GameObject[] companionHiddenUiRoots;

        [Tooltip("Companion 모드에서만 GameObject를 끄는 순수 조작 UI. 로직 루트는 넣지 않는다.")]
        [SerializeField] private GameObject[] gameModeOnlyObjects;

        public PresentationMode CurrentMode { get; private set; } = PresentationMode.Game;
        public bool IsCompanionMode => CurrentMode == PresentationMode.Companion;

        public event Action<PresentationMode> ModeChanged;

        private readonly List<RendererState> rendererStates = new List<RendererState>();
        private readonly List<CanvasGroupState> canvasGroupStates = new List<CanvasGroupState>();
        private readonly List<InputRegionState> inputRegionStates = new List<InputRegionState>();
        private readonly List<ActiveState> activeStates = new List<ActiveState>();
        private bool hasCapturedGamePresentation;

        private void Awake()
        {
            Instance = this;
            CurrentMode = PresentationMode.Game;

            if (initialMode == PresentationMode.Companion)
            {
                SetMode(PresentationMode.Companion);
            }
        }

        private void OnDestroy()
        {
            if (hasCapturedGamePresentation) RestoreGamePresentation();
            if (Instance == this) Instance = null;
        }

        public void ToggleMode()
        {
            SetMode(IsCompanionMode ? PresentationMode.Game : PresentationMode.Companion);
        }

        public void SetMode(PresentationMode mode)
        {
            if (CurrentMode == mode) return;

            if (mode == PresentationMode.Companion)
            {
                CaptureAndHideGamePresentation();
            }
            else
            {
                RestoreGamePresentation();
            }

            CurrentMode = mode;
            ModeChanged?.Invoke(CurrentMode);
        }

        private void CaptureAndHideGamePresentation()
        {
            rendererStates.Clear();
            canvasGroupStates.Clear();
            inputRegionStates.Clear();
            activeStates.Clear();

            var seenRenderers = new HashSet<Renderer>();
            if (companionHiddenWorldVisualRoots != null)
            {
                foreach (GameObject root in companionHiddenWorldVisualRoots)
                {
                    if (root == null) continue;

                    Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
                    foreach (Renderer renderer in renderers)
                    {
                        if (renderer == null || !seenRenderers.Add(renderer)) continue;
                        rendererStates.Add(new RendererState(renderer, renderer.enabled));
                        renderer.enabled = false;
                    }
                }
            }

            var seenRegions = new HashSet<WindowInputRegion>();
            if (companionHiddenUiRoots != null)
            {
                foreach (GameObject root in companionHiddenUiRoots)
                {
                    if (root == null) continue;

                    CanvasGroup group = root.GetComponent<CanvasGroup>();
                    if (group == null) group = root.AddComponent<CanvasGroup>();

                    canvasGroupStates.Add(new CanvasGroupState(
                        group,
                        group.alpha,
                        group.interactable,
                        group.blocksRaycasts,
                        group.ignoreParentGroups));

                    group.alpha = 0f;
                    group.interactable = false;
                    group.blocksRaycasts = false;

                    WindowInputRegion[] regions = root.GetComponentsInChildren<WindowInputRegion>(true);
                    foreach (WindowInputRegion region in regions)
                    {
                        if (region == null || !seenRegions.Add(region)) continue;
                        inputRegionStates.Add(new InputRegionState(region, region.ReceiveMouseInput));
                        region.ReceiveMouseInput = false;
                    }
                }
            }

            if (gameModeOnlyObjects != null)
            {
                foreach (GameObject target in gameModeOnlyObjects)
                {
                    if (target == null) continue;
                    activeStates.Add(new ActiveState(target, target.activeSelf));
                    target.SetActive(false);
                }
            }

            hasCapturedGamePresentation = true;
        }

        private void RestoreGamePresentation()
        {
            if (!hasCapturedGamePresentation) return;

            foreach (RendererState state in rendererStates)
            {
                if (state.Target != null) state.Target.enabled = state.Enabled;
            }

            foreach (CanvasGroupState state in canvasGroupStates)
            {
                if (state.Target == null) continue;
                state.Target.alpha = state.Alpha;
                state.Target.interactable = state.Interactable;
                state.Target.blocksRaycasts = state.BlocksRaycasts;
                state.Target.ignoreParentGroups = state.IgnoreParentGroups;

            }

            foreach (InputRegionState state in inputRegionStates)
            {
                if (state.Target != null) state.Target.ReceiveMouseInput = state.ReceiveMouseInput;
            }

            foreach (ActiveState state in activeStates)
            {
                if (state.Target != null) state.Target.SetActive(state.ActiveSelf);
            }

            rendererStates.Clear();
            canvasGroupStates.Clear();
            inputRegionStates.Clear();
            activeStates.Clear();
            hasCapturedGamePresentation = false;
        }

        private readonly struct RendererState
        {
            public RendererState(Renderer target, bool enabled) { Target = target; Enabled = enabled; }
            public Renderer Target { get; }
            public bool Enabled { get; }
        }

        private readonly struct CanvasGroupState
        {
            public CanvasGroupState(CanvasGroup target, float alpha, bool interactable, bool blocksRaycasts,
                bool ignoreParentGroups)
            {
                Target = target;
                Alpha = alpha;
                Interactable = interactable;
                BlocksRaycasts = blocksRaycasts;
                IgnoreParentGroups = ignoreParentGroups;
            }

            public CanvasGroup Target { get; }
            public float Alpha { get; }
            public bool Interactable { get; }
            public bool BlocksRaycasts { get; }
            public bool IgnoreParentGroups { get; }
        }

        private readonly struct InputRegionState
        {
            public InputRegionState(WindowInputRegion target, bool receiveMouseInput)
            {
                Target = target;
                ReceiveMouseInput = receiveMouseInput;
            }

            public WindowInputRegion Target { get; }
            public bool ReceiveMouseInput { get; }
        }

        private readonly struct ActiveState
        {
            public ActiveState(GameObject target, bool activeSelf) { Target = target; ActiveSelf = activeSelf; }
            public GameObject Target { get; }
            public bool ActiveSelf { get; }
        }
    }
}
