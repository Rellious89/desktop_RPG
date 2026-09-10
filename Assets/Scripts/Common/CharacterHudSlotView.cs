using System;
using System.Collections.Generic;
using Character;
using UnityEngine;
using UnityEngine.UI;

namespace Common
{
    /// <summary>
    /// 전투 HUD의 캐릭터 슬롯 하나를 그린다. 값의 소유자는 CharacterRoster이며 이 클래스는
    /// 초상화, 행동력 막대, 오염도 10칸과 클릭만 표시한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CharacterHudSlotView : MonoBehaviour
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
            if (slotButton != null)
            {
                slotButton.onClick.RemoveListener(HandleClicked);
                slotButton.onClick.AddListener(HandleClicked);
            }
        }

        private void OnDisable()
        {
            if (slotButton != null) slotButton.onClick.RemoveListener(HandleClicked);
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
        }

        /// <summary>PurificationSlotView와 동일한 10% 단위 규칙을 HUD에도 적용한다.</summary>
        public void RefreshCorruption(double currentCorruption, int maxCorruption)
        {
            BuildCells();
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
            if (character != null) selected?.Invoke(character);
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
            fillCells.Sort((left, right) => string.CompareOrdinal(left.name, right.name));
            for (int i = 0; i < fillCells.Count && cells.Count < CorruptionCellCount; i++)
            {
                Image image = fillCells[i];
                if (!image.gameObject.activeSelf) image.gameObject.SetActive(true);
                cells.Add(new CellImage(image));
            }
        }

        private void SetCellAlpha(int index, float alpha)
        {
            if (index < 0 || index >= cells.Count || cells[index].Image == null) return;
            Color color = cells[index].OriginalColor;
            color.a *= Mathf.Clamp01(alpha);
            cells[index].Image.color = color;
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
