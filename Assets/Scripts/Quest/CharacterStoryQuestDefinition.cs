using Common;
using UnityEngine;

namespace Quest
{
    [CreateAssetMenu(fileName = "CharacterStoryQuestDefinition", menuName = "Quest/Character Story Quest Definition")]
    public sealed class CharacterStoryQuestDefinition : ScriptableObject
    {
        [SerializeField] private string questId;
        [SerializeField] private string characterId;
        [SerializeField] private string previousQuestId;
        [SerializeField] private LocalizedTextReference localizedTitle = new LocalizedTextReference();
        [SerializeField] private LocalizedTextReference localizedDescription = new LocalizedTextReference();
        [SerializeField] private int displayOrder;
        [SerializeField] private bool isFinal;
        [SerializeField] private bool enabled;

        public string QuestId => questId ?? string.Empty;
        public string CharacterId => characterId ?? string.Empty;
        public string PreviousQuestId => previousQuestId ?? string.Empty;
        public LocalizedTextReference LocalizedTitle => localizedTitle ?? (localizedTitle = new LocalizedTextReference());
        public LocalizedTextReference LocalizedDescription => localizedDescription ?? (localizedDescription = new LocalizedTextReference());
        public int DisplayOrder => displayOrder;
        public bool IsFinal => isFinal;
        public bool Enabled => enabled;
        public bool IsValid => !string.IsNullOrWhiteSpace(QuestId) && !string.IsNullOrWhiteSpace(CharacterId);
    }
}
