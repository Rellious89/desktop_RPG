using System;
using System.Globalization;
using Common;
using DesktopWindow;
using TMPro;
using UnityEngine;

namespace Dungeon
{
    /// <summary>
    /// 모닥불 휴식 상태를 월드 앵커 위의 uGUI 말풍선으로 보여 준다.
    /// 실제 UI는 AlwaysOn Canvas에 두고, CharacterSlot1 아래의 월드 앵커만 화면 좌표로 투영한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DungeonRestEventDescriptionPresenter : MonoBehaviour
    {
        private const int MaximumRestStatusSlots = 3;
        private const string StatusInstanceNamePrefix = "__RestStatusSlot";

        [Header("Runtime References")]
        [SerializeField] private DungeonPartyRestEventController restEventController;
        [SerializeField] private Camera stageCamera;
        [Tooltip("DungeonRestEventRoot/CharacterSlot1 아래의 UIAnchor입니다.")]
        [SerializeField] private Transform worldAnchor;

        [Header("UI References")]
        [Tooltip("월드 좌표를 로컬 좌표로 변환할 AlwaysOn RectTransform입니다.")]
        [SerializeField] private RectTransform uiParent;
        [SerializeField] private RectTransform presentationRoot;
        [Tooltip("SpeechBubble 프리팹 인스턴스의 루트입니다. Button과 UI/128 라벨은 이 아래에서 찾습니다.")]
        [SerializeField] private RectTransform speechBubbleRoot;
        [SerializeField] private GameObject detailRoot;
        [Tooltip("UI/130 포맷에 Resume Stamina Ratio를 퍼센트로 채울 TMP입니다.")]
        [SerializeField] private TextMeshProUGUI resumeValueText;
        [Tooltip("Canvas/AlwaysOn/RestEventDescription/Status_SP 비활성 원본입니다. 원본은 활성화하지 않고 런타임 복제에만 사용합니다.")]
        [SerializeField] private RectTransform statusPrototype;
        [Tooltip("DungeonRestEventRoot/CharacterSlot1~3 아래 UIAnchor를 슬롯 순서대로 연결합니다.")]
        [SerializeField] private Transform[] statusWorldAnchors = new Transform[MaximumRestStatusSlots];

        [Header("Presentation Tuning")]
        [Tooltip("회복중. → 회복중.. → 회복중... 점 연출의 갱신 간격(초)입니다. 게임 시간 배율의 영향을 받지 않습니다.")]
        [Min(0.05f)]
        [SerializeField] private float statusDotInterval = 0.5f;
        [Tooltip("detail 토스트가 활성화된 시점부터 비활성화되기까지의 시간(초)입니다. detail 위에 " +
                 "마우스가 있으면 시간이 지나도 표시되며, 마우스가 벗어나는 즉시(이미 만료된 경우) 닫힙니다.")]
        [Min(0.01f)]
        [SerializeField] private float detailToastDuration = 3f;
        [Tooltip("월드 앵커를 Canvas 좌표로 바꾼 뒤 더할 UI 위치 오프셋입니다.")]
        [SerializeField] private Vector2 uiOffset = Vector2.zero;

        [Header("Stamina Status Tuning")]
        [Tooltip("각 캐릭터의 RestStatusSlot을 월드 앵커에서 투영한 위치에 더할 공통 UI 오프셋입니다.")]
        [SerializeField] private Vector2 restStatusSlotOffset = Vector2.zero;
        [Tooltip("이 퍼센트 미만을 Low 색상으로 표시합니다.")]
        [Min(0)]
        [SerializeField] private int lowStaminaPercentThreshold = 50;
        [Tooltip("이 퍼센트 미만을 Recovering 색상으로, 이상을 Ready 색상으로 표시합니다.")]
        [Min(0)]
        [SerializeField] private int readyStaminaPercentThreshold = 100;
        [SerializeField] private Color lowStaminaColor = Color.red;
        [SerializeField] private Color recoveringStaminaColor = Color.yellow;
        [SerializeField] private Color readyStaminaColor = Color.green;
        [Tooltip("행동력이 100% 이상일 때 표시할 로컬라이즈 텍스트입니다. 기본값은 UI/131입니다.")]
        [SerializeField] private LocalizedTextReference readyStaminaText = new LocalizedTextReference();

        private UnityEngine.UI.Button speechBubbleButton;
        private TextMeshProUGUI statusText;
        private LocalizedTMPText statusLocalizer;
        private LocalizedTMPText valueLocalizer;
        private LocalizedTextReference boundStatusReference;
        private LocalizedTextReference boundValueReference;
        private LocalizedTextReference boundReadyStaminaReference;
        private Canvas parentCanvas;

        private string localizedStatusBase = string.Empty;
        private string localizedValueFormat = string.Empty;
        private string localizedReadyStaminaText = string.Empty;
        private bool localizationSubscribed;
        private bool buttonListenerAttached;
        private bool presentationVisible;
        private bool formatFailureLogged;
        private MenuPointerRegion detailPointerRegion;
        private UITweenTransition detailTransition;
        private int displayedResumePercent = int.MinValue;
        private int dotCount = 1;
        private float nextDotTime;
        private float detailCloseAt;
        private RectTransform[] statusRoots = Array.Empty<RectTransform>();
        private TextMeshProUGUI[] statusValueTexts = Array.Empty<TextMeshProUGUI>();
        private UITweenTransition[] statusTransitions = Array.Empty<UITweenTransition>();
        private bool statusExitInProgress;
        private bool detailExitCompleted;
        private int statusExitPendingCount;

        /// <summary>복귀 비율을 UI에 표시할 정수 퍼센트로 바꾼다.</summary>
        public static int ToResumePercent(float ratio)
        {
            return Mathf.RoundToInt(Mathf.Clamp01(ratio) * 100f);
        }

        /// <summary>말줄임 점 개수를 1 → 2 → 3 → 1 순서로 순환한다.</summary>
        public static int NextDotCount(int current)
        {
            return current >= 1 && current < 3 ? current + 1 : 1;
        }

        /// <summary>번역된 기본 문구 뒤에 1~3개의 점을 붙인다.</summary>
        public static string ComposeStatus(string baseText, int requestedDotCount)
        {
            string normalized = NormalizeStatusBase(baseText);
            int clampedDotCount = Mathf.Clamp(requestedDotCount, 1, 3);
            return normalized + new string('.', clampedDotCount);
        }

        /// <summary>UI/130 포맷의 {0}에 복귀 비율 퍼센트를 채운다.</summary>
        public static string ComposeResumeValue(string format, float ratio, out bool formatFailed)
        {
            formatFailed = false;
            if (string.IsNullOrEmpty(format)) return string.Empty;

            try
            {
                return string.Format(CultureInfo.InvariantCulture, format, ToResumePercent(ratio));
            }
            catch (FormatException)
            {
                formatFailed = true;
                return format;
            }
        }

        /// <summary>
        /// 복귀 필요량을 100%로 삼는 행동력 진행률. 기준 미만이 반올림으로 100%가 되지 않도록
        /// 내림하며, 기준을 넘긴 실제 진행률은 100%로 제한하지 않는다.
        /// </summary>
        public static int ToRestStaminaPercent(int currentStamina, int requiredStamina)
        {
            if (currentStamina <= 0 || requiredStamina <= 0) return 0;

            long percent = (long)currentStamina * 100L / requiredStamina;
            return percent >= int.MaxValue ? int.MaxValue : (int)percent;
        }

        /// <summary>행동력 진행률 표시를 고른다. Ready 이상이면 숫자 대신 로컬라이즈 문구를 사용한다.</summary>
        public static string ComposeRestStaminaValue(int progressPercent, int readyThresholdPercent, string readyText)
        {
            int readyThreshold = Mathf.Max(0, readyThresholdPercent);
            return progressPercent >= readyThreshold
                ? (readyText ?? string.Empty)
                : progressPercent.ToString(CultureInfo.InvariantCulture) + "%";
        }

        /// <summary>Inspector 임계값을 적용해 진행률 표시 색상을 고른다.</summary>
        public static Color SelectRestStaminaColor(
            int progressPercent,
            int lowThresholdPercent,
            int readyThresholdPercent,
            Color lowColor,
            Color recoveringColor,
            Color readyColor)
        {
            int lowThreshold = Mathf.Max(0, lowThresholdPercent);
            int readyThreshold = Mathf.Max(lowThreshold, readyThresholdPercent);
            if (progressPercent < lowThreshold) return lowColor;
            return progressPercent < readyThreshold ? recoveringColor : readyColor;
        }

        private void Awake()
        {
            ResolveUiReferences();
            TakeLocalizationOwnership();
            EnsureSpeechBubbleInputRegion();
            EnsureDetailInputRegion();
            EnsureStatusInstances();

            if (presentationRoot != null) presentationRoot.gameObject.SetActive(false);
            if (detailRoot != null) detailRoot.SetActive(false);
            HideStatusViews();
        }

        private void OnEnable()
        {
            ResolveUiReferences();
            TakeLocalizationOwnership();
            EnsureSpeechBubbleInputRegion();
            EnsureDetailInputRegion();
            EnsureStatusInstances();
            SubscribeLocalization();
            AttachButtonListener();
            SetPresentationVisible(restEventController != null && restEventController.IsResting);
        }

        private void LateUpdate()
        {
            bool shouldBeVisible = restEventController != null && restEventController.IsResting;
            if (shouldBeVisible != presentationVisible)
                SetPresentationVisible(shouldBeVisible);

            if (!shouldBeVisible) return;

            UpdateProjectedPosition();
            RefreshResumeValue(false);
            RefreshStatusViews();
            TickStatusDots();
            TickDetailToast();
        }

        private void OnDisable()
        {
            DetachButtonListener();
            UnsubscribeLocalization();
            SetPresentationVisible(false);
        }

        private void OnDestroy()
        {
            DestroyStatusInstances();
        }

        private void OnValidate()
        {
            statusDotInterval = Mathf.Max(0.05f, statusDotInterval);
            detailToastDuration = Mathf.Max(0.01f, detailToastDuration);
            lowStaminaPercentThreshold = Mathf.Max(0, lowStaminaPercentThreshold);
            readyStaminaPercentThreshold = Mathf.Max(
                lowStaminaPercentThreshold,
                readyStaminaPercentThreshold);
        }

        private void ResolveUiReferences()
        {
            EnsureReadyStaminaTextReference();
            if (presentationRoot != null && parentCanvas == null)
                parentCanvas = presentationRoot.GetComponentInParent<Canvas>(true);

            if (speechBubbleRoot != null)
            {
                if (speechBubbleButton == null)
                    speechBubbleButton = speechBubbleRoot.GetComponent<UnityEngine.UI.Button>();
                if (statusLocalizer == null)
                    statusLocalizer = speechBubbleRoot.GetComponentInChildren<LocalizedTMPText>(true);
                if (statusText == null)
                    statusText = speechBubbleRoot.GetComponentInChildren<TextMeshProUGUI>(true);
            }

            if (resumeValueText != null && valueLocalizer == null)
                valueLocalizer = resumeValueText.GetComponent<LocalizedTMPText>();

            if (detailRoot != null && detailTransition == null)
                detailTransition = detailRoot.GetComponent<UITweenTransition>();

            if (string.IsNullOrEmpty(localizedStatusBase) && statusText != null)
                localizedStatusBase = NormalizeStatusBase(statusText.text);
            if (string.IsNullOrEmpty(localizedValueFormat) && resumeValueText != null)
                localizedValueFormat = resumeValueText.text ?? string.Empty;
        }

        private void TakeLocalizationOwnership()
        {
            // 두 LocalizedTMPText가 같은 라벨을 다시 쓰면 점/포맷 인자가 사라질 수 있다.
            // 참조값은 그대로 재사용하되 실제 TMP 쓰기는 이 Presenter 한 곳에서만 담당한다.
            if (statusLocalizer != null) statusLocalizer.enabled = false;
            if (valueLocalizer != null) valueLocalizer.enabled = false;
        }

        private void SubscribeLocalization()
        {
            if (localizationSubscribed) return;

            boundStatusReference = statusLocalizer != null ? statusLocalizer.TextReference : null;
            boundValueReference = valueLocalizer != null ? valueLocalizer.TextReference : null;
            boundReadyStaminaReference = readyStaminaText;

            if (boundStatusReference != null && boundStatusReference.HasReference)
                boundStatusReference.StringChanged += ApplyLocalizedStatus;
            if (boundValueReference != null && boundValueReference.HasReference)
                boundValueReference.StringChanged += ApplyLocalizedValueFormat;
            if (boundReadyStaminaReference != null && boundReadyStaminaReference.HasReference)
                boundReadyStaminaReference.StringChanged += ApplyLocalizedReadyStaminaText;

            localizationSubscribed = true;
        }

        private void UnsubscribeLocalization()
        {
            if (!localizationSubscribed) return;

            if (boundStatusReference != null && boundStatusReference.HasReference)
                boundStatusReference.StringChanged -= ApplyLocalizedStatus;
            if (boundValueReference != null && boundValueReference.HasReference)
                boundValueReference.StringChanged -= ApplyLocalizedValueFormat;
            if (boundReadyStaminaReference != null && boundReadyStaminaReference.HasReference)
                boundReadyStaminaReference.StringChanged -= ApplyLocalizedReadyStaminaText;

            boundStatusReference = null;
            boundValueReference = null;
            boundReadyStaminaReference = null;
            localizationSubscribed = false;
        }

        private void ApplyLocalizedStatus(string localizedText)
        {
            localizedStatusBase = NormalizeStatusBase(localizedText);
            RefreshStatusText();
        }

        private void ApplyLocalizedValueFormat(string localizedText)
        {
            localizedValueFormat = localizedText ?? string.Empty;
            formatFailureLogged = false;
            RefreshResumeValue(true);
        }

        private void ApplyLocalizedReadyStaminaText(string localizedText)
        {
            localizedReadyStaminaText = localizedText ?? string.Empty;
            RefreshStatusViews();
        }

        private void EnsureReadyStaminaTextReference()
        {
            if (readyStaminaText == null)
                readyStaminaText = new LocalizedTextReference();
            if (readyStaminaText.HasReference) return;

            readyStaminaText.TableReference = "01_UI";
            readyStaminaText.TableEntryReference = "131";
        }

        private void AttachButtonListener()
        {
            if (buttonListenerAttached || speechBubbleButton == null) return;
            speechBubbleButton.onClick.AddListener(ToggleDetail);
            buttonListenerAttached = true;
        }

        private void DetachButtonListener()
        {
            if (!buttonListenerAttached || speechBubbleButton == null) return;
            speechBubbleButton.onClick.RemoveListener(ToggleDetail);
            buttonListenerAttached = false;
        }

        private void EnsureSpeechBubbleInputRegion()
        {
            if (speechBubbleButton == null) return;

            WindowInputRegion inputRegion = speechBubbleButton.GetComponent<WindowInputRegion>();
            if (inputRegion == null)
                inputRegion = speechBubbleButton.gameObject.AddComponent<WindowInputRegion>();
            inputRegion.ReceiveMouseInput = true;
        }

        private void EnsureDetailInputRegion()
        {
            if (detailRoot == null) return;

            detailPointerRegion = detailRoot.GetComponent<MenuPointerRegion>();
            if (detailPointerRegion == null)
                detailPointerRegion = detailRoot.AddComponent<MenuPointerRegion>();

            WindowInputRegion inputRegion = detailRoot.GetComponent<WindowInputRegion>();
            if (inputRegion == null)
                inputRegion = detailRoot.AddComponent<WindowInputRegion>();
            inputRegion.ReceiveMouseInput = true;
        }

        private void SetPresentationVisible(bool visible)
        {
            presentationVisible = visible;

            if (presentationRoot != null && presentationRoot.gameObject.activeSelf != visible)
                presentationRoot.gameObject.SetActive(visible);

            if (!visible)
            {
                HideDetailImmediately();
                HideStatusViews();
                displayedResumePercent = int.MinValue;
                dotCount = 1;
                return;
            }

            HideDetailImmediately();
            dotCount = 1;
            nextDotTime = Time.unscaledTime + Mathf.Max(0.05f, statusDotInterval);
            RefreshStatusText();
            RefreshResumeValue(true);
            UpdateProjectedPosition();
            RefreshStatusViews();
        }

        private void ToggleDetail()
        {
            if (!presentationVisible || detailRoot == null) return;

            if (detailTransition != null && detailTransition.IsExiting)
            {
                ShowDetail();
                return;
            }

            bool show = !detailRoot.activeSelf;
            if (!show)
            {
                HideDetail();
                return;
            }

            ShowDetail();
        }

        private void ShowDetail()
        {
            if (!presentationVisible || detailRoot == null) return;

            RefreshResumeValue(true);
            CancelStatusExitForReopen();
            if (!detailRoot.activeSelf)
                detailRoot.SetActive(true);

            // RestStatusSlot은 detail 정보의 일부이므로 detail을 연 프레임에 즉시
            // 표시한다. 종료 전환 중이었다면 아래 Enter 호출이 슬롯의 Exit를 취소하고
            // 현재 상태에서 자연스럽게 복귀시킨다.
            RefreshStatusViews();
            PlayEnterStatusViews();

            // 포인터 재진입/재클릭으로 종료 중 토스트를 다시 열 수 있다.
            if (detailTransition != null && detailTransition.IsExiting)
                detailTransition.PlayEnter();

            detailCloseAt = Time.unscaledTime + GetDetailToastDuration();
        }

        private void TickDetailToast()
        {
            if (detailRoot == null || !detailRoot.activeSelf) return;

            float now = Time.unscaledTime;
            if (detailPointerRegion != null && detailPointerRegion.PointerInside)
            {
                if (detailTransition != null && detailTransition.IsExiting)
                {
                    CancelStatusExitForReopen();
                    detailTransition.PlayEnter();
                }

                // 활성화 시점 기준 타이머는 진행하되, hover 중에는 토스트를 유지한다.
                return;
            }

            if (now >= detailCloseAt)
                HideDetail();
        }

        private void HideDetail()
        {
            if (detailRoot == null)
            {
                HideStatusViews();
                return;
            }

            if (!detailRoot.activeSelf)
            {
                detailCloseAt = 0f;
                HideStatusViews();
                return;
            }

            if (detailTransition == null)
            {
                detailRoot.SetActive(false);
                detailCloseAt = 0f;
                HideStatusViews();
                return;
            }

            if (!detailTransition.IsExiting)
            {
                PlayExitStatusViews();
                detailTransition.PlayExit(() => CompleteHideDetail(detailTransition));
            }

            detailCloseAt = 0f;
        }

        private void CompleteHideDetail(UITweenTransition transition)
        {
            if (detailRoot != null && detailRoot.activeSelf)
                detailRoot.SetActive(false);

            detailCloseAt = 0f;
            detailExitCompleted = true;
            if (statusExitPendingCount == 0)
                FinishStatusExit();
        }

        private void HideDetailImmediately()
        {
            if (detailTransition != null && detailTransition.IsExiting)
                detailTransition.Stop();

            if (detailRoot != null)
                detailRoot.SetActive(false);

            detailCloseAt = 0f;
            HideStatusViews();
        }

        private void PlayEnterStatusViews()
        {
            for (int i = 0; i < statusRoots.Length; i++)
            {
                RectTransform root = statusRoots[i];
                if (root == null || !root.gameObject.activeSelf) continue;

                UITweenTransition transition = i < statusTransitions.Length ? statusTransitions[i] : null;
                if (transition != null)
                    transition.PlayEnter();
            }
        }

        private void CancelStatusExitForReopen()
        {
            statusExitInProgress = false;
            detailExitCompleted = false;
            statusExitPendingCount = 0;
            PlayEnterStatusViews();
        }

        private void PlayExitStatusViews()
        {
            statusExitInProgress = true;
            detailExitCompleted = false;
            statusExitPendingCount = 0;
            for (int i = 0; i < statusRoots.Length; i++)
            {
                RectTransform root = statusRoots[i];
                if (root == null || !root.gameObject.activeSelf) continue;

                statusExitPendingCount++;

                UITweenTransition transition = i < statusTransitions.Length ? statusTransitions[i] : null;
                if (transition != null)
                {
                    RectTransform capturedRoot = root;
                    transition.PlayExit(() => CompleteHideStatus(capturedRoot));
                }
                else
                {
                    CompleteHideStatus(root);
                }
            }

            if (statusExitPendingCount == 0 && detailExitCompleted)
                FinishStatusExit();
        }

        private void CompleteHideStatus(RectTransform root)
        {
            if (root != null && root.gameObject.activeSelf)
                root.gameObject.SetActive(false);

            if (statusExitPendingCount > 0)
                statusExitPendingCount--;
            if (detailExitCompleted && statusExitPendingCount == 0)
                FinishStatusExit();
        }

        private void FinishStatusExit()
        {
            statusExitInProgress = false;
            detailExitCompleted = false;
            statusExitPendingCount = 0;
        }

        private float GetDetailToastDuration()
        {
            return Mathf.Max(0.01f, detailToastDuration);
        }

        private void TickStatusDots()
        {
            float now = Time.unscaledTime;
            if (now < nextDotTime) return;

            float interval = Mathf.Max(0.05f, statusDotInterval);
            do
            {
                dotCount = NextDotCount(dotCount);
                nextDotTime += interval;
            } while (now >= nextDotTime);

            RefreshStatusText();
        }

        private void RefreshStatusText()
        {
            if (statusText != null)
                statusText.text = ComposeStatus(localizedStatusBase, dotCount);
        }

        private void RefreshResumeValue(bool force)
        {
            if (resumeValueText == null || restEventController == null) return;

            int resumePercent = ToResumePercent(restEventController.ResumeStaminaRatio);
            if (!force && resumePercent == displayedResumePercent) return;

            bool formatFailed;
            resumeValueText.text = ComposeResumeValue(
                localizedValueFormat,
                restEventController.ResumeStaminaRatio,
                out formatFailed);
            displayedResumePercent = resumePercent;

            if (formatFailed && !formatFailureLogged)
            {
                formatFailureLogged = true;
                Debug.LogWarning("[DungeonRestEventDescription] UI/130의 {0} 포맷을 적용할 수 없습니다.", this);
            }
        }

        private void UpdateProjectedPosition()
        {
            if (worldAnchor == null || stageCamera == null || uiParent == null || presentationRoot == null) return;

            if (TryGetCanvasLocalPoint(worldAnchor, out Vector2 localPoint))
                presentationRoot.anchoredPosition = localPoint + uiOffset;
        }

        private void RefreshStatusViews()
        {
            // LateUpdate는 휴식 중인 동안 계속 호출되므로, detail이 닫힌 뒤에도
            // 슬롯이 다시 켜지지 않도록 표시 조건을 이 메서드에서 최종적으로
            // 차단한다. Exit tween 중에는 detailRoot가 아직 활성 상태이므로
            // 실제 비활성화 시점까지 슬롯을 유지한다.
            if (!presentationVisible)
            {
                HideStatusViews();
                return;
            }

            if (detailRoot == null || !detailRoot.activeSelf)
            {
                if (statusExitInProgress) return;
                HideStatusViews();
                return;
            }

            // A slot's Exit callback owns its deactivation. Do not reactivate it
            // while the detail/status exit is still draining.
            if (statusExitInProgress) return;

            EnsureStatusInstances();
            int slotCount = Mathf.Min(statusRoots.Length, MaximumRestStatusSlots);
            for (int i = 0; i < slotCount; i++)
            {
                RectTransform root = statusRoots[i];
                TextMeshProUGUI valueText = i < statusValueTexts.Length ? statusValueTexts[i] : null;
                Transform anchor = statusWorldAnchors != null && i < statusWorldAnchors.Length
                    ? statusWorldAnchors[i]
                    : null;

                if (root == null || valueText == null || anchor == null || restEventController == null
                    || !restEventController.TryGetRestSlotStatus(
                        i,
                        out DungeonPartyRestEventController.RestSlotStatus status)
                    || !TryGetCanvasLocalPoint(anchor, out Vector2 localPoint))
                {
                    if (root != null) root.gameObject.SetActive(false);
                    continue;
                }

                int percent = ToRestStaminaPercent(status.CurrentStamina, status.RequiredStamina);
                valueText.text = ComposeRestStaminaValue(
                    percent,
                    readyStaminaPercentThreshold,
                    localizedReadyStaminaText);
                valueText.color = SelectRestStaminaColor(
                    percent,
                    lowStaminaPercentThreshold,
                    readyStaminaPercentThreshold,
                    lowStaminaColor,
                    recoveringStaminaColor,
                    readyStaminaColor);
                root.anchoredPosition = localPoint + restStatusSlotOffset;
                if (!root.gameObject.activeSelf) root.gameObject.SetActive(true);
            }

            for (int i = slotCount; i < statusRoots.Length; i++)
            {
                if (statusRoots[i] != null) statusRoots[i].gameObject.SetActive(false);
            }
        }

        private bool TryGetCanvasLocalPoint(Transform anchor, out Vector2 localPoint)
        {
            localPoint = default;
            if (anchor == null || stageCamera == null || uiParent == null) return false;

            Vector3 screenPoint = stageCamera.WorldToScreenPoint(anchor.position);
            if (screenPoint.z < 0f) return false;

            Camera uiCamera = parentCanvas != null && parentCanvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? parentCanvas.worldCamera
                : null;
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(
                uiParent,
                screenPoint,
                uiCamera,
                out localPoint);
        }

        private void EnsureStatusInstances()
        {
            if (statusPrototype == null || uiParent == null) return;

            // Status_SP is an authoring prototype only. Keep it hidden even if an
            // inspector edit accidentally enabled it, otherwise it would appear
            // alongside the runtime clones when the presentation root is shown.
            if (statusPrototype.gameObject.activeSelf)
                statusPrototype.gameObject.SetActive(false);

            int requestedCount = Mathf.Min(
                MaximumRestStatusSlots,
                statusWorldAnchors != null ? statusWorldAnchors.Length : 0);
            if (statusRoots.Length == requestedCount && AllStatusInstancesExist()) return;

            statusRoots = new RectTransform[requestedCount];
            statusValueTexts = new TextMeshProUGUI[requestedCount];
            statusTransitions = new UITweenTransition[requestedCount];
            for (int i = 0; i < requestedCount; i++)
            {
                string instanceName = StatusInstanceNamePrefix + (i + 1);
                RectTransform instance = uiParent.Find(instanceName) as RectTransform;
                if (instance == null)
                {
                    instance = Instantiate(statusPrototype, uiParent, false);
                    instance.name = instanceName;
                    instance.gameObject.hideFlags = HideFlags.DontSave;
                }

                ConfigureStatusInstance(instance);
                statusRoots[i] = instance;

                Transform value = instance.Find("lb_value");
                statusValueTexts[i] = value != null
                    ? value.GetComponent<TextMeshProUGUI>()
                    : instance.GetComponentInChildren<TextMeshProUGUI>(true);
                statusTransitions[i] = instance.GetComponent<UITweenTransition>();
                instance.gameObject.SetActive(false);
            }
        }

        private bool AllStatusInstancesExist()
        {
            for (int i = 0; i < statusRoots.Length; i++)
            {
                if (statusRoots[i] == null) return false;
            }
            return true;
        }

        private static void ConfigureStatusInstance(RectTransform instance)
        {
            UnityEngine.UI.Graphic[] graphics = instance.GetComponentsInChildren<UnityEngine.UI.Graphic>(true);
            for (int i = 0; i < graphics.Length; i++)
                graphics[i].raycastTarget = false;

            CanvasGroup canvasGroup = instance.GetComponent<CanvasGroup>();
            if (canvasGroup == null) canvasGroup = instance.gameObject.AddComponent<CanvasGroup>();
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }

        private void HideStatusViews()
        {
            statusExitInProgress = false;
            detailExitCompleted = false;
            statusExitPendingCount = 0;
            for (int i = 0; i < statusRoots.Length; i++)
            {
                RectTransform root = statusRoots[i];
                if (root == null) continue;

                UITweenTransition transition = i < statusTransitions.Length ? statusTransitions[i] : null;
                if (transition != null) transition.Stop();
                root.gameObject.SetActive(false);
            }
        }

        private void DestroyStatusInstances()
        {
            for (int i = 0; i < statusRoots.Length; i++)
            {
                RectTransform root = statusRoots[i];
                if (root == null) continue;

                if (Application.isPlaying) Destroy(root.gameObject);
                else DestroyImmediate(root.gameObject);
            }

            statusRoots = Array.Empty<RectTransform>();
            statusValueTexts = Array.Empty<TextMeshProUGUI>();
            statusTransitions = Array.Empty<UITweenTransition>();
            statusExitInProgress = false;
            detailExitCompleted = false;
            statusExitPendingCount = 0;
        }

        private static string NormalizeStatusBase(string value)
        {
            return string.IsNullOrEmpty(value) ? string.Empty : value.TrimEnd('.');
        }
    }
}
