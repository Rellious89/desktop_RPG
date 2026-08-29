using System;
using System.Collections.Generic;
using UnityEngine;

namespace Quest
{
    [CreateAssetMenu(fileName = "CharacterStoryQuestCatalog", menuName = "Quest/Character Story Quest Catalog")]
    public sealed class CharacterStoryQuestCatalog : ScriptableObject
    {
        [SerializeField] private List<CharacterStoryQuestDefinition> quests = new List<CharacterStoryQuestDefinition>();
        private readonly List<CharacterStoryQuestDefinition> valid = new List<CharacterStoryQuestDefinition>();
        private bool built;
        public IReadOnlyList<CharacterStoryQuestDefinition> Quests { get { Build(); return valid; } }
        public CharacterStoryQuestDefinition Find(string id)
        {
            Build(); foreach (var quest in valid) if (string.Equals(quest.QuestId, id, StringComparison.Ordinal)) return quest; return null;
        }
        public CharacterStoryQuestDefinition FindRoot(string characterId)
        {
            Build(); foreach (var quest in valid) if (string.Equals(quest.CharacterId, characterId, StringComparison.Ordinal) && string.IsNullOrEmpty(quest.PreviousQuestId)) return quest; return null;
        }
        public CharacterStoryQuestDefinition FindNext(string questId)
        {
            Build(); foreach (var quest in valid) if (string.Equals(quest.PreviousQuestId, questId, StringComparison.Ordinal)) return quest; return null;
        }
        public void MarkDirty() => built = false;
        private void OnEnable() => built = false;
        private void Build()
        {
            if (built) return; built = true; valid.Clear(); var ids = new HashSet<string>(StringComparer.Ordinal);
            if (quests == null) return;
            foreach (var quest in quests) if (quest != null && quest.IsValid && ids.Add(quest.QuestId)) valid.Add(quest);
        }
    }
}
