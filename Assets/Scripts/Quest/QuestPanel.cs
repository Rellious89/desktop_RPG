using System;
using System.Collections.Generic;
using Character;
using Common;
using Party;
using UnityEngine;
using UnityEngine.UI;

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
        [SerializeField] private GameObject subPanel;
        [SerializeField] private Button subPanelCloseButton;

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
        public bool IsDetailOpen => subPanel != null && subPanel.activeSelf;
        public bool HasRequiredReferences => characterCatalog != null && questCatalog != null && objectiveCatalog != null &&
                                             detailView != null && detailView.HasRequiredReferences && subPanel != null &&
                                             subPanelCloseButton != null && HasValidSlots();

        /// <summary>알림 딥링크. 대상이 고정 파티 슬롯의 활성 퀘스트 또는 AllClear 카드라면 선택한다.</summary>
        public bool OpenForCharacter(string characterId)
        {
            requestedCharacterId = characterId;
            Open();
            BindDetailCloseButton();
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
            BindDetailCloseButton();
            OpenDetail();
        }

        protected override void OnModalClosed()
        {
            CharacterStoryQuestService.QuestStateChanged -= HandleQuestStateChanged;
            PartyCompositionEvents.ChangedAfterSave -= HandlePartyChanged;
            if (subPanelCloseButton != null) subPanelCloseButton.onClick.RemoveListener(CloseDetail);
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
                if (character != null && (quest != null || snapshot.Graduated)) validQuestCharacters.Add(characterId);
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
                if (slotCharacters[i] != null && valid.Contains(slotCharacters[i].CharacterId)) return slotCharacters[i].CharacterId;
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
            if (quest == null && snapshot.Graduated) quest = LastNarrativeQuest(characterId, snapshot);

            List<CharacterStoryQuestObjectiveDefinition> objectives = EnabledObjectives(quest);
            CharacterStoryQuestSnapshot displaySnapshot = snapshot.Graduated && quest != null
                ? CompletedPresentationSnapshot(snapshot, objectives)
                : snapshot;
            detailView.Bind(characterId, quest, objectives, displaySnapshot, completing, TryComplete);
        }

        // Graduation is committed only after the final quest completes.  The UI intentionally
        // resolves that final definition from the catalog instead of restoring an active quest
        // into save data; this is a presentation-only read of the existing completion state.
        private CharacterStoryQuestDefinition LastNarrativeQuest(string characterId, CharacterStoryQuestSnapshot snapshot)
        {
            CharacterStoryQuestDefinition result = null;
            if (questCatalog?.Quests == null) return null;
            IReadOnlyList<CharacterStoryQuestDefinition> quests = questCatalog.Quests;
            for (int i = 0; i < quests.Count; i++)
            {
                CharacterStoryQuestDefinition candidate = quests[i];
                if (candidate == null || !candidate.IsFinal ||
                    !string.Equals(candidate.CharacterId, characterId, StringComparison.Ordinal)) continue;
                if (result == null || candidate.DisplayOrder > result.DisplayOrder ||
                    candidate.DisplayOrder == result.DisplayOrder &&
                    string.CompareOrdinal(candidate.QuestId, result.QuestId) > 0)
                    result = candidate;
            }

            // Keep a readable completed detail for legacy or incomplete table data that lacks a
            // final marker, without mutating the quest sequence or its persisted progress.
            if (result != null || snapshot?.CompletedQuestIds == null) return result;
            for (int i = 0; i < snapshot.CompletedQuestIds.Count; i++)
            {
                CharacterStoryQuestDefinition candidate = questCatalog.Find(snapshot.CompletedQuestIds[i]);
                if (candidate == null || !string.Equals(candidate.CharacterId, characterId, StringComparison.Ordinal)) continue;
                if (result == null || candidate.DisplayOrder > result.DisplayOrder ||
                    candidate.DisplayOrder == result.DisplayOrder &&
                    string.CompareOrdinal(candidate.QuestId, result.QuestId) > 0)
                    result = candidate;
            }
            return result;
        }

        private static CharacterStoryQuestSnapshot CompletedPresentationSnapshot(
            CharacterStoryQuestSnapshot snapshot,
            IReadOnlyList<CharacterStoryQuestObjectiveDefinition> objectives)
        {
            var progress = new Dictionary<string, int>(StringComparer.Ordinal);
            if (snapshot?.ObjectiveProgress != null)
                foreach (KeyValuePair<string, int> pair in snapshot.ObjectiveProgress) progress[pair.Key] = pair.Value;
            if (objectives != null)
                for (int i = 0; i < objectives.Count; i++)
                    if (objectives[i] != null) progress[objectives[i].ObjectiveId] = objectives[i].RequiredValue;
            var completed = snapshot?.CompletedQuestIds != null
                ? new List<string>(snapshot.CompletedQuestIds)
                : new List<string>();
            return new CharacterStoryQuestSnapshot(snapshot != null ? snapshot.CharacterId : string.Empty,
                snapshot != null ? snapshot.ActiveQuestId : string.Empty, false, true, completed, progress);
        }

        private void SelectCharacter(string characterId)
        {
            if (completing || string.IsNullOrEmpty(characterId)) return;
            selectedCharacterId = characterId;
            requestedCharacterId = characterId;
            OpenDetail();
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

        private void CloseDetail()
        {
            if (subPanel != null) subPanel.SetActive(false);
        }

        private void BindDetailCloseButton()
        {
            if (subPanelCloseButton == null) return;
            subPanelCloseButton.onClick.RemoveListener(CloseDetail);
            subPanelCloseButton.onClick.AddListener(CloseDetail);
        }

        private void OpenDetail()
        {
            if (subPanel != null && !subPanel.activeSelf) subPanel.SetActive(true);
        }

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
