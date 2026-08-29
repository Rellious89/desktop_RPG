using System;
using System.Collections.Generic;
using UnityEngine;

namespace Recruitment
{
    [CreateAssetMenu(fileName = "CharacterUnlockConditionCatalog", menuName = "Recruitment/Character Unlock Condition Catalog")]
    public sealed class CharacterUnlockConditionCatalog : ScriptableObject
    {
        [SerializeField] private List<CharacterUnlockConditionDefinition> conditions = new List<CharacterUnlockConditionDefinition>();
        private readonly Dictionary<string, CharacterUnlockConditionDefinition> byId = new Dictionary<string, CharacterUnlockConditionDefinition>(StringComparer.Ordinal);
        private bool built;
        public CharacterUnlockConditionDefinition Find(string conditionId)
        {
            if (string.IsNullOrEmpty(conditionId)) return null;
            EnsureBuilt(); byId.TryGetValue(conditionId, out CharacterUnlockConditionDefinition value); return value;
        }
        public void MarkDirty() => built = false;
        private void OnEnable() => built = false;
        private void EnsureBuilt()
        {
            if (built) return; built = true; byId.Clear();
            if (conditions == null) return;
            foreach (var condition in conditions)
                if (condition != null && condition.IsValid && !byId.ContainsKey(condition.ConditionId)) byId.Add(condition.ConditionId, condition);
        }
    }
}
