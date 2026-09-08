using System;
using System.Collections.Generic;
using Character;
using Common;
using Party;
using UnityEngine;

namespace Quest
{
    /// <summary>pn_Quest의 독립 캐릭터 서사 퀘스트 패널. 고정 파티 슬롯을 읽기만 하고 완료 저장은
    /// CharacterStoryQuestService의 expected quest 관문으로만 요청한다.</summary>
    [DisallowMultipleComponent]
    public sealed class QuestPanel : ModalPanel
    {
        [Serializable]
        private sealed class FixedSlot
        {
            [SerializeField] internal GameObject emptyRoot;
            [SerializeField] internal GameObject characterRoot;
            [SerializeField] internal CharacterQuestCardView card;
        }

        [Header("Catalogs (Inspector에서만 연결)")]
        [SerializeField] private CharacterCatalog characterCatalog;
        [SerializeField] private CharacterStoryQuestCatalog questCatalog;
        [SerializeField] private CharacterStoryQuestObjectiveCatalog objectiveCatalog;

        [Header("Fixed Party Slots")]
        [SerializeField] private FixedSlot[] slots = Array.Empty<FixedSlot>();
        [SerializeField] private CharacterStoryQuestDetailView detailView;

        private readonly CharacterDefinition[] slotCharacters = new CharacterDefinition[3];
        private readonly CharacterStoryQuestDefinition[] slotQuests = new CharacterStoryQuestDefinition[3];
        private string selectedCharacterId;
        private string requestedCharacterId;
        private bool completing;
        private bool refreshing;
        private bool refreshQueued;

        public string SelectedCharacterId => selectedCharacterId ?? string.Empty;
        public int SlotCount => slots != null ? slots.Length : 0;
        public bool IsCompleting => completing;
        public CharacterStoryQuestDetailView DetailView => detailView;
        public bool HasRequiredReferences => characterCatalog != null && questCatalog != null && objectiveCatalog != null &&
                                             detailView != null && detailView.HasRequiredReferences && HasValidSlots();

        /// <summary>알림 딥링크. 대상이 현재 고정 파티 슬롯의 활성 퀘스트라면 해당 카드를 선택한다.</summary>
        public bool OpenForCharacter(string characterId)
        {
            requestedCharacterId = characterId;
            Open();
            if (gameObject.activeInHierarchy &&
                !string.Equals(selectedCharacterId, characterId, StringComparison.Ordinal)) RefreshContents();
            return gameObject.activeInHierarchy &&
                   string.Equals(selectedCharacterId, characterId, StringComparison.Ordinal);
        }

        public void RequestRefresh()
        {
            if (gameObject.activeInHierarchy) RefreshContents();
        }

        protected override void OnModalOpened()
        {
            CharacterStoryQuestService.QuestStateChanged -= HandleQuestStateChanged;
            CharacterStoryQuestService.QuestStateChanged += HandleQuestStateChanged;
            PartyCompositionEvents.ChangedAfterSave -= HandlePartyChanged;
            PartyCompositionEvents.ChangedAfterSave += HandlePartyChanged;
        }

        protected override void OnModalClosed()
        {
            CharacterStoryQuestService.QuestStateChanged -= HandleQuestStateChanged;
            PartyCompositionEvents.ChangedAfterSave -= HandlePartyChanged;
            completing = false;
            for (int i = 0; slots != null && i < slots.Length; i++) slots[i]?.card?.Clear();
            detailView?.Clear();
        }

        protected override void RefreshContents()
        {
            if (refreshing)
            {
                refreshQueued = true;
                return;
            }

            do
            {
                refreshQueued = false;
                refreshing = true;
                try { RefreshImmediate(); }
                finally { refreshing = false; }
            }
            while (refreshQueued);
        }

        private void RefreshImmediate()
        {
            if (!HasRequiredReferences)
            {
                SetCompletionInputs(false);
                return;
            }

            SaveData data = SaveSystem.Data;
            var validQuestCharacters = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < 3; i++)
            {
                slotCharacters[i] = null;
                slotQuests[i] = null;
                FixedSlot slot = slots[i];
                string characterId = data != null ? PartySlotUtility.At(data.partyCharacterIds, i) : string.Empty;
                bool occupied = !string.IsNullOrEmpty(characterId);
                SetActive(slot.emptyRoot, !occupied);
                SetActive(slot.characterRoot, occupied);
                if (!occupied)
                {
                    slot.card.Clear();
                    continue;
                }

                CharacterDefinition character = characterCatalog.Find(characterId);
                CharacterStoryQuestSnapshot snapshot = CharacterStoryQuestService.Instance != null
                    ? CharacterStoryQuestService.Instance.GetSnapshot(characterId)
                    : CharacterStoryQuestSnapshot.Empty(characterId);
                CharacterStoryQuestDefinition quest = !string.IsNullOrEmpty(snapshot.ActiveQuestId)
                    ? questCatalog.Find(snapshot.ActiveQuestId)
                    : null;
                if (quest != null && !string.Equals(quest.CharacterId, characterId, StringComparison.Ordinal)) quest = null;
                int level = CharacterLevel(data, characterId);
                List<CharacterStoryQuestObjectiveDefinition> objectives = EnabledObjectives(quest);
                slotCharacters[i] = character;
                slotQuests[i] = quest;
                if (character != null && quest != null) validQuestCharacters.Add(characterId);
                slot.card.Bind(character, level, quest, objectives, snapshot, completing, SelectCharacter, TryComplete);
            }

            string nextSelection = ResolveSelection(validQuestCharacters);
            selectedCharacterId = nextSelection;
            requestedCharacterId = null;
            for (int i = 0; i < 3; i++)
                slots[i].card.SetSelected(!string.IsNullOrEmpty(nextSelection) &&
                                          string.Equals(slots[i].card.CharacterId, nextSelection, StringComparison.Ordinal));
            RefreshDetail(nextSelection);
        }

        private string ResolveSelection(HashSet<string> valid)
        {
            if (!string.IsNullOrEmpty(requestedCharacterId) && valid.Contains(requestedCharacterId)) return requestedCharacterId;
            if (!string.IsNullOrEmpty(selectedCharacterId) && valid.Contains(selectedCharacterId)) return selectedCharacterId;
            for (int i = 0; i < 3; i++)
                if (slotCharacters[i] != null && slotQuests[i] != null) return slotCharacters[i].CharacterId;
            return string.Empty;
        }

        private void RefreshDetail(string characterId)
        {
            if (string.IsNullOrEmpty(characterId))
            {
                detailView.Clear();
                return;
            }
            CharacterStoryQuestSnapshot snapshot = CharacterStoryQuestService.Instance != null
                ? CharacterStoryQuestService.Instance.GetSnapshot(characterId)
                : CharacterStoryQuestSnapshot.Empty(characterId);
            CharacterStoryQuestDefinition quest = !string.IsNullOrEmpty(snapshot.ActiveQuestId)
                ? questCatalog.Find(snapshot.ActiveQuestId)
                : null;
            if (quest != null && !string.Equals(quest.CharacterId, characterId, StringComparison.Ordinal)) quest = null;
            detailView.Bind(characterId, quest, EnabledObjectives(quest), snapshot, completing, TryComplete);
        }

        private void SelectCharacter(string characterId)
        {
            if (completing || string.IsNullOrEmpty(characterId)) return;
            selectedCharacterId = characterId;
            requestedCharacterId = characterId;
            RefreshContents();
        }

        private void TryComplete(string characterId, string expectedQuestId)
        {
            if (completing || string.IsNullOrEmpty(characterId) || string.IsNullOrEmpty(expectedQuestId)) return;
            CharacterStoryQuestService service = CharacterStoryQuestService.Instance;
            if (service == null) return;
            CharacterStoryQuestSnapshot before = service.GetSnapshot(characterId);
            if (!before.ReadyToComplete || !string.Equals(before.ActiveQuestId, expectedQuestId, StringComparison.Ordinal)) return;

            completing = true;
            SetCompletionInputs(false);
            try { service.TryConfirmComplete(characterId, expectedQuestId); }
            finally
            {
                completing = false;
                requestedCharacterId = characterId;
                RefreshContents();
            }
        }

        private void SetCompletionInputs(bool enabled)
        {
            for (int i = 0; slots != null && i < slots.Length; i++) slots[i]?.card?.SetCompletionInputEnabled(enabled);
            detailView?.SetCompletionInputEnabled(enabled);
        }

        private void HandleQuestStateChanged(string _) => RefreshContents();
        private void HandlePartyChanged() => RefreshContents();

        private List<CharacterStoryQuestObjectiveDefinition> EnabledObjectives(CharacterStoryQuestDefinition quest)
        {
            if (quest == null) return new List<CharacterStoryQuestObjectiveDefinition>();
            List<CharacterStoryQuestObjectiveDefinition> result = objectiveCatalog.ForQuest(quest.QuestId);
            result.RemoveAll(item => item == null || !item.Enabled);
            return result;
        }

        private static int CharacterLevel(SaveData data, string characterId)
        {
            if (data?.characters == null) return 1;
            for (int i = 0; i < data.characters.Count; i++)
            {
                CharacterSaveState state = data.characters[i];
                if (state != null && string.Equals(state.characterId, characterId, StringComparison.Ordinal))
                    return Mathf.Max(1, state.level);
            }
            return 1;
        }

        private bool HasValidSlots()
        {
            if (slots == null || slots.Length != 3) return false;
            for (int i = 0; i < slots.Length; i++)
                if (slots[i] == null || slots[i].emptyRoot == null || slots[i].characterRoot == null ||
                    slots[i].card == null || !slots[i].card.HasRequiredReferences) return false;
            return true;
        }

        private static void SetActive(GameObject target, bool value)
        {
            if (target != null && target.activeSelf != value) target.SetActive(value);
        }
    }
}
