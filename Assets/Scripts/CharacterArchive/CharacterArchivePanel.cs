using System;
using System.Collections.Generic;
using Character;
using Common;
using Recovery;
using Party;
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
        [SerializeField] private PartyConfigCatalog partyConfigCatalog;
        [Header("Party Toasts (Inspector에서만 연결)")]
        [SerializeField] private LocalizedTextReference recoveryLeaveBlockedToast;
        [SerializeField] private LocalizedTextReference partyJoinToast;
        [SerializeField] private LocalizedTextReference activeCharacterBlockedToast;

        private readonly List<CharacterArchiveCardView> cards = new List<CharacterArchiveCardView>();
        private readonly List<PartySlotView> partySlots = new List<PartySlotView>();
        private PartyCompositionService partyService;
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
            BindPartySlots();
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
            UnbindPartySlots();
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
            RefreshPartySlots(data, current);
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

        private void BindPartySlots()
        {
            partySlots.Clear();
            PartySlotView[] found = GetComponentsInChildren<PartySlotView>(true);
            for (int i = 0; i < found.Length; i++) partySlots.Add(found[i]);

            // UI 프리팹은 이미 만들어져 있으므로, 이전 버전의 프리팹에도 런타임에서 동작하도록
            // 이름이 정해진 슬롯에만 컴포넌트를 붙인다. RectTransform/아트 설정은 바꾸지 않는다.
            if (partySlots.Count == 0)
            {
                Transform[] transforms = GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < transforms.Length; i++)
                {
                    if (!transforms[i].name.StartsWith("slot_CharacterArchive_Party", StringComparison.Ordinal)) continue;
                    partySlots.Add(transforms[i].gameObject.AddComponent<PartySlotView>());
                }
            }

            partySlots.Sort((left, right) => left.transform.GetSiblingIndex().CompareTo(right.transform.GetSiblingIndex()));
            for (int i = 0; i < partySlots.Count; i++) partySlots[i].Bind(this, i);
            partyService = new PartyCompositionService(() => SaveSystem.Data, SaveSystem.Save, ResolvePartyConfigCatalog());
        }

        private void UnbindPartySlots()
        {
            for (int i = 0; i < partySlots.Count; i++) partySlots[i].Unbind();
            partySlots.Clear();
            partyService = null;
        }

        private PartyConfigCatalog ResolvePartyConfigCatalog()
        {
            if (partyConfigCatalog != null) return partyConfigCatalog;
            PartyConfigCatalog[] candidates = Resources.FindObjectsOfTypeAll<PartyConfigCatalog>();
            return candidates != null && candidates.Length > 0 ? candidates[0] : null;
        }

        private void RefreshPartySlots(SaveData data, CharacterDefinition current)
        {
            if (partyService == null) return;
            PartyCapacityResult capacity = partyService.GetCapacity();
            int count = capacity.IsAvailable ? capacity.Capacity : 0;
            for (int i = 0; i < partySlots.Count; i++) partySlots[i].Refresh(data, current, count);
        }

        internal void HandlePartyDrop(CharacterDefinition incoming, PartySlotView target, bool fromPartySlot)
        {
            if (incoming == null || target == null || partyService == null || !target.IsEnabled) return;
            SaveData data = SaveSystem.Data;
            string incomingId = incoming.CharacterId;
            string outgoingId = target.CharacterId;
            CharacterDefinition current = CharacterRoster.Instance != null ? CharacterRoster.Instance.Current : null;

            bool alreadyInParty = data != null && data.partyCharacterIds != null && data.partyCharacterIds.Contains(incomingId);
            if (fromPartySlot || alreadyInParty)
            {
                int targetIndex = data.partyCharacterIds.Count > 0
                    ? Mathf.Min(target.SlotIndex, data.partyCharacterIds.Count - 1) : 0;
                ApplyPartyResult(partyService.TryMove(incomingId, targetIndex), incoming, false);
                return;
            }

            if (string.Equals(incomingId, outgoingId, StringComparison.Ordinal)) return;
            if (string.IsNullOrEmpty(outgoingId))
            {
                ApplyPartyResult(partyService.TryJoin(incomingId), incoming, true);
                return;
            }

            if (Same(current, outgoingId)) { ShowToast(activeCharacterBlockedToast); return; }
            if (RecoveryStation.IsCharacterIdInSavedSlot(data, incomingId) || RecoveryStation.IsCharacterIdInSavedSlot(data, outgoingId)) { ShowToast(recoveryLeaveBlockedToast); return; }
            ApplyPartyResult(partyService.TryReplace(outgoingId, incomingId), incoming, true);
        }

        internal CharacterDefinition FindCharacter(string id) => catalog != null ? catalog.Find(id) : null;

        internal void LeavePartyMember(PartySlotView slot)
        {
            if (slot == null || partyService == null || string.IsNullOrEmpty(slot.CharacterId)) return;
            CharacterDefinition current = CharacterRoster.Instance != null ? CharacterRoster.Instance.Current : null;
            if (Same(current, slot.CharacterId)) { ShowToast(activeCharacterBlockedToast); return; }
            if (RecoveryStation.IsCharacterIdInSavedSlot(SaveSystem.Data, slot.CharacterId)) { ShowToast(recoveryLeaveBlockedToast); return; }
            ApplyPartyResult(partyService.TryLeave(slot.CharacterId), null, false);
        }

        private void ApplyPartyResult(PartyCompositionResult result, CharacterDefinition successCharacter, bool showSuccess)
        {
            if (!result.Success)
            {
                if (result.Code == PartyCompositionCode.InRecovery) ShowToast(recoveryLeaveBlockedToast);
                return;
            }

            CharacterRoster.Instance?.RefreshPartyAfterExternalSave();
            CharacterSwapPanel.RequestRefresh();
            RecoveryService.NotifyRosterChangedAfterExternalSave();
            if (showSuccess && successCharacter != null) ShowToast(partyJoinToast, CharacterNameBinding.GetCurrent(successCharacter));
            RefreshContents();
        }

        private static bool Same(CharacterDefinition character, string id) => character != null && string.Equals(character.CharacterId, id, StringComparison.Ordinal);
        private static void ShowToast(LocalizedTextReference message, params object[] arguments)
        {
            if (ToastManager.Instance == null || message == null || !message.HasReference) return;
            string localizedMessage = message.GetLocalizedString(arguments);
            if (string.IsNullOrEmpty(localizedMessage) || localizedMessage.StartsWith("No translation found", StringComparison.Ordinal)) return;
            ToastManager.Instance.Show(localizedMessage);
        }
    }
}
