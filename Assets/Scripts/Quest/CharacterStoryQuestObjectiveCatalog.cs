using System;
using System.Collections.Generic;
using UnityEngine;

namespace Quest
{
    [CreateAssetMenu(fileName = "CharacterStoryQuestObjectiveCatalog", menuName = "Quest/Character Story Quest Objective Catalog")]
    public sealed class CharacterStoryQuestObjectiveCatalog : ScriptableObject
    {
        [SerializeField] private List<CharacterStoryQuestObjectiveDefinition> objectives = new List<CharacterStoryQuestObjectiveDefinition>();
        private readonly List<CharacterStoryQuestObjectiveDefinition> valid = new List<CharacterStoryQuestObjectiveDefinition>();
        private bool built;
        public IReadOnlyList<CharacterStoryQuestObjectiveDefinition> Objectives { get { Build(); return valid; } }
        public List<CharacterStoryQuestObjectiveDefinition> ForQuest(string questId)
        {
            Build(); var result = new List<CharacterStoryQuestObjectiveDefinition>();
            foreach (var objective in valid) if (string.Equals(objective.QuestId, questId, StringComparison.Ordinal)) result.Add(objective);
            result.Sort((a, b) => a.DisplayOrder != b.DisplayOrder ? a.DisplayOrder.CompareTo(b.DisplayOrder) : string.CompareOrdinal(a.ObjectiveId, b.ObjectiveId));
            return result;
        }
        public void MarkDirty() => built = false;
        private void OnEnable() => built = false;
        private void Build()
        {
            if (built) return; built = true; valid.Clear(); var ids = new HashSet<string>(StringComparer.Ordinal);
            if (objectives == null) return;
            foreach (var objective in objectives) if (objective != null && objective.IsValid && ids.Add(objective.ObjectiveId)) valid.Add(objective);
        }
    }
}
