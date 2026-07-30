using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.Events;

namespace Common
{
    /// <summary>
    /// 무료 DOTween 코어만으로 동작하는 범용 uGUI 등장/종료 Tween 컴포넌트. DOTween Pro의
    /// DOTweenAnimation처럼 Inspector에서 방향/거리/시간/알파/크기/Ease를 설정해 쓰되, Pro 기능에는
    /// 의존하지 않는다. 알림뿐 아니라 팝업/툴팁/HUD 등 어떤 UI에도 붙일 수 있다.
    ///
    /// 기준 상태(anchoredPosition/localScale/CanvasGroup alpha)는 최초 초기화 때 한 번만 잡고,
    /// 이후 Tween 결과로 다시 잡지 않는다. 그래서 비활성화-재활성화를 반복하거나 풀에서 재사용해도
    /// 기준 위치가 이동 방향으로 계속 밀려나지 않는다. 레이아웃을 의도적으로 바꿨을 때만
    /// <see cref="CaptureBaseState"/>를 직접 호출한다.
    ///
    /// 이 컴포넌트는 대상 오브젝트를 스스로 비활성화하거나 삭제하지 않는다. 삭제/풀 반환/비활성화는
    /// 호출한 쪽이 <see cref="PlayExit(Action)"/>의 완료 콜백이나 On Exit Completed에서 결정한다.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    [RequireComponent(typeof(CanvasGroup))]
    [DisallowMultipleComponent]
    public class UITweenTransition : MonoBehaviour
    {
        // Duration이 0이어도 Sequence가 한 프레임은 살아 있어야 완료 콜백 경로가 항상 같아진다.
        private const float MinTweenDuration = 0.0001f;

        [Header("General")]
        [Tooltip("GameObject가 활성화될 때 자동으로 Enter를 재생한다. 끄면 외부 관리자가 PlayEnter()를 호출한다.")]
        [SerializeField] private bool playEnterOnEnable = true;

        [Tooltip("Time.timeScale과 무관하게 재생한다. 일시정지 중에도 UI 연출이 돌아가야 하므로 기본값은 켜짐이다.")]
        [SerializeField] private bool ignoreTimeScale = true;

        [Header("Enter Transition")]
        [Tooltip("등장 연출. 오프셋 위치/시작 알파에서 기준 상태로 들어온다.")]
        [SerializeField] private UITweenPhaseSettings enter = new UITweenPhaseSettings(0.25f, 0f, 1f, Ease.OutCubic);

        [Header("Exit Transition")]
        [Tooltip("종료 연출. 현재 상태에서 오프셋 위치/목표 알파로 나간다.")]
        [SerializeField] private UITweenPhaseSettings exit = new UITweenPhaseSettings(0.2f, 1f, 0f, Ease.InCubic);

        [Header("Events")]
        [Tooltip("Enter 재생을 시작할 때(Delay 시작 시점) 호출된다.")]
        [SerializeField] private UnityEvent onEnterStarted = new UnityEvent();

        [Tooltip("Enter가 끝난 뒤 호출된다.")]
        [SerializeField] private UnityEvent onEnterCompleted = new UnityEvent();

        [Tooltip("Exit 재생을 시작할 때(Delay 시작 시점) 호출된다.")]
        [SerializeField] private UnityEvent onExitStarted = new UnityEvent();

        [Tooltip("Exit가 끝난 뒤 호출된다. 삭제/풀 반환은 보통 여기서 처리한다.")]
        [SerializeField] private UnityEvent onExitCompleted = new UnityEvent();

        private RectTransform rect;
        private CanvasGroup canvasGroup;
        private Vector2 basePosition;
        private Vector3 baseScale;
        private float baseAlpha;
        private bool initialized;

        // 이 컴포넌트가 만든 Sequence만 보관한다. 다른 오브젝트의 Tween은 절대 건드리지 않는다.
        private Sequence activeSequence;
        private bool exitInProgress;
        private Action exitCompleteCallback;

        /// <summary>Enter든 Exit든 현재 재생 중인지 여부.</summary>
        public bool IsPlaying => activeSequence != null && activeSequence.IsActive() && activeSequence.IsPlaying();

        /// <summary>Exit가 진행 중인지 여부. 중복 종료 요청을 걸러낼 때 참고한다.</summary>
        public bool IsExiting => exitInProgress;

        private void Awake()
        {
            EnsureInitialized();
        }

        private void OnEnable()
        {
            if (!playEnterOnEnable) return;

            PlayEnter();
        }

        private void OnDisable()
        {
            // 파괴된/숨겨진 UI를 계속 참조하지 않도록 정리한다. 취소된 Exit의 완료 콜백은 호출되지 않으므로
            // 호출자는 반드시 Exit 완료 콜백에서만 비활성화/반환을 처리해야 한다. 강제로 끝내고 싶으면
            // 비활성화 전에 Complete()를 부른다.
            ClearActiveSequence();
        }

        private void OnDestroy()
        {
            ClearActiveSequence();
        }

        /// <summary>
        /// 기준 상태를 복원하고 Enter 시작 상태를 적용한 뒤 등장 연출을 재생한다.
        /// 재생 중에 다시 호출하면 기존 Tween을 정리하고 처음부터 다시 시작한다.
        /// Exit 재생 중에 호출하면 그 Exit는 취소되며 완료 콜백은 호출되지 않는다.
        /// </summary>
        public void PlayEnter()
        {
            EnsureInitialized();
            ClearActiveSequence();

            // Move/Fade/Scale 중 꺼진 채널이 이전 Tween 상태로 남아 있을 수 있으므로 기준 상태부터 되돌린다.
            RestoreBaseState();
            if (enter.Enabled)
            {
                ApplyEnterStartState();
            }

            onEnterStarted?.Invoke();
            activeSequence = BuildSequence(enter, false);
        }

        /// <summary>현재 상태에서 종료 연출을 재생한다.</summary>
        public void PlayExit()
        {
            PlayExit(null);
        }

        /// <summary>
        /// 현재 상태에서 종료 연출을 재생하고, 전부 끝난 뒤 <paramref name="onComplete"/>를 정확히 한 번 호출한다.
        /// 이미 Exit가 진행 중이면 연출을 다시 시작하지 않고 콜백만 합쳐 등록하므로, 중복 호출로 종료 처리가
        /// 두 번 실행되지 않는다.
        /// </summary>
        public void PlayExit(Action onComplete)
        {
            EnsureInitialized();

            if (exitInProgress && activeSequence != null && activeSequence.IsActive())
            {
                if (onComplete != null)
                {
                    // 같은 대상/메서드를 두 번 등록해 종료 처리가 두 번 돌지 않게 한 번 빼고 다시 넣는다.
                    exitCompleteCallback -= onComplete;
                    exitCompleteCallback += onComplete;
                }
                return;
            }

            ClearActiveSequence();
            exitInProgress = true;
            exitCompleteCallback = onComplete;

            // Exit는 시작 상태를 따로 적용하지 않는다. 현재 위치/알파/크기에서 그대로 이어가야
            // Enter 도중에 들어와도 튀지 않는다.
            onExitStarted?.Invoke();
            activeSequence = BuildSequence(exit, true);
        }

        /// <summary>재생 중인 Tween을 현재 상태에서 중단한다. 완료 콜백과 완료 이벤트는 호출되지 않는다.</summary>
        public void Stop()
        {
            ClearActiveSequence();
        }

        /// <summary>재생 중인 Tween을 즉시 끝까지 진행시킨다. 완료 콜백과 완료 이벤트는 정상적으로 호출된다.</summary>
        public void Complete()
        {
            Sequence seq = activeSequence;
            if (seq == null || !seq.IsActive()) return;

            seq.Complete(true);
        }

        /// <summary>
        /// 현재 anchoredPosition/localScale/CanvasGroup alpha를 새 기준 상태로 저장한다.
        /// 자동으로는 최초 1회만 저장하므로, 레이아웃이나 기준 위치를 의도적으로 바꿨을 때만 직접 호출한다.
        /// Tween 재생 중에 호출하면 변형된 중간 상태가 기준으로 굳으니 Stop() 이후에 쓴다.
        /// </summary>
        public void CaptureBaseState()
        {
            EnsureInitialized();
            CaptureBaseStateInternal();
        }

        /// <summary>기준 상태로 즉시 되돌린다. 실행 중인 Tween은 정리하지 않으므로 필요하면 Stop()을 먼저 호출한다.</summary>
        public void RestoreBaseState()
        {
            EnsureInitialized();
            rect.anchoredPosition = basePosition;
            rect.localScale = baseScale;
            canvasGroup.alpha = baseAlpha;
        }

        private void EnsureInitialized()
        {
            if (initialized) return;
            initialized = true;

            rect = GetComponent<RectTransform>();
            canvasGroup = GetComponent<CanvasGroup>();
            if (canvasGroup == null)
            {
                // RequireComponent가 에디터에서는 자동으로 붙여주지만, 런타임 AddComponent 경로도 막아둔다.
                canvasGroup = gameObject.AddComponent<CanvasGroup>();
            }

            CaptureBaseStateInternal();
        }

        private void CaptureBaseStateInternal()
        {
            basePosition = rect.anchoredPosition;
            baseScale = rect.localScale;
            baseAlpha = canvasGroup.alpha;
        }

        private void ApplyEnterStartState()
        {
            if (enter.MoveEnabled)
            {
                rect.anchoredPosition = basePosition + enter.ResolveOffset();
            }
            if (enter.FadeEnabled)
            {
                canvasGroup.alpha = enter.FromAlpha;
            }
            if (enter.ScaleEnabled)
            {
                rect.localScale = Vector3.Scale(baseScale, enter.FromScale);
            }
        }

        /// <summary>Move/Fade/Scale을 같은 시점에 시작하는 Sequence 하나로 묶어 재생한다.</summary>
        private Sequence BuildSequence(UITweenPhaseSettings settings, bool isExit)
        {
            float duration = Mathf.Max(MinTweenDuration, settings.Duration);
            Sequence seq = DOTween.Sequence();
            int channelCount = 0;

            if (settings.Enabled)
            {
                if (settings.MoveEnabled)
                {
                    // Enter는 기준 위치로 들어오고, Exit는 기준 위치 + 오프셋으로 나간다.
                    Vector2 target = isExit ? basePosition + settings.ResolveOffset() : basePosition;
                    Tween move = rect.DOAnchorPos(target, duration);
                    settings.MoveEase.Apply(move);
                    seq.Insert(0f, move);
                    channelCount++;
                }

                if (settings.FadeEnabled)
                {
                    Tween fade = canvasGroup.DOFade(settings.ToAlpha, duration);
                    settings.FadeEase.Apply(fade);
                    seq.Insert(0f, fade);
                    channelCount++;
                }

                if (settings.ScaleEnabled)
                {
                    Tween scale = rect.DOScale(Vector3.Scale(baseScale, settings.ToScale), duration);
                    settings.ScaleEase.Apply(scale);
                    seq.Insert(0f, scale);
                    channelCount++;
                }
            }

            if (channelCount == 0)
            {
                // 채널이 전부 꺼져 있거나 Phase 자체가 꺼져 있어도 완료 콜백이 누락되면 안 된다.
                // Phase가 꺼져 있으면 기다릴 이유가 없으니 한 프레임 만에 끝내고, Phase는 켜져 있는데
                // 채널만 전부 꺼진 경우는 타이밍 계약(Duration)을 그대로 지킨다.
                seq.AppendInterval(settings.Enabled ? duration : MinTweenDuration);
            }

            if (settings.Enabled && settings.Delay > 0f)
            {
                // SetDelay 대신 명시적 선행 Interval을 넣어 Sequence에서의 동작을 확실하게 만든다.
                seq.PrependInterval(settings.Delay);
            }

            seq.SetUpdate(ignoreTimeScale)
                .SetLink(gameObject)
                .OnComplete(() =>
                {
                    // 호출자 콜백에서 SetActive(false)나 Destroy를 해도 OnDisable/OnDestroy가 이 Sequence를
                    // 다시 건드리지 않도록 참조를 먼저 지운다.
                    if (activeSequence == seq)
                    {
                        activeSequence = null;
                    }

                    if (isExit)
                    {
                        HandleExitCompleted();
                    }
                    else
                    {
                        HandleEnterCompleted();
                    }
                })
                .OnKill(() =>
                {
                    // 콜백 안에서 새 Tween이 시작됐을 수 있으니, 내가 만든 그 Sequence일 때만 참조를 지운다.
                    if (activeSequence == seq)
                    {
                        activeSequence = null;
                    }
                });

            return seq;
        }

        private void HandleEnterCompleted()
        {
            onEnterCompleted?.Invoke();
        }

        private void HandleExitCompleted()
        {
            exitInProgress = false;
            Action callback = exitCompleteCallback;
            exitCompleteCallback = null;

            onExitCompleted?.Invoke();

            // 코드 콜백은 보통 삭제나 풀 반환이라 가장 마지막에 호출한다.
            callback?.Invoke();
        }

        private void ClearActiveSequence()
        {
            Sequence seq = activeSequence;
            activeSequence = null;
            exitInProgress = false;
            exitCompleteCallback = null;

            if (seq != null && seq.IsActive())
            {
                // complete:false - 취소된 Tween의 완료 콜백은 호출하지 않는다.
                seq.Kill();
            }
        }
    }
}
