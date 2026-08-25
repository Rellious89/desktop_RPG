using System;
using System.Collections.Generic;
using Character;
using CharacterArchive;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Corruption
{
    /// <summary>기도 슬롯의 표시와 명부 카드 드롭만 담당한다. 데이터 변경은 부모 패널 서비스로 위임한다.</summary>
    [DisallowMultipleComponent]
    public sealed class PurificationSlotView : MonoBehaviour, IDropHandler
    {
        private const int CellCount = 10;
        [SerializeField] private int slotIndex;
        private PurificationPanel panel;
        private GameObject enabledItem, disabledItem;
        private Image portrait;
        private TMP_Text nameText, percentText, timerText;
        private Button stopButton;
        private readonly List<CellImage> currentCells = new List<CellImage>(CellCount);
        private readonly List<CellImage> baseCells = new List<CellImage>(CellCount);
        private int blinkingCell = -1;
        private float blinkElapsed;
        private bool fastBlink;

        private readonly struct CellImage
        {
            public readonly Image Image;
            public readonly Color Color;
            public CellImage(Image image) { Image = image; Color = image != null ? image.color : Color.white; }
        }

        public int SlotIndex => slotIndex;

        private void Awake()
        {
            enabledItem = Find("item_Party_enable"); disabledItem = Find("item_Party_disable");
            portrait = FindComponent<Image>("portrait"); nameText = FindComponent<TMP_Text>("lb_CharacterName");
            percentText = FindComponent<TMP_Text>("lb_percent"); timerText = FindComponent<TMP_Text>("lb_RemainingTime");
            stopButton = FindComponent<Button>("btn_archive");
            BuildCells();
            ResetProgressVisuals();
            if (stopButton != null) stopButton.onClick.AddListener(Stop);
        }

        private void Update()
        {
            if (blinkingCell < 0 || blinkingCell >= currentCells.Count) return;
            blinkElapsed += Time.unscaledDeltaTime;
            float frequency = fastBlink ? 5f : 2f;
            SetAlpha(currentCells[blinkingCell], 0.25f + 0.75f * (0.5f + 0.5f * Mathf.Sin(blinkElapsed * frequency * Mathf.PI * 2f)));
        }

        private void OnDestroy() { if (stopButton != null) stopButton.onClick.RemoveListener(Stop); }
        internal void Bind(PurificationPanel value, int index) { panel = value; slotIndex = index; }

        internal void Refresh(CharacterDefinition definition, double corruption, double baseCorruption, TimeSpan remaining)
        {
            bool occupied = definition != null;
            SetActive(enabledItem, occupied); SetActive(disabledItem, !occupied);
            if (!occupied)
            {
                if (percentText != null) percentText.text = string.Empty;
                if (timerText != null) timerText.text = string.Empty;
                ResetProgressVisuals();
                return;
            }
            if (portrait != null) portrait.sprite = definition.Portrait;
            if (nameText != null) nameText.text = CharacterNameBinding.GetCurrent(definition);
            float percent = Mathf.Clamp((float)(corruption / 300d * 100d), 0f, 100f);
            if (percentText != null) percentText.text = string.Format("{0:0.#}%", percent);
            if (timerText != null) timerText.text = PurificationPanel.FormatRemaining(remaining);
            RefreshProgressVisuals(percent, baseCorruption);
        }

        internal void ResetProgressVisuals()
        {
            blinkingCell = -1;
            blinkElapsed = 0f;
            fastBlink = false;
            for (int i = 0; i < currentCells.Count; i++) SetAlpha(currentCells[i], 0f);
            for (int i = 0; i < baseCells.Count; i++) SetAlpha(baseCells[i], 0f);
        }

        public void OnDrop(PointerEventData eventData)
        {
            CharacterArchiveCardView card = eventData != null && eventData.pointerDrag != null ? eventData.pointerDrag.GetComponent<CharacterArchiveCardView>() : null;
            if (card == null || !card.IsDraggingToParty || card.Definition == null) return;
            panel?.Register(slotIndex, card.Definition);
        }

        private void RefreshProgressVisuals(float percent, double baseCorruption)
        {
            blinkingCell = -1;
            blinkElapsed = 0f;
            fastBlink = false;
            int full = Mathf.Clamp(Mathf.FloorToInt(percent / 10f), 0, CellCount);
            float remainder = percent - full * 10f;
            for (int i = 0; i < currentCells.Count; i++) SetAlpha(currentCells[i], i < full ? 1f : 0f);
            if (full < currentCells.Count && remainder >= 5f)
            {
                blinkingCell = full;
                SetAlpha(currentCells[full], 0.7f);
                fastBlink = remainder >= 9f;
            }
            int fixedCount = Mathf.Clamp(Mathf.CeilToInt((float)(baseCorruption / 300d * CellCount)), 0, CellCount);
            for (int i = 0; i < baseCells.Count; i++) SetAlpha(baseCells[i], i < fixedCount ? 1f : 0f);
        }

        private void BuildCells()
        {
            currentCells.Clear(); baseCells.Clear();
            Transform root = FindDeep(transform, "fill_cell");
            if (root == null) return;
            Image[] images = root.GetComponentsInChildren<Image>(true);
            for (int i = 0; i < images.Length; i++)
            {
                Image image = images[i];
                if (image == null) continue;
                if (image.gameObject.name.StartsWith("cell_fill_", StringComparison.Ordinal)) AddCell(currentCells, image);
                else if (image.gameObject.name.StartsWith("cell_purification_", StringComparison.Ordinal)) AddCell(baseCells, image);
            }
        }

        private static void AddCell(List<CellImage> cells, Image image)
        {
            if (cells.Count >= CellCount) return;
            // 프리팹은 원본 fill을 꺼 둔 상태다. 여기서 한 번만 켠 뒤 이후에는 alpha만 바꾼다.
            if (!image.gameObject.activeSelf) image.gameObject.SetActive(true);
            cells.Add(new CellImage(image));
        }
        private static void SetAlpha(CellImage cell, float alpha)
        {
            if (cell.Image == null) return;
            Color color = cell.Color; color.a *= Mathf.Clamp01(alpha); cell.Image.color = color;
        }
        private void Stop() { panel?.Stop(slotIndex); }
        private GameObject Find(string child) { Transform value = FindDeep(transform, child); return value != null ? value.gameObject : null; }
        private T FindComponent<T>(string child) where T : Component { Transform value = FindDeep(transform, child); return value != null ? value.GetComponent<T>() : null; }
        private static Transform FindDeep(Transform root, string name) { for (int i = 0; i < root.childCount; i++) { Transform child = root.GetChild(i); if (child.name == name) return child; Transform found = FindDeep(child, name); if (found != null) return found; } return null; }
        private static void SetActive(GameObject value, bool active) { if (value != null && value.activeSelf != active) value.SetActive(active); }
    }
}
