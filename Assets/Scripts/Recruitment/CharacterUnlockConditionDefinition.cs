using System;
using System.Collections.Generic;
using UnityEngine;

namespace Recruitment
{
    public enum CharacterUnlockConditionType
    {
        Unknown = 0,
        MaxOwnedCharacterLevelAtLeast,
        OwnedCharacterCountAtLeast,
    }

    [Serializable]
    public struct CharacterUnlockConditionEntry
    {
        [SerializeField] private string entryId;
        [SerializeField] private string groupId;
        [SerializeField] private string conditionType;
        [SerializeField] private string targetId;
        [SerializeField] private int requiredValue;
        [SerializeField] private bool enabled;

        public string EntryId => entryId ?? string.Empty;
        public string GroupId => groupId ?? string.Empty;
        public string ConditionTypeText => conditionType ?? string.Empty;
        public string TargetId => targetId ?? string.Empty;
        public int RequiredValue => requiredValue;
        public bool Enabled => enabled;
        public CharacterUnlockConditionType Type => ParseType(conditionType);

        public static CharacterUnlockConditionType ParseType(string value)
        {
            if (string.Equals(value, "MAX_OWNED_CHARACTER_LEVEL_AT_LEAST", StringComparison.Ordinal))
                return CharacterUnlockConditionType.MaxOwnedCharacterLevelAtLeast;
            if (string.Equals(value, "OWNED_CHARACTER_COUNT_AT_LEAST", StringComparison.Ordinal))
                return CharacterUnlockConditionType.OwnedCharacterCountAtLeast;
            return CharacterUnlockConditionType.Unknown;
        }
    }

    [CreateAssetMenu(fileName = "CharacterUnlockConditionDefinition", menuName = "Recruitment/Character Unlock Condition Definition")]
    public sealed class CharacterUnlockConditionDefinition : ScriptableObject
    {
        [SerializeField] private string conditionId;
        [SerializeField] private List<CharacterUnlockConditionEntry> entries = new List<CharacterUnlockConditionEntry>();
        public string ConditionId => conditionId ?? string.Empty;
        public IReadOnlyList<CharacterUnlockConditionEntry> Entries => entries ?? (IReadOnlyList<CharacterUnlockConditionEntry>)Array.Empty<CharacterUnlockConditionEntry>();
        public bool IsValid => !string.IsNullOrEmpty(ConditionId);
    }
}
