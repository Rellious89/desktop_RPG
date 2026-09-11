using System;
using System.Collections.Generic;
using Character;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Common
{
    /// <summary>
    /// 전투 HUD의 캐릭터 슬롯 하나를 그린다. 값의 소유자는 CharacterRoster이며 이 클래스는
    /// 초상화, 행동력 막대, 오염도 10칸과 클릭만 표시한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CharacterHudSlotView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public const int CorruptionCellCount = 10;

        [Serializable]
        public readonly struct CorruptionCellState
        {
            public CorruptionCellState(int fullCellCount, int blinkingCellIndex, bool fastBlink)
            {
                FullCellCount = fullCellCount;
                BlinkingCellIndex = blinkingCellIndex;
                FastBlink = fastBlink;
            }

            public int FullCellCount { get; }
            public int BlinkingCellIndex { get; }
            public bool FastBlink { get; }
        }

        [Header("References")]
        [SerializeField] private Button slotButton;
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private Image portraitImage;
        [SerializeField] private ProgressBarView staminaProgress;
        [SerializeField] private Transform purificationCellsRoot;

        private readonly List<CellImage> cells = new List<CellImage>(CorruptionCellCount);
        private CharacterDefinition character;
        private Action<CharacterDefinition> selected;
        private int blinkingCell = -1;
        private bool fastBlink;
        private float blinkElapsed;
        private bool cellsBuilt;
        private bool cellsSortedForVisualOrder;
        private CharacterHudTooltipController tooltipController;

        private readonly struct CellImage
        {
            public CellImage(Image image)
            {
                Image = image;
                OriginalColor = image != null ? image.color : Color.white;
            }

            public Image Image { get; }
            public Color OriginalColor { get; }
        }

        public CharacterDefinition BoundCharacter => character;

        private void Awake()
        {
            ResolveReferences();
            BuildCells();
        }

        private void OnEnable()
        {
            ResolveReferences();
            if (tooltipController == null) tooltipController = CharacterHudTooltipController.FindSharedController(this);
            if (slotButton != null)
            {
                slotButton.onClick.RemoveListener(HandleClicked);
                slotButton.onClick.AddListener(HandleClicked);
            }
        }

        private void OnDisable()
        {
            if (slotButton != null) slotButton.onClick.RemoveListener(HandleClicked);
            tooltipController?.CancelShow(this);
        }

        private void Update()
        {
            if (blinkingCell < 0 || blinkingCell >= cells.Count) return;

            blinkElapsed += Time.unscaledDeltaTime;
            float frequency = fastBlink ? 5f : 2f;
            float alpha = 0.25f + 0.75f * (0.5f + 0.5f * Mathf.Sin(blinkElapsed * frequency * Mathf.PI * 2f));
            SetCellAlpha(blinkingCell, alpha);
        }

        public void Bind(CharacterDefinition definition, Action<CharacterDefinition> onSelected)
        {
            ResolveReferences();
            BuildCells();

            // 같은 슬롯 인스턴스가 다른 캐릭터에 재사용될 수 있다. 그 경우 기존 캐릭터의 툴팁을
            // 새 데이터로 바꿔 보여 주지 않고 즉시 거둔다. 새 캐릭터의 표시는 다음 Hover 진입이
            // 명시적으로 소유한다.
            tooltipController?.CancelShow(this);

            character = definition;
            selected = onSelected;
            if (portraitImage != null)
            {
                portraitImage.sprite = definition != null ? definition.Portrait : null;
                portraitImage.enabled = portraitImage.sprite != null;
            }
        }

        public void Refresh(int currentStamina, int maxStamina, double currentCorruption, int maxCorruption,
                            bool isCurrent)
        {
            if (canvasGroup != null) canvasGroup.alpha = isCurrent ? 1f : 0.2f;
            if (staminaProgress != null) staminaProgress.SetValue(currentStamina, maxStamina);
            RefreshCorruption(currentCorruption, maxCorruption);
            tooltipController?.RefreshVisible(this);
        }

        /// <summary>PurificationSlotView와 동일한 10% 단위 규칙을 HUD에도 적용한다.</summary>
        public void RefreshCorruption(double currentCorruption, int maxCorruption)
        {
            BuildCells();
            // 이 프리팹은 정화 UI를 회전/반전해 재사용한다. Awake 시점에는 상위 HUD 레이아웃이
            // 확정되지 않아 좌표 정렬이 원본 계층 순서로 굳을 수 있으므로, 실제 표시 직전에
            // 레이아웃을 확정하고 화면 x 좌표로 다시 정렬한다.
            SortCellsByVisualX();
            CorruptionCellState state = CalculateCorruptionCellState(currentCorruption, maxCorruption);
            bool preserveBlinkPhase = state.BlinkingCellIndex >= 0 && state.BlinkingCellIndex == blinkingCell &&
                                      state.FastBlink == fastBlink;

            for (int i = 0; i < cells.Count; i++)
            {
                if (preserveBlinkPhase && i == state.BlinkingCellIndex) continue;
                SetCellAlpha(i, i < state.FullCellCount ? 1f : 0f);
            }

            if (!preserveBlinkPhase)
            {
                blinkElapsed = 0f;
                if (state.BlinkingCellIndex >= 0) SetCellAlpha(state.BlinkingCellIndex, 0.7f);
            }

            blinkingCell = state.BlinkingCellIndex;
            fastBlink = state.FastBlink;
        }

        /// <summary>현재/최대 오염도를 기존 정화 UI와 같은 10칸 표시 상태로 변환한다.</summary>
        public static CorruptionCellState CalculateCorruptionCellState(double currentCorruption, int maxCorruption)
        {
            if (maxCorruption <= 0 || double.IsNaN(currentCorruption) || double.IsInfinity(currentCorruption))
            {
                return new CorruptionCellState(0, -1, false);
            }

            float percent = Mathf.Clamp((float)(currentCorruption / maxCorruption * 100d), 0f, 100f);
            int full = Mathf.Clamp(Mathf.FloorToInt(percent / 10f), 0, CorruptionCellCount);
            float remainder = percent - full * 10f;
            int blinking = full < CorruptionCellCount && remainder >= 5f ? full : -1;
            return new CorruptionCellState(full, blinking, blinking >= 0 && remainder >= 9f);
        }

        private void HandleClicked()
        {
            // 교체 확인창을 열기 전 이 슬롯의 Hover 툴팁을 먼저 거둔다. 남겨 두면 작은 HUD 위에
            // 툴팁과 확인창이 겹치고, 클릭이 툴팁을 위한 것처럼 보인다.
            tooltipController?.CancelShow(this);
            if (character != null) selected?.Invoke(character);
        }

        /// <summary>슬롯의 초상화·두 게이지를 포함한 전체 버튼 영역에 같은 캐릭터 상세 툴팁을 띄운다.</summary>
        public void OnPointerEnter(PointerEventData eventData)
        {
            if (tooltipController == null) tooltipController = CharacterHudTooltipController.FindSharedController(this);
            tooltipController?.RequestShow(this);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            tooltipController?.CancelShow(this);
        }

        private void ResolveReferences()
        {
            if (slotButton == null) slotButton = GetComponent<Button>();
            if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
            if (portraitImage == null) portraitImage = FindDeepChild(transform, "sp_portrait")?.GetComponent<Image>();
            if (staminaProgress == null) staminaProgress = FindDeepChild(transform, "Progress_Stamina")?.GetComponent<ProgressBarView>();
            if (purificationCellsRoot == null) purificationCellsRoot = FindDeepChild(transform, "fill_cell");
        }

        private void BuildCells()
        {
            if (cellsBuilt) return;
            cellsBuilt = true;
            cells.Clear();
            if (purificationCellsRoot == null) return;

            Image[] images = purificationCellsRoot.GetComponentsInChildren<Image>(true);
            var fillCells = new List<Image>(CorruptionCellCount);
            for (int i = 0; i < images.Length; i++)
            {
                Image image = images[i];
                if (image != null && image.name.StartsWith("cell_fill_", StringComparison.Ordinal)) fillCells.Add(image);
            }
            for (int i = 0; i < fillCells.Count && cells.Count < CorruptionCellCount; i++)
            {
                Image image = fillCells[i];
                if (!image.gameObject.activeSelf) image.gameObject.SetActive(true);
                cells.Add(new CellImage(image));
            }
        }

        private void SortCellsByVisualX()
        {
            if (cellsSortedForVisualOrder || cells.Count < 2) return;
            if (purificationCellsRoot is RectTransform cellsRect)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(cellsRect);
            }

            // HUD의 오염도 막대는 원본 세로 정화 UI를 y=180/z=90으로 회전해 쓴다. 실제 x를
            // 우선하되, 최초 Bind 프레임처럼 Layout이 아직 같은 좌표를 돌려줄 때는 VerticalLayout의
            // local-down 진행 방향에서 결정한 sibling 순서를 fallback으로 쓴다. 현재 회전에서는
            // sibling 증가가 화면 왼쪽 방향이므로 index를 역순으로 쓴다.
            float layoutDownX = purificationCellsRoot != null
                ? purificationCellsRoot.TransformVector(Vector3.down).x
                : 0f;
            cells.Sort((left, right) => CompareVisualCellOrder(left, right, layoutDownX));
            cellsSortedForVisualOrder = true;
        }

        private int CompareVisualCellOrder(CellImage left, CellImage right, float layoutDownX)
        {
            float difference = GetVisualX(left.Image) - GetVisualX(right.Image);
            if (Mathf.Abs(difference) > 0.001f) return difference < 0f ? -1 : 1;

            int leftSibling = GetCellSiblingIndex(left.Image);
            int rightSibling = GetCellSiblingIndex(right.Image);
            // local down이 화면 왼쪽이면 나중 sibling이 더 왼쪽이다.
            return layoutDownX < -0.001f
                ? rightSibling.CompareTo(leftSibling)
                : leftSibling.CompareTo(rightSibling);
        }

        private int GetCellSiblingIndex(Image image)
        {
            if (image == null) return int.MaxValue;
            Transform cell = image.transform;
            while (cell.parent != null && cell.parent != purificationCellsRoot) cell = cell.parent;
            return cell.parent == purificationCellsRoot ? cell.GetSiblingIndex() : int.MaxValue;
        }

        private void SetCellAlpha(int index, float alpha)
        {
            if (index < 0 || index >= cells.Count || cells[index].Image == null) return;
            Color color = cells[index].OriginalColor;
            color.a *= Mathf.Clamp01(alpha);
            cells[index].Image.color = color;
        }

        private static float GetVisualX(Image image)
        {
            if (image == null) return float.PositiveInfinity;

            Vector3[] corners = new Vector3[4];
            image.rectTransform.GetWorldCorners(corners);
            return (corners[0].x + corners[1].x + corners[2].x + corners[3].x) * 0.25f;
        }

        private static Transform FindDeepChild(Transform root, string childName)
        {
            if (root == null) return null;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (child.name == childName) return child;
                Transform found = FindDeepChild(child, childName);
                if (found != null) return found;
            }
            return null;
        }
    }
}
