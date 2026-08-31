using System;
using System.Collections.Generic;
using Common;
using Inventory;
using UnityEngine;

namespace Quest
{
    public enum CharacterStoryQuestRewardType
    {
        None,
        Currency,
        Item,
    }

    /// <summary>퀘스트 한 단계의 확정 보상 한 줄. 현재 저장 구조에서 재화는 jewel 한 종만
    /// 지급하지만, 표와 표시 계층은 재화 정의를 참조해 이후 확장 지점을 한곳에 둔다.</summary>
    [Serializable]
    public sealed class CharacterStoryQuestRewardDefinition
    {
        [SerializeField] private CharacterStoryQuestRewardType rewardType;
        [SerializeField] private CurrencyDefinition currency;
        [SerializeField] private ItemDefinition item;
        [SerializeField] private int amount;

        public CharacterStoryQuestRewardType RewardType => rewardType;
        public CurrencyDefinition Currency => currency;
        public ItemDefinition Item => item;
        public int Amount => amount;
        public bool IsValid => amount > 0 &&
            (rewardType == CharacterStoryQuestRewardType.Currency && currency != null ||
             rewardType == CharacterStoryQuestRewardType.Item && item != null);
    }

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
        [SerializeField] private List<CharacterStoryQuestRewardDefinition> rewards =
            new List<CharacterStoryQuestRewardDefinition>();

        public string QuestId => questId ?? string.Empty;
        public string CharacterId => characterId ?? string.Empty;
        public string PreviousQuestId => previousQuestId ?? string.Empty;
        public LocalizedTextReference LocalizedTitle => localizedTitle ?? (localizedTitle = new LocalizedTextReference());
        public LocalizedTextReference LocalizedDescription => localizedDescription ?? (localizedDescription = new LocalizedTextReference());
        public int DisplayOrder => displayOrder;
        public bool IsFinal => isFinal;
        public bool Enabled => enabled;
        public IReadOnlyList<CharacterStoryQuestRewardDefinition> Rewards =>
            rewards ?? (rewards = new List<CharacterStoryQuestRewardDefinition>());
        public bool IsValid => !string.IsNullOrWhiteSpace(QuestId) && !string.IsNullOrWhiteSpace(CharacterId);
    }
}
