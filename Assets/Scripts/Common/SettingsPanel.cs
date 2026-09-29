using System;
using System.Collections;
using System.Collections.Generic;
using DesktopWindow;
using TMPro;
using UnityEngine;

namespace Common
{
    /// <summary>설정 패널의 열기, 닫기, ESC 및 포커스 처리를 공통 모달 흐름에 연결한다.</summary>
    [DisallowMultipleComponent]
    public sealed class SettingsPanel : ModalPanel
    {
        [SerializeField] private TMP_Dropdown displayDropdown;
        [SerializeField] private UnityEngine.UI.Button uiResetButton;
        [SerializeField] private UnityEngine.UI.Slider uiOpacitySlider;
        [SerializeField] private UnityEngine.UI.Image uiOpacityFillImage;
        [SerializeField] private Color uiOpacityFillColor = new Color(0.003921569f, 0.8627451f, 1f, 1f);
        [SerializeField] private Color uiOpacityMinimumFillColor = new Color(1f, 0.25f, 0.1f, 1f);
        [SerializeField] private TextMeshProUGUI uiOpacityMinimumDescription;
        [SerializeField] private UITweenTransition uiOpacityMinimumTransition;
        [SerializeField] private UIShakeTween uiOpacityLimitShake;

        private LocalizedTextReference opacityMinimumText;
        private readonly object[] opacityMinimumArguments = new object[1];
        private int displayedMinimumOpacityPercent = -1;
        private float observedMinimumOpacity = float.NaN;
        private bool opacityMinimumTextSubscribed;
        private bool opacitySavePending;
        private bool opacityShakePlayedForCurrentPress;
        private float lastOpacityChangeTime;
        private const float OpacitySaveDelay = 0.4f;

        private readonly List<TransparentWindowController.MonitorChoice> displayedMonitors =
            new List<TransparentWindowController.MonitorChoice>();
        private Coroutine monitorRefresh;
        private Coroutine pendingMonitorMove;

        protected override void OnModalOpened()
        {
            ResolveOpacityControls();
            if (uiOpacitySlider != null)
            {
                uiOpacitySlider.onValueChanged.AddListener(OnOpacityChanged);
                if (uiOpacitySlider is MouseOnlySlider mouseSlider)
                    mouseSlider.PointerReleased += OnOpacityPointerReleased;
            }
            RefreshOpacityMinimumText(UiOpacityController.Instance);

            ResolveResetButton();
            if (uiResetButton != null)
            {
                uiResetButton.onClick.RemoveListener(ResetUiPlacement);
                uiResetButton.onClick.AddListener(ResetUiPlacement);
            }

            ResolveDropdown();
            if (displayDropdown == null) return;

            // TMP_Dropdown은 Template을 복제해 패널 아래까지 목록을 펼친다.
            // 복제된 목록도 네이티브 클릭 관통 예외를 가져야 항목을 누를 수 있다.
            if (displayDropdown.template != null)
            {
                WindowInputRegion region = displayDropdown.template.GetComponent<WindowInputRegion>();
                if (region == null) region = displayDropdown.template.gameObject.AddComponent<WindowInputRegion>();
                region.ReceiveMouseInput = true;
            }

            displayDropdown.onValueChanged.AddListener(OnDisplaySelected);
            monitorRefresh = StartCoroutine(RefreshMonitorsWhileOpen());
        }

        protected override void OnModalClosed()
        {
            FlushOpacitySave();
            if (uiOpacitySlider != null) uiOpacitySlider.onValueChanged.RemoveListener(OnOpacityChanged);
            if (uiOpacitySlider is MouseOnlySlider mouseSlider)
                mouseSlider.PointerReleased -= OnOpacityPointerReleased;
            opacityShakePlayedForCurrentPress = false;
            // 부모 패널이 닫히는 동안에는 Exit가 완료될 수 없다. 다음에 열 때
            // 남아 있는 라벨이 잘못 Enter를 자동 재생하지 않도록 상태를 정리한다.
            uiOpacityMinimumTransition?.Stop();
            if (uiOpacityMinimumDescription != null)
                uiOpacityMinimumDescription.gameObject.SetActive(false);
            if (opacityMinimumTextSubscribed)
            {
                opacityMinimumText.StringChanged -= ApplyOpacityMinimumDescription;
                opacityMinimumTextSubscribed = false;
            }
            displayedMinimumOpacityPercent = -1;
            observedMinimumOpacity = float.NaN;
            if (uiResetButton != null) uiResetButton.onClick.RemoveListener(ResetUiPlacement);
            if (displayDropdown != null) displayDropdown.onValueChanged.RemoveListener(OnDisplaySelected);
            if (monitorRefresh != null) StopCoroutine(monitorRefresh);
            if (pendingMonitorMove != null) StopCoroutine(pendingMonitorMove);
            monitorRefresh = null;
            pendingMonitorMove = null;
        }

        protected override void RefreshContents()
        {
            RefreshOpacityControls();
            ResolveDropdown();
            RefreshMonitorChoices();
        }

        private void Update()
        {
            if (opacitySavePending && Time.unscaledTime - lastOpacityChangeTime >= OpacitySaveDelay)
                FlushOpacitySave();

            UiOpacityController controller = UiOpacityController.Instance;
            if (controller != null &&
                !Mathf.Approximately(controller.MinimumOpacity, observedMinimumOpacity))
            {
                RefreshOpacityMinimumText(controller);
                UpdateOpacityFillColor(controller);
                UpdateOpacityMinimumDescriptionVisibility(controller);
            }
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus)
            {
                opacityShakePlayedForCurrentPress = false;
                FlushOpacitySave();
            }
        }

        private void OnOpacityPointerReleased()
        {
            opacityShakePlayedForCurrentPress = false;
        }

        private void ResolveOpacityControls()
        {
            if (uiOpacitySlider == null)
            {
                Transform target = transform.Find("bg/setting/UIOpacity/controller/Opacity/Progress");
                if (target != null) uiOpacitySlider = target.GetComponent<UnityEngine.UI.Slider>();
            }
            if (uiOpacityFillImage == null && uiOpacitySlider != null && uiOpacitySlider.fillRect != null)
                uiOpacityFillImage = uiOpacitySlider.fillRect.GetComponent<UnityEngine.UI.Image>();
            if (uiOpacityMinimumDescription == null)
            {
                Transform target = transform.Find("bg/setting/UIOpacity/lb_description (1)");
                if (target != null) uiOpacityMinimumDescription = target.GetComponent<TextMeshProUGUI>();
            }
            if (uiOpacityMinimumTransition == null && uiOpacityMinimumDescription != null)
                uiOpacityMinimumTransition = uiOpacityMinimumDescription.GetComponent<UITweenTransition>();
            if (uiOpacityLimitShake == null && uiOpacitySlider != null)
                uiOpacityLimitShake = uiOpacitySlider.GetComponent<UIShakeTween>();
            if (uiOpacityMinimumDescription != null)
            {
                LocalizedTMPText staticBinding = uiOpacityMinimumDescription.GetComponent<LocalizedTMPText>();
                if (staticBinding != null)
                {
                    staticBinding.enabled = false;
                    opacityMinimumText = staticBinding.TextReference;
                }
            }
        }

        private void RefreshOpacityMinimumText(UiOpacityController controller)
        {
            if (controller == null) return;
            observedMinimumOpacity = controller.MinimumOpacity;
            if (opacityMinimumText == null || !opacityMinimumText.HasReference) return;

            int percent = Mathf.RoundToInt(observedMinimumOpacity * 100f);
            if (opacityMinimumTextSubscribed && percent == displayedMinimumOpacityPercent) return;

            opacityMinimumArguments[0] = percent;
            opacityMinimumText.Arguments = opacityMinimumArguments;
            displayedMinimumOpacityPercent = percent;
            if (!opacityMinimumTextSubscribed)
            {
                // The label begins inactive. This reference stays subscribed while the panel is
                // open, so both its first activation and locale changes use the same live argument.
                opacityMinimumText.StringChanged += ApplyOpacityMinimumDescription;
                opacityMinimumTextSubscribed = true;
            }
            else
                opacityMinimumText.RefreshString();
        }

        private void RefreshOpacityControls()
        {
            ResolveOpacityControls();
            UiOpacityController controller = UiOpacityController.Instance;
            if (uiOpacitySlider != null && controller != null)
            {
                // Keep the Slider's physical range at 0..1. Raising minValue remaps
                // normalizedValue, placing the minimum at the far-left end of the track.
                uiOpacitySlider.minValue = 0f;
                uiOpacitySlider.maxValue = 1f;
                uiOpacitySlider.SetValueWithoutNotify(controller.Opacity);
            }
            RefreshOpacityMinimumText(controller);
            UpdateOpacityFillColor(controller);
            UpdateOpacityMinimumDescriptionVisibility(controller);
        }

        private void ApplyOpacityMinimumDescription(string localizedText)
        {
            if (uiOpacityMinimumDescription != null) uiOpacityMinimumDescription.text = localizedText;
        }

        private void OnOpacityChanged(float value)
        {
            UiOpacityController controller = UiOpacityController.Instance;
            if (controller == null) return;
            bool wasAboveMinimum = controller.Opacity > controller.MinimumOpacity + 0.000001f;
            controller.SetOpacity(value);
            // SetOpacity clamps to the Inspector-configured floor. Reapply that exact
            // value without firing another change event so the thumb and fill stop at
            // the corresponding point on the full 0..1 track.
            if (uiOpacitySlider != null)
                uiOpacitySlider.SetValueWithoutNotify(controller.Opacity);
            RefreshOpacityMinimumText(controller);
            UpdateOpacityFillColor(controller);
            UpdateOpacityMinimumDescriptionVisibility(controller);
            // The shake moves bg_Bar, which is also the Slider handle's drag
            // coordinate frame. Only re-arm after moving farther above the
            // floor than a full swing between both visual extremes can perturb
            // the reported value.
            if (controller.Opacity > controller.MinimumOpacity + OpacityShakeRearmDistance())
                opacityShakePlayedForCurrentPress = false;
            if (!opacityShakePlayedForCurrentPress && wasAboveMinimum &&
                Mathf.Approximately(controller.Opacity, controller.MinimumOpacity))
            {
                opacityShakePlayedForCurrentPress = true;
                uiOpacityLimitShake?.PlayShake();
            }
            opacitySavePending = true;
            lastOpacityChangeTime = Time.unscaledTime;
        }

        private float OpacityShakeRearmDistance()
        {
            if (uiOpacitySlider == null || uiOpacityLimitShake == null) return 0f;
            RectTransform handleTrack = uiOpacitySlider.handleRect != null
                ? uiOpacitySlider.handleRect.parent as RectTransform : null;
            float width = handleTrack != null ? handleTrack.rect.width : 0f;
            if (width <= 0f) return 0f;
            float sliderRange = uiOpacitySlider.maxValue - uiOpacitySlider.minValue;
            return 2f * uiOpacityLimitShake.MaximumHorizontalDisplacement / width * sliderRange + 0.000001f;
        }

        private void UpdateOpacityMinimumDescriptionVisibility(UiOpacityController controller)
        {
            if (uiOpacityMinimumDescription == null) return;
            bool atMinimum = controller != null &&
                             Mathf.Approximately(controller.Opacity, controller.MinimumOpacity);
            GameObject description = uiOpacityMinimumDescription.gameObject;
            if (atMinimum)
            {
                if (!description.activeSelf)
                    description.SetActive(true); // 프리팹의 OnEnable이 Enter를 자동 재생한다.
                else if (uiOpacityMinimumTransition != null && uiOpacityMinimumTransition.IsExiting)
                    uiOpacityMinimumTransition.PlayEnter(); // 진행 중인 Exit와 완료 콜백을 취소한다.
                return;
            }

            if (!description.activeSelf) return;
            if (uiOpacityMinimumTransition == null)
            {
                description.SetActive(false);
                return;
            }

            if (uiOpacityMinimumTransition.IsExiting) return;
            uiOpacityMinimumTransition.PlayExit(() =>
            {
                if (this == null || description == null) return;
                UiOpacityController current = UiOpacityController.Instance;
                if (current != null && Mathf.Approximately(current.Opacity, current.MinimumOpacity)) return;
                description.SetActive(false);
            });
        }

        private void UpdateOpacityFillColor(UiOpacityController controller)
        {
            if (uiOpacityFillImage == null) return;
            bool atMinimum = controller != null &&
                             Mathf.Approximately(controller.Opacity, controller.MinimumOpacity);
            uiOpacityFillImage.color = atMinimum ? uiOpacityMinimumFillColor : uiOpacityFillColor;
        }

        private void FlushOpacitySave()
        {
            if (!opacitySavePending) return;
            UiOpacityController controller = UiOpacityController.Instance;
            if (controller != null) controller.SaveOpacity();
            opacitySavePending = false;
        }

        private void ResolveDropdown()
        {
            if (displayDropdown != null) return;
            Transform target = transform.Find("bg/setting/Display/selection/Dropdown");
            if (target != null) displayDropdown = target.GetComponent<TMP_Dropdown>();
        }

        private void ResolveResetButton()
        {
            if (uiResetButton != null) return;
            Transform target = transform.Find("bg/setting/UIReset/button/btn_Reset");
            if (target != null) uiResetButton = target.GetComponent<UnityEngine.UI.Button>();
            if (uiResetButton == null)
                Debug.LogWarning("[SettingsPanel] UI 위치 초기화 버튼을 찾지 못했습니다.", this);
        }

        private void ResetUiPlacement()
        {
            TransparentWindowController window = TransparentWindowController.Instance;
            bool groupsReset = window != null && window.ResetUiPlacementToDefaults();

            PanelDockManager dockManager = GetComponentInParent<PanelDockManager>();
            if (dockManager != null) dockManager.ResetAllPanelPositions();
            else
            {
                // 패널이 도킹 관리자 없이 배치된 씬에서도 현재 이동 위치는 복구한다.
                RectTransform parent = transform.parent as RectTransform;
                if (parent != null)
                    foreach (PanelDragHandle handle in parent.GetComponentsInChildren<PanelDragHandle>(true))
                        handle.ResetToDefaultPosition();
            }

            if (!groupsReset)
                Debug.LogWarning("[SettingsPanel] Stage/HUD 배치 초기화를 완료하지 못했습니다. 패널 위치만 복구했습니다.", this);
        }

        private IEnumerator RefreshMonitorsWhileOpen()
        {
            while (true)
            {
                RefreshMonitorChoices();
                yield return new WaitForSecondsRealtime(0.5f);
            }
        }

        private void RefreshMonitorChoices()
        {
            if (displayDropdown == null) return;
            // 열린 목록은 별도 복제 UI다. 연결 상태가 바뀌어도 항목 인덱스의
            // 장치 매핑을 목록이 닫힐 때까지 그대로 유지한다.
            if (displayDropdown.IsExpanded) return;

            TransparentWindowController controller = TransparentWindowController.Instance;
            if (controller == null || !controller.MonitorSelectionReady)
            {
                displayedMonitors.Clear();
                displayDropdown.ClearOptions();
                displayDropdown.AddOptions(new List<string> { "Windows 실행 파일에서 사용 가능" });
                displayDropdown.SetValueWithoutNotify(0);
                displayDropdown.RefreshShownValue();
                displayDropdown.interactable = false;
                return;
            }

            List<TransparentWindowController.MonitorChoice> connected = controller.GetConnectedMonitors();
            bool changed = connected.Count != displayedMonitors.Count;
            if (!changed)
            {
                for (int i = 0; i < connected.Count; i++)
                {
                    if (!string.Equals(connected[i].DeviceName, displayedMonitors[i].DeviceName, StringComparison.OrdinalIgnoreCase) ||
                        connected[i].DisplayName != displayedMonitors[i].DisplayName)
                    {
                        changed = true;
                        break;
                    }
                }
            }

            if (changed)
            {
                displayedMonitors.Clear();
                displayedMonitors.AddRange(connected);
                displayDropdown.ClearOptions();
                var labels = new List<string>(connected.Count);
                foreach (var monitor in connected) labels.Add(monitor.DisplayName);
                if (labels.Count == 0) labels.Add("디스플레이 없음");
                displayDropdown.AddOptions(labels);
            }

            int currentIndex = connected.FindIndex(m =>
                string.Equals(m.DeviceName, controller.CurrentMonitorDeviceName, StringComparison.OrdinalIgnoreCase));
            displayDropdown.interactable = connected.Count > 0 && currentIndex >= 0;
            if (currentIndex >= 0 && (changed || displayDropdown.value != currentIndex))
            {
                displayDropdown.SetValueWithoutNotify(currentIndex);
                displayDropdown.RefreshShownValue();
            }
        }

        private void OnDisplaySelected(int index)
        {
            if (index < 0 || index >= displayedMonitors.Count) return;
            string deviceName = displayedMonitors[index].DeviceName;
            if (pendingMonitorMove != null) StopCoroutine(pendingMonitorMove);
            pendingMonitorMove = StartCoroutine(MoveAfterDropdownCloses(deviceName));
        }

        private IEnumerator MoveAfterDropdownCloses(string deviceName)
        {
            // TMP_Dropdown이 포인터 이벤트와 목록 제거를 마치기 전에 OS 창을 옮기지 않는다.
            yield return null;
            TransparentWindowController controller = TransparentWindowController.Instance;
            if (controller == null || !controller.TryMoveOverlayToMonitor(deviceName))
                Debug.LogWarning("[SettingsPanel] 선택한 디스플레이를 더 이상 사용할 수 없습니다.");
            RefreshMonitorChoices();
            pendingMonitorMove = null;
        }
    }
}
