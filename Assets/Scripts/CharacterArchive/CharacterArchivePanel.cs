using System;
using System.Collections.Generic;
using Character;
using Common;
using Recovery;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CharacterArchive
{
    /// <summary>보유 여부를 바꾸지 않는 캐릭터 명부의 조회/선택 화면.</summary>
    [DisallowMultipleComponent]
    public sealed class CharacterArchivePanel : ModalPanel
    {
        [Header("References (Inspector에서만 연결)")]
        [SerializeField] private CharacterCatalog catalog;
        [SerializeField] private RectTransform content;
        [SerializeField] private CharacterArchiveCardView cardTemplate;
        [SerializeField] private CharacterArchiveCardView detailCard;
        [SerializeField] private GameObject rightPanel;
        [SerializeField] private Button rightCloseButton;
        [SerializeField] private Button expandRightButton;
        [SerializeField] private Button allBookmarkButton;
        [SerializeField] private Button ownedBookmarkButton;
        [SerializeField] private GameObject allBookmarkEnabled;
        [SerializeField] private GameObject allBookmarkDisabled;
        [SerializeField] private GameObject ownedBookmarkEnabled;
        [SerializeField] private GameObject ownedBookmarkDisabled;
        [SerializeField] private TMP_Text ownedCountText;
        [SerializeField] private LocalizedTMPText ownedCountLocalizer;

        private readonly List<CharacterArchiveCardView> cards = new List<CharacterArchiveCardView>();
        private bool ownedOnly;
        private CharacterDefinition selected;
        private LocalizedTextReference boundCountFormat;
        private static CharacterArchivePanel openInstance;

        public static void RequestRefresh()
        {
            if (openInstance != null) openInstance.RefreshContents();
        }

        protected override void OnModalOpened()
        {
            ValidateReferences();
            BindButtons();
            BindCountFormat();
            CharacterRoster.CurrentCharacterChanged += HandleRosterChanged;
            CharacterRoster.CharacterStateChanged += HandleRosterChanged;
            RecoveryService.SlotsChanged += HandleRecoverySlotsChanged;
            openInstance = this;
        }

        protected override void OnModalClosed()
        {
            CharacterRoster.CurrentCharacterChanged -= HandleRosterChanged;
            CharacterRoster.CharacterStateChanged -= HandleRosterChanged;
            RecoveryService.SlotsChanged -= HandleRecoverySlotsChanged;
            UnbindButtons();
            UnbindCountFormat();
            if (openInstance == this) openInstance = null;
        }

        protected override void RefreshContents()
        {
            if (!ValidateReferences()) return;
            SaveData data = SaveSystem.Data;
            var owned = new OwnedCharacterCollection(catalog, data);
            IReadOnlyList<CharacterDefinition> source = ownedOnly ? owned.OwnedCharacters : owned.AllCharacters;
            if (selected != null && !Contains(source, selected.CharacterId)) selected = null;

            EnsureCardCount(source.Count);
            CharacterDefinition current = CharacterRoster.Instance != null ? CharacterRoster.Instance.Current : null;
            for (int i = 0; i < source.Count; i++)
            {
                CharacterArchiveCardView card = cards[i];
                card.gameObject.SetActive(true);
                card.Bind(source[i], data, current, Same(selected, source[i]), SelectCard);
            }
            for (int i = source.Count; i < cards.Count; i++) cards[i].gameObject.SetActive(false);

            SetActive(rightPanel, selected != null);
            SetActive(expandRightButton != null ? expandRightButton.gameObject : null, selected != null && (rightPanel == null || !rightPanel.activeSelf));
            if (detailCard != null)
            {
                SetActive(detailCard.gameObject, selected != null);
                if (selected != null) detailCard.Bind(selected, data, current, false, null);
            }
            RefreshBookmarks();
            RefreshCount(owned.OwnedCount, owned.AllCharacters.Count);
        }

        private bool ValidateReferences()
        {
            if (catalog != null && content != null && cardTemplate != null) return true;
            Debug.LogWarning("[CharacterArchivePanel] CharacterCatalog, Content, Card Template Inspector references are required.", this);
            return false;
        }

        private void BindButtons()
        {
            Add(allBookmarkButton, ShowAll); Add(ownedBookmarkButton, ShowOwned);
            Add(rightCloseButton, CloseRight); Add(expandRightButton, OpenRight);
        }
        private void BindCountFormat()
        {
            boundCountFormat = ownedCountLocalizer != null ? ownedCountLocalizer.TextReference : null;
            if (boundCountFormat == null || !boundCountFormat.HasReference) return;
            ownedCountLocalizer.enabled = false;
            boundCountFormat.StringChanged += HandleCountFormatChanged;
        }
        private void UnbindCountFormat()
        {
            if (boundCountFormat != null) boundCountFormat.StringChanged -= HandleCountFormatChanged;
            boundCountFormat = null;
        }
        private void HandleCountFormatChanged(string _) => RefreshContents();
        private void UnbindButtons()
        {
            Remove(allBookmarkButton, ShowAll); Remove(ownedBookmarkButton, ShowOwned);
            Remove(rightCloseButton, CloseRight); Remove(expandRightButton, OpenRight);
        }
        private static void Add(Button button, UnityEngine.Events.UnityAction action) { if (button != null) { button.onClick.RemoveListener(action); button.onClick.AddListener(action); } }
        private static void Remove(Button button, UnityEngine.Events.UnityAction action) { if (button != null) button.onClick.RemoveListener(action); }
        private void ShowAll() { if (!ownedOnly) return; ownedOnly = false; RefreshContents(); }
        private void ShowOwned() { if (ownedOnly) return; ownedOnly = true; RefreshContents(); }
        private void CloseRight() { SetActive(rightPanel, false); SetActive(expandRightButton != null ? expandRightButton.gameObject : null, selected != null); }
        private void OpenRight() { if (selected == null) return; SetActive(rightPanel, true); SetActive(expandRightButton != null ? expandRightButton.gameObject : null, false); }
        private void SelectCard(CharacterArchiveCardView card) { selected = card != null ? card.Definition : null; OpenRight(); RefreshContents(); }
        private void HandleRosterChanged(CharacterDefinition _) => RefreshContents();
        private void HandleRecoverySlotsChanged() => RefreshContents();

        private void EnsureCardCount(int count)
        {
            while (cards.Count < count)
            {
                CharacterArchiveCardView item = Instantiate(cardTemplate, content);
                item.gameObject.SetActive(false);
                cards.Add(item);
            }
        }
        private void RefreshBookmarks()
        {
            SetActive(allBookmarkEnabled, !ownedOnly); SetActive(allBookmarkDisabled, ownedOnly);
            SetActive(ownedBookmarkEnabled, ownedOnly); SetActive(ownedBookmarkDisabled, !ownedOnly);
        }
        private void RefreshCount(int owned, int all)
        {
            if (ownedCountText == null) return;
            LocalizedTextReference format = boundCountFormat ?? (ownedCountLocalizer != null ? ownedCountLocalizer.TextReference : null);
            ownedCountText.text = format != null && format.HasReference
                ? format.GetLocalizedString(owned, all) : string.Format("{0}/{1}", owned, all);
        }
        private static bool Contains(IReadOnlyList<CharacterDefinition> values, string id)
        {
            for (int i = 0; i < values.Count; i++) if (values[i] != null && string.Equals(values[i].CharacterId, id, StringComparison.Ordinal)) return true;
            return false;
        }
        private static bool Same(CharacterDefinition left, CharacterDefinition right) => left != null && right != null && string.Equals(left.CharacterId, right.CharacterId, StringComparison.Ordinal);
        private static void SetActive(GameObject target, bool value) { if (target != null && target.activeSelf != value) target.SetActive(value); }
    }
}
