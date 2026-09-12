using System;
using System.Collections.Generic;
using UnityEngine;

namespace Quest
{
    public static class CharacterStoryQuestTarget
    {
        private const char CompositeSeparator = '@';

        public static string ItemUse(string itemId, string characterId)
        {
            return (itemId ?? string.Empty) + CompositeSeparator + (characterId ?? string.Empty);
        }

        public static bool TrySplitItemUse(string value, out string itemId, out string characterId)
        {
            itemId = value ?? string.Empty;
            characterId = string.Empty;
            int separator = itemId.IndexOf(CompositeSeparator);
            if (separator <= 0 || separator >= itemId.Length - 1) return false;
            characterId = itemId.Substring(separator + 1);
            itemId = itemId.Substring(0, separator);
            return true;
        }
    }

    public enum CharacterStoryQuestConditionType
    {
        CharacterLevelAtLeast,
        MonsterDefeatCount,
        DungeonEnterCount,
        StaminaSpent,
        TownReturnCount,
        BuildingCompleted,
        CharacterOwned,
        CharacterArchiveOpenCount,
        PartyContainsCharacter,
        RecoveryStarted,
        CharacterRecoveryComplete,
        RecoveryJoined,
        ItemPurchaseCount,
        ItemUseCount,
        CharacterStaminaFull,
        ManualCharacterSwitchCount,
    }

    [CreateAssetMenu(fileName = "CharacterStoryQuestObjectiveDefinition", menuName = "Quest/Character Story Quest Objective Definition")]
    public sealed class CharacterStoryQuestObjectiveDefinition : ScriptableObject
    {
        [SerializeField] private string objectiveId;
        [SerializeField] private string questId;
        [SerializeField] private CharacterStoryQuestConditionType conditionType;
        [SerializeField] private List<string> targetIds = new List<string>();
        [SerializeField] private int requiredValue = 1;
        [SerializeField] private int displayOrder;
        [SerializeField] private bool enabled;

        public string ObjectiveId => objectiveId ?? string.Empty;
        public string QuestId => questId ?? string.Empty;
        public CharacterStoryQuestConditionType ConditionType => conditionType;
        public IReadOnlyList<string> TargetIds => targetIds ?? (targetIds = new List<string>());
        public int RequiredValue => Mathf.Max(1, requiredValue);
        public int DisplayOrder => displayOrder;
        public bool Enabled => enabled;
        public bool IsValid => !string.IsNullOrWhiteSpace(ObjectiveId) && !string.IsNullOrWhiteSpace(QuestId);

        public bool Targets(string targetId)
        {
            if (targetIds == null || targetIds.Count == 0) return true;
            for (int i = 0; i < targetIds.Count; i++)
            {
                if (string.Equals(targetIds[i], targetId, StringComparison.Ordinal)) return true;
                if (CharacterStoryQuestTarget.TrySplitItemUse(targetId, out string itemId, out _) &&
                    string.Equals(targetIds[i], itemId, StringComparison.Ordinal)) return true;
            }
            return false;
        }
    }
}
