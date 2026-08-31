using System;
using System.Collections.Generic;
using Character;
using Common;
using Dungeon;
using Inventory;
using UnityEngine;

namespace Quest
{
    /// <summary>서사 퀘스트의 유일한 상태 변경 관문. 누적 이벤트는 활성 단계에만 기록하고 목표값에서
    /// 포화한다. 완료 확정은 반드시 <see cref="TryConfirmComplete"/>를 명시적으로 호출해야 한다.</summary>
    [DisallowMultipleComponent]
    public sealed class CharacterStoryQuestService : MonoBehaviour
    {
        [SerializeField] private CharacterStoryQuestCatalog questCatalog;
        [SerializeField] private CharacterStoryQuestObjectiveCatalog objectiveCatalog;
        [SerializeField] private CharacterRoster roster;
        [SerializeField] private InventoryManager inventoryManager;

        [Header("Reward Toast")]
        [SerializeField] private string rewardToastFormat = "획득: +{0} 재화 / {1} x{2}";
        [SerializeField] private string itemOnlyToastFormat = "획득: {0} x{1}";
        [SerializeField] private string currencyOnlyToastFormat = "획득: +{0} 재화";

        private static Func<bool> saveOverride;

        public static CharacterStoryQuestService Instance { get; private set; }
        public static event Action<string> QuestBecameReadyToComplete;

        /// <summary>씬 wiring 검사와 부트스트랩 실패 차단에 쓰는 최소 구성 계약.</summary>
        public bool HasRequiredReferences => questCatalog != null && objectiveCatalog != null &&
                                             roster != null && ResolveInventory() != null;

        private void Awake()
        {
            if (Instance != null && Instance != this) { enabled = false; return; }
            Instance = this;
        }

        private void OnEnable()
        {
            DungeonEntryService.DungeonEntered += HandleDungeonEntered;
            CharacterRoster.CharacterStateChanged += HandleCharacterChanged;
        }

        private void Start()
        {
            if (!HasRequiredReferences)
            {
                Debug.LogError("[CharacterStoryQuestService] Quest Catalog, Objective Catalog, Character Roster를 모두 연결해야 합니다.", this);
                enabled = false;
                return;
            }
            if (SaveSystem.TryGetLoadedData(out SaveData data) && EnsureRootsForOwned(data)) SaveSystem.Save();
        }

        private void OnDisable()
        {
            DungeonEntryService.DungeonEntered -= HandleDungeonEntered;
            CharacterRoster.CharacterStateChanged -= HandleCharacterChanged;
            if (Instance == this) Instance = null;
        }

        public CharacterStoryQuestSnapshot GetSnapshot(string characterId)
        {
            if (!SaveSystem.TryGetLoadedData(out SaveData data)) return CharacterStoryQuestSnapshot.Empty(characterId);
            var state = FindState(data, characterId);
            return SnapshotOf(state);
        }

        public bool TryConfirmComplete(string characterId)
        {
            if (!SaveSystem.TryGetLoadedData(out SaveData data)) return false;
            CharacterStoryQuestSaveState state = FindState(data, characterId);
            CharacterStoryQuestDefinition current = state != null && questCatalog != null
                ? questCatalog.Find(state.activeQuestId) : null;
            if (current == null || !state.readyToComplete) return false;

            InventoryManager inventory = ResolveInventory();
            if (inventory == null) return false;
            BuildRewardRequest(current, out int currencyAmount, out List<InventoryManager.RewardItemStack> itemStacks);
            CharacterStoryQuestMutationReceipt receipt = Capture(data, characterId);
            InventoryRewardMutationReceipt rewardReceipt =
                inventory.ApplyRewardsWithoutSave(currencyAmount, itemStacks);
            if (!ConfirmWithoutSave(data, characterId))
            {
                inventory.RollbackRewards(rewardReceipt);
                return false;
            }
            bool saved;
            try
            {
                saved = SaveNow();
            }
            catch (Exception exception)
            {
                // SaveSystem.Save는 자체적으로 예외를 삼키지만, 시험 이음매나 이후 저장 구현이
                // 예외를 던져도 완료/보상이 메모리에 남으면 안 된다.
                Debug.LogWarning("[CharacterStoryQuestService] 퀘스트 완료 저장 실패: " + exception.Message);
                saved = false;
            }
            if (saved)
            {
                inventory.NotifyRewardsAfterExternalSave(rewardReceipt);
                ShowRewardToast(rewardReceipt.Result);
                return true;
            }
            Rollback(receipt);
            inventory.RollbackRewards(rewardReceipt);
            return false;
        }

        private static void BuildRewardRequest(CharacterStoryQuestDefinition quest, out int currencyAmount,
            out List<InventoryManager.RewardItemStack> itemStacks)
        {
            currencyAmount = 0;
            itemStacks = new List<InventoryManager.RewardItemStack>();
            if (quest == null || quest.Rewards == null) return;
            for (int i = 0; i < quest.Rewards.Count; i++)
            {
                CharacterStoryQuestRewardDefinition reward = quest.Rewards[i];
                if (reward == null || !reward.IsValid) continue;
                if (reward.RewardType == CharacterStoryQuestRewardType.Currency &&
                    reward.Currency != null && reward.Currency.CurrencyId == "jewel")
                    currencyAmount = SaturatingAdd(currencyAmount, reward.Amount);
                else if (reward.RewardType == CharacterStoryQuestRewardType.Item)
                    itemStacks.Add(new InventoryManager.RewardItemStack(reward.Item, reward.Amount));
            }
        }

        private static int SaturatingAdd(int left, int right)
        {
            long sum = (long)left + right;
            return sum >= int.MaxValue ? int.MaxValue : (int)sum;
        }

        private void ShowRewardToast(InventoryRewardApplyResult result)
        {
            if (result == null || result.IsEmpty || ToastManager.Instance == null) return;
            bool hasCurrency = result.ActualCurrencyDelta > 0;
            bool hasItem = result.ItemDeltas.Count > 0;
            string message;
            if (hasCurrency && hasItem)
                message = string.Format(rewardToastFormat, result.ActualCurrencyDelta,
                    DescribeItem(result.ItemDeltas[0].Definition), result.ItemDeltas[0].ActualCount);
            else if (hasItem)
                message = string.Format(itemOnlyToastFormat, DescribeItem(result.ItemDeltas[0].Definition),
                    result.ItemDeltas[0].ActualCount);
            else message = string.Format(currencyOnlyToastFormat, result.ActualCurrencyDelta);
            ToastManager.Instance.Show(message);
        }

        private static string DescribeItem(ItemDefinition item)
        {
            if (item == null) return string.Empty;
            try
            {
                string localized = item.LocalizedName.GetLocalizedString();
                if (!string.IsNullOrWhiteSpace(localized) &&
                    !localized.StartsWith("No translation found", StringComparison.Ordinal)) return localized;
            }
            catch (Exception) { }
            return item.DisplayName;
        }

        private InventoryManager ResolveInventory()
        {
            if (inventoryManager != null) return inventoryManager;
            inventoryManager = InventoryManager.Instance != null
                ? InventoryManager.Instance : FindObjectOfType<InventoryManager>(true);
            return inventoryManager;
        }

        private static bool SaveNow() => saveOverride != null ? saveOverride() : SaveSystem.Save();

        /// <summary>처치 저장 트랜잭션에 붙는 무저장 변경. caller는 SaveSystem.Save 실패 시 receipt를
        /// 롤백해야 하므로 보상/EXP/행동력과 완전히 같은 원자 경계를 공유한다.</summary>
        public CharacterStoryQuestMutationReceipt ApplyDefeatWithoutSave(
            SaveData data, string characterId, string monsterId, int actualStaminaSpent)
        {
            CharacterStoryQuestMutationReceipt receipt = Capture(data, characterId);
            if (data == null || string.IsNullOrEmpty(characterId)) return receipt;
            bool changed = AddForCondition(data, characterId, CharacterStoryQuestConditionType.MonsterDefeatCount, monsterId, 1);
            changed |= AddForCondition(data, characterId, CharacterStoryQuestConditionType.StaminaSpent, null,
                Mathf.Max(0, actualStaminaSpent));
            receipt.Changed = changed;
            return receipt;
        }

        /// <summary>모집처럼 캐릭터를 새로 보유하게 만드는 저장 트랜잭션 안에서 루트만 열어 준다.
        /// 이 메서드는 저장하지 않으므로 호출자가 자기 트랜잭션의 저장 한 번과 롤백을 계속 소유한다.</summary>
        public CharacterStoryQuestMutationReceipt ActivateForCharacterWithoutSave(
            SaveData data, string characterId, int level)
        {
            CharacterStoryQuestMutationReceipt receipt = Capture(data, characterId);
            receipt.Changed = EnsureRootWithoutSave(data, characterId, level);
            return receipt;
        }

        public CharacterStoryQuestMutationReceipt ApplyDungeonEntryWithoutSave(
            SaveData data, string dungeonId, IReadOnlyList<string> partyCharacterIds)
        {
            var receipt = new CharacterStoryQuestMutationReceipt(data);
            if (data == null || partyCharacterIds == null) return receipt;
            for (int i = 0; i < partyCharacterIds.Count; i++)
            {
                string characterId = partyCharacterIds[i];
                if (string.IsNullOrEmpty(characterId)) continue;
                receipt.Capture(characterId, FindState(data, characterId));
                receipt.Changed |= AddForCondition(data, characterId, CharacterStoryQuestConditionType.DungeonEnterCount, dungeonId, 1);
            }
            return receipt;
        }

        public void Rollback(CharacterStoryQuestMutationReceipt receipt) => receipt?.Restore();

        /// <summary>호출자가 소유한 저장 트랜잭션이 성공한 뒤 완료 가능 전환을 확정한다. 동일 영수증은
        /// 한 번만 소비되며, 한 트랜잭션에서 여러 캐릭터가 동시에 달성해도 공용 토스트는 한 번만
        /// 표시한다.</summary>
        public bool NotifyReadyAfterExternalSave(CharacterStoryQuestMutationReceipt receipt)
        {
            if (receipt == null || !receipt.TryConsumeReadyTransition(out string characterId)) return false;

            QuestBecameReadyToComplete?.Invoke(characterId);
            ShowReadyToast();
            return true;
        }

        public bool EnsureRootsForOwned(SaveData data)
        {
            if (data == null || data.characters == null) return false;
            bool changed = false;
            foreach (CharacterSaveState character in data.characters)
                if (character != null) changed |= EnsureRootWithoutSave(data, character.characterId, character.level);
            return changed;
        }

        private void HandleDungeonEntered(DungeonDefinition dungeon)
        {
            if (dungeon == null || !SaveSystem.TryGetLoadedData(out SaveData data)) return;
            var party = data.partyCharacterIds ?? new List<string>();
            CharacterStoryQuestMutationReceipt receipt = ApplyDungeonEntryWithoutSave(data, dungeon.DungeonId, party);
            if (!receipt.Changed) return;
            if (!SaveSystem.Save()) Rollback(receipt);
            else NotifyReadyAfterExternalSave(receipt);
        }

        private void HandleCharacterChanged(CharacterDefinition definition)
        {
            if (definition == null || !SaveSystem.TryGetLoadedData(out SaveData data)) return;
            CharacterSaveState character = FindCharacter(data, definition.CharacterId);
            if (character == null) return;
            CharacterStoryQuestMutationReceipt receipt = Capture(data, definition.CharacterId);
            bool changed = EnsureRootWithoutSave(data, definition.CharacterId, character.level) |
                           EvaluateLevelWithoutSave(data, definition.CharacterId, character.level);
            receipt.Changed = changed;
            if (!changed) return;
            if (!SaveSystem.Save()) Rollback(receipt);
            else NotifyReadyAfterExternalSave(receipt);
        }

        private static void ShowReadyToast()
        {
            if (ToastManager.Instance == null) return;
            try
            {
                var text = new LocalizedTextReference { TableReference = "01_UI", TableEntryReference = "92" };
                string localized = text.GetLocalizedString();
                if (!string.IsNullOrEmpty(localized) &&
                    !localized.StartsWith("No translation found", StringComparison.Ordinal))
                {
                    ToastManager.Instance.Show(localized);
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[CharacterStoryQuestService] 완료 가능 토스트를 표시하지 못했습니다: " +
                                 exception.Message);
            }
        }

        private bool EnsureRootWithoutSave(SaveData data, string characterId, int level)
        {
            if (string.IsNullOrEmpty(characterId) || questCatalog == null) return false;
            CharacterStoryQuestSaveState state = FindState(data, characterId);
            if (state != null) return false;
            CharacterStoryQuestDefinition root = questCatalog.FindRoot(characterId);
            if (root == null) return false;
            state = new CharacterStoryQuestSaveState { characterId = characterId, activeQuestId = root.QuestId };
            data.characterStoryQuests.Add(state);
            EvaluateLevelWithoutSave(data, characterId, level);
            return true;
        }

        private bool EvaluateLevelWithoutSave(SaveData data, string characterId, int level)
        {
            CharacterStoryQuestSaveState state = FindState(data, characterId);
            if (state == null || state.readyToComplete || string.IsNullOrEmpty(state.activeQuestId)) return false;
            bool changed = false;
            foreach (var objective in ObjectivesFor(state.activeQuestId))
                if (objective.ConditionType == CharacterStoryQuestConditionType.CharacterLevelAtLeast)
                    changed |= SetProgress(state, objective, level);
            return changed | RefreshReady(state);
        }

        private bool AddForCondition(SaveData data, string characterId, CharacterStoryQuestConditionType condition,
            string targetId, int amount)
        {
            if (amount <= 0) return false;
            CharacterStoryQuestSaveState state = FindState(data, characterId);
            if (state == null || state.readyToComplete || string.IsNullOrEmpty(state.activeQuestId)) return false;
            bool changed = false;
            foreach (var objective in ObjectivesFor(state.activeQuestId))
                if (objective.ConditionType == condition && objective.Targets(targetId))
                    changed |= AddProgress(state, objective, amount);
            return changed | RefreshReady(state);
        }

        private bool ConfirmWithoutSave(SaveData data, string characterId)
        {
            CharacterStoryQuestSaveState state = FindState(data, characterId);
            if (state == null || !state.readyToComplete || questCatalog == null) return false;
            CharacterStoryQuestDefinition current = questCatalog.Find(state.activeQuestId);
            if (current == null) return false;
            if (!state.completedQuestIds.Contains(current.QuestId)) state.completedQuestIds.Add(current.QuestId);
            state.objectiveProgress.Clear(); state.readyToComplete = false;
            if (current.IsFinal) { state.activeQuestId = string.Empty; state.graduated = true; return true; }
            CharacterStoryQuestDefinition next = questCatalog.FindNext(current.QuestId);
            if (next == null) { state.activeQuestId = string.Empty; return true; }
            state.activeQuestId = next.QuestId;
            CharacterSaveState character = FindCharacter(data, characterId);
            EvaluateLevelWithoutSave(data, characterId, character != null ? character.level : 1);
            return true;
        }

        private IEnumerable<CharacterStoryQuestObjectiveDefinition> ObjectivesFor(string questId) =>
            objectiveCatalog != null ? objectiveCatalog.ForQuest(questId) : new List<CharacterStoryQuestObjectiveDefinition>();

        private bool RefreshReady(CharacterStoryQuestSaveState state)
        {
            var objectives = objectiveCatalog != null ? objectiveCatalog.ForQuest(state.activeQuestId) : null;
            if (objectives == null || objectives.Count == 0) return false;
            foreach (var objective in objectives) if (GetProgress(state, objective.ObjectiveId) < objective.RequiredValue) return false;
            if (state.readyToComplete) return false;
            state.readyToComplete = true; return true;
        }

        private static int GetProgress(CharacterStoryQuestSaveState state, string objectiveId)
        {
            foreach (var entry in state.objectiveProgress) if (entry != null && entry.objectiveId == objectiveId) return entry.progress;
            return 0;
        }
        private static bool AddProgress(CharacterStoryQuestSaveState state, CharacterStoryQuestObjectiveDefinition objective, int amount) =>
            SetProgress(state, objective, Math.Min(objective.RequiredValue, GetProgress(state, objective.ObjectiveId) + amount));
        private static bool SetProgress(CharacterStoryQuestSaveState state, CharacterStoryQuestObjectiveDefinition objective, int value)
        {
            int capped = Math.Min(objective.RequiredValue, Math.Max(0, value));
            foreach (var entry in state.objectiveProgress)
                if (entry != null && entry.objectiveId == objective.ObjectiveId)
                { if (entry.progress == capped) return false; entry.progress = capped; return true; }
            state.objectiveProgress.Add(new CharacterStoryObjectiveProgressSaveState { objectiveId = objective.ObjectiveId, progress = capped });
            return true;
        }
        private static CharacterStoryQuestSaveState FindState(SaveData data, string characterId)
        {
            if (data?.characterStoryQuests == null) return null;
            foreach (var state in data.characterStoryQuests) if (state != null && state.characterId == characterId) return state;
            return null;
        }
        private static CharacterSaveState FindCharacter(SaveData data, string characterId)
        {
            if (data?.characters == null) return null;
            foreach (var state in data.characters) if (state != null && state.characterId == characterId) return state;
            return null;
        }
        private static CharacterStoryQuestMutationReceipt Capture(SaveData data, string characterId)
        {
            var receipt = new CharacterStoryQuestMutationReceipt(data); receipt.Capture(characterId, FindState(data, characterId)); return receipt;
        }
        private static CharacterStoryQuestSnapshot SnapshotOf(CharacterStoryQuestSaveState state)
        {
            if (state == null) return CharacterStoryQuestSnapshot.Empty(string.Empty);
            var progress = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var item in state.objectiveProgress) if (item != null) progress[item.objectiveId] = item.progress;
            return new CharacterStoryQuestSnapshot(state.characterId, state.activeQuestId, state.readyToComplete, state.graduated,
                new List<string>(state.completedQuestIds), progress);
        }
    }

    public sealed class CharacterStoryQuestSnapshot
    {
        public readonly string CharacterId; public readonly string ActiveQuestId; public readonly bool ReadyToComplete; public readonly bool Graduated;
        public readonly IReadOnlyList<string> CompletedQuestIds; public readonly IReadOnlyDictionary<string, int> ObjectiveProgress;
        public CharacterStoryQuestSnapshot(string characterId, string activeQuestId, bool ready, bool graduated, List<string> completed, Dictionary<string, int> progress)
        { CharacterId = characterId; ActiveQuestId = activeQuestId; ReadyToComplete = ready; Graduated = graduated; CompletedQuestIds = completed; ObjectiveProgress = progress; }
        public static CharacterStoryQuestSnapshot Empty(string characterId) => new CharacterStoryQuestSnapshot(characterId, string.Empty, false, false, new List<string>(), new Dictionary<string, int>());
    }

    public sealed class CharacterStoryQuestMutationReceipt
    {
        private readonly SaveData data; private readonly Dictionary<string, CharacterStoryQuestSaveState> before = new Dictionary<string, CharacterStoryQuestSaveState>(StringComparer.Ordinal);
        private bool readyTransitionConsumed;
        internal bool Changed;
        internal CharacterStoryQuestMutationReceipt(SaveData data) { this.data = data; }
        internal void Capture(string id, CharacterStoryQuestSaveState state) { if (!before.ContainsKey(id)) before[id] = Clone(state); }
        internal bool TryConsumeReadyTransition(out string characterId)
        {
            characterId = null;
            if (readyTransitionConsumed) return false;
            readyTransitionConsumed = true;

            foreach (KeyValuePair<string, CharacterStoryQuestSaveState> pair in before)
            {
                CharacterStoryQuestSaveState current = FindCurrent(pair.Key);
                if (current == null || !current.readyToComplete) continue;
                CharacterStoryQuestSaveState previous = pair.Value;
                if (previous != null && previous.readyToComplete &&
                    string.Equals(previous.activeQuestId, current.activeQuestId, StringComparison.Ordinal)) continue;
                characterId = pair.Key;
                return true;
            }
            return false;
        }
        public void Restore()
        {
            if (data == null) return;
            foreach (var pair in before)
            {
                CharacterStoryQuestSaveState current = null;
                if (data.characterStoryQuests != null) foreach (var entry in data.characterStoryQuests) if (entry != null && entry.characterId == pair.Key) { current = entry; break; }
                if (pair.Value == null) { if (current != null) data.characterStoryQuests.Remove(current); }
                else if (current != null) Copy(pair.Value, current); else data.characterStoryQuests.Add(Clone(pair.Value));
            }
        }
        private CharacterStoryQuestSaveState FindCurrent(string characterId)
        {
            if (data?.characterStoryQuests == null) return null;
            foreach (CharacterStoryQuestSaveState state in data.characterStoryQuests)
                if (state != null && string.Equals(state.characterId, characterId, StringComparison.Ordinal)) return state;
            return null;
        }
        private static CharacterStoryQuestSaveState Clone(CharacterStoryQuestSaveState state)
        {
            if (state == null) return null; var clone = new CharacterStoryQuestSaveState { characterId = state.characterId, activeQuestId = state.activeQuestId, readyToComplete = state.readyToComplete, graduated = state.graduated, completedQuestIds = new List<string>(state.completedQuestIds), objectiveProgress = new List<CharacterStoryObjectiveProgressSaveState>() };
            foreach (var p in state.objectiveProgress) if (p != null) clone.objectiveProgress.Add(new CharacterStoryObjectiveProgressSaveState { objectiveId = p.objectiveId, progress = p.progress }); return clone;
        }
        private static void Copy(CharacterStoryQuestSaveState from, CharacterStoryQuestSaveState to) { var clone = Clone(from); to.characterId = clone.characterId; to.activeQuestId = clone.activeQuestId; to.readyToComplete = clone.readyToComplete; to.graduated = clone.graduated; to.completedQuestIds = clone.completedQuestIds; to.objectiveProgress = clone.objectiveProgress; }
    }
}
