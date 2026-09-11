using Character;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Common
{
    /// <summary>
    /// HUD 슬롯에서 여는 전용 확인창. 현재/선택 캐릭터의 이름을 Locale 변경에도 갱신하고,
    /// 실제 교체는 confirm 시점의 CharacterRoster.TrySwitchTo 재검증으로만 수행한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CharacterSwapConfirmationDialog : ModalPanel
    {
        [SerializeField] private Button confirmButton;
        [SerializeField] private TextMeshProUGUI currentCharacterText;
        [SerializeField] private TextMeshProUGUI selectedCharacterText;
        [Tooltip("dialog_CharacterSwap 전체를 덮는 루트가 아니라 실제 확인창(bg)을 연결한다.")]
        [SerializeField] private RectTransform dialogContent;
        [Min(0f)]
        [SerializeField] private float sourceVerticalGap = 8f;

        private readonly CharacterNameBinding currentName = new CharacterNameBinding();
        private readonly CharacterNameBinding selectedName = new CharacterNameBinding();
        private CharacterDefinition selectedCharacter;
        private RectTransform selectedSource;
        private int openedOrRetargetedFrame = -1;

        protected override string CloseButtonName => "btn_cancle";

        public void OpenFor(CharacterDefinition selected)
        {
            OpenFor(selected, null);
        }

        /// <summary>선택 HUD 바로 위에 같은 확인창을 재사용해 연다.</summary>
        public void OpenFor(CharacterDefinition selected, RectTransform source)
        {
            selectedCharacter = selected;
            selectedSource = source;
            // ItemUseTargetDialog와 같은 원칙: 이미 열려 있는 창을 다른 HUD 슬롯으로 바꾸는
            // 클릭은 외부 클릭으로 다시 해석하지 않는다. Update와 UI 이벤트의 실행 순서가 달라도
            // 한 번의 클릭으로 새 대상을 유지한다.
            openedOrRetargetedFrame = Time.frameCount;
            Open();
            PositionAboveSelectedSource();
        }

        protected override void OnModalOpened()
        {
            if (confirmButton != null)
            {
                confirmButton.onClick.RemoveListener(Confirm);
                confirmButton.onClick.AddListener(Confirm);
            }
        }

        protected override void OnModalClosed()
        {
            if (confirmButton != null) confirmButton.onClick.RemoveListener(Confirm);
            currentName.Unbind();
            selectedName.Unbind();
            selectedCharacter = null;
            selectedSource = null;
        }

        protected override void RefreshContents()
        {
            CharacterRoster roster = CharacterRoster.Instance;
            CharacterDefinition current = roster != null ? roster.Current : null;
            currentName.Bind(current, ApplyCurrentName);
            selectedName.Bind(selectedCharacter, ApplySelectedName);

            if (confirmButton != null)
            {
                confirmButton.interactable = roster != null && selectedCharacter != null &&
                                             roster.GetSwapBlockReason(selectedCharacter) == CharacterRoster.SwapBlockReason.None;
            }
        }

        private void Confirm()
        {
            CharacterRoster roster = CharacterRoster.Instance;
            if (roster == null || selectedCharacter == null) { RefreshContents(); return; }

            // 열려 있는 동안 행동력/회복 상태가 달라질 수 있으므로 반드시 다시 권한을 확인한다.
            if (!roster.TrySwitchTo(selectedCharacter, out CharacterRoster.SwapBlockReason reason))
            {
                if (reason == CharacterRoster.SwapBlockReason.NoStamina)
                {
                    Close();
                    CharacterHudController.ShowNoStaminaToast();
                    return;
                }

                Debug.LogWarning($"[CharacterSwapConfirmationDialog] 캐릭터 교체가 취소됐습니다(사유: {reason}).", this);
                RefreshContents();
                return;
            }

            Close();
        }

        private void Update()
        {
            if (Time.frameCount == openedOrRetargetedFrame) return;
            if (!Input.GetMouseButtonDown(0) && !Input.GetMouseButtonDown(1)) return;
            if (IsPointerInsideContent(Input.mousePosition)) return;

            // 배경은 raycast를 가로채지 않는다. 따라서 이 Close와 같은 클릭으로 HUD의 다른 슬롯
            // 또는 ControlDock의 다른 버튼도 원래 동작을 즉시 실행한다.
            Close();
        }

        /// <summary>
        /// HUD 열의 윗변을 기준으로 popup 위치를 만든다. y는 선택 슬롯과 무관하게 고정하고,
        /// x만 슬롯 중심을 따라가되 Canvas 범위 안으로 보정한다.
        /// </summary>
        public static Vector2 CalculateHorizontallyClampedPopupPosition(
            Vector2 hudRowTopCenter,
            Vector2 popupSize,
            Vector2 popupPivot,
            Rect parentRect,
            float verticalGap)
        {
            // popup bottom이 HUD 열 위로 gap만큼 떨어지게 pivot 좌표로 환산한다.
            float x = hudRowTopCenter.x + popupSize.x * (popupPivot.x - 0.5f);
            float y = hudRowTopCenter.y + Mathf.Max(0f, verticalGap) + popupSize.y * popupPivot.y;
            float minX = parentRect.xMin + popupSize.x * popupPivot.x;
            float maxX = parentRect.xMax - popupSize.x * (1f - popupPivot.x);
            return new Vector2(Mathf.Clamp(x, minX, maxX), y);
        }

        private void PositionAboveSelectedSource()
        {
            if (dialogContent == null || selectedSource == null) return;

            RectTransform parent = dialogContent.parent as RectTransform;
            if (parent == null) return;

            // item_CharacterHUD 루트는 ContentSizeFitter 적용 전 sizeDelta가 0일 수 있고, 실제
            // portrait/bar가 루트 밖으로 뻗는다. 따라서 RectTransform 루트가 아니라 현재 켜진
            // Graphic들의 실제 bounds를 사용한다. x는 선택 슬롯의 시각 중심, y는 HUD 전체의
            // 시각 상단으로 고정해 어느 슬롯을 선택해도 다른 HUD를 가리지 않는다.
            RectTransform hudRow = selectedSource.parent as RectTransform ?? selectedSource;
            GetVisualBounds(selectedSource, out float selectedMinX, out float selectedMaxX, out _, out _);
            GetVisualBounds(hudRow, out _, out _, out _, out float hudTop);
            Vector3 rowTopCenter = new Vector3((selectedMinX + selectedMaxX) * 0.5f, hudTop, 0f);
            Vector2 localTopCenter = parent.InverseTransformPoint(rowTopCenter);

            dialogContent.anchoredPosition = CalculateHorizontallyClampedPopupPosition(
                localTopCenter, dialogContent.rect.size, dialogContent.pivot, parent.rect, sourceVerticalGap);
        }

        private bool IsPointerInsideContent(Vector2 screenPosition)
        {
            RectTransform target = dialogContent != null ? dialogContent : transform as RectTransform;
            if (target == null) return false;

            Canvas canvas = target.GetComponentInParent<Canvas>();
            Camera eventCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;
            return RectTransformUtility.RectangleContainsScreenPoint(target, screenPosition, eventCamera);
        }

        private static void GetVisualBounds(
            Transform root, out float minX, out float maxX, out float minY, out float maxY)
        {
            minX = minY = float.PositiveInfinity;
            maxX = maxY = float.NegativeInfinity;
            bool foundGraphic = false;
            Graphic[] graphics = root != null ? root.GetComponentsInChildren<Graphic>(false) : null;
            Vector3[] corners = new Vector3[4];
            if (graphics != null)
            {
                for (int i = 0; i < graphics.Length; i++)
                {
                    Graphic graphic = graphics[i];
                    if (graphic == null || !graphic.enabled || !graphic.gameObject.activeInHierarchy) continue;
                    graphic.rectTransform.GetWorldCorners(corners);
                    for (int corner = 0; corner < corners.Length; corner++)
                    {
                        minX = Mathf.Min(minX, corners[corner].x);
                        maxX = Mathf.Max(maxX, corners[corner].x);
                        minY = Mathf.Min(minY, corners[corner].y);
                        maxY = Mathf.Max(maxY, corners[corner].y);
                    }
                    foundGraphic = true;
                }
            }

            if (foundGraphic) return;

            RectTransform fallback = root as RectTransform;
            if (fallback != null)
            {
                fallback.GetWorldCorners(corners);
                minX = maxX = corners[0].x;
                minY = maxY = corners[0].y;
                for (int i = 1; i < corners.Length; i++)
                {
                    minX = Mathf.Min(minX, corners[i].x);
                    maxX = Mathf.Max(maxX, corners[i].x);
                    minY = Mathf.Min(minY, corners[i].y);
                    maxY = Mathf.Max(maxY, corners[i].y);
                }
                return;
            }

            minX = maxX = minY = maxY = 0f;
        }

        private void ApplyCurrentName(string value)
        {
            if (currentCharacterText != null) currentCharacterText.text = value ?? string.Empty;
        }

        private void ApplySelectedName(string value)
        {
            if (selectedCharacterText != null) selectedCharacterText.text = value ?? string.Empty;
        }
    }
}
