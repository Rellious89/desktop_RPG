using System;
using System.Collections.Generic;
using Character;
using Common;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Quest
{
    /// <summary>독립 퀘스트 패널의 고정 파티 슬롯 카드. 표시와 입력만 소유하고 저장은 패널에 위임한다.</summary>
    [DisallowMultipleComponent]
    public sealed class CharacterQuestCardView : MonoBehaviour, IPointerClickHandler
    {
        [Header("Selection")]
        [SerializeField] private Image selectionImage;
        [SerializeField] private Sprite defaultSprite;
        [SerializeField] private Sprite selectedSprite;

        [Header("Character")]
        [SerializeField] private Image portrait;
        [SerializeField] private TMP_Text levelText;
        [SerializeField] private TMP_Text nameText;

        [Header("Quest")]
        [SerializeField] private TMP_Text questTitleText;
        [SerializeField] private TMP_Text objectiveLineTemplate;
        [SerializeField] private float objectiveLineSpacing = 16f;

        [Header("Reward")]
        [SerializeField] private GameObject rewardRoot;
        [SerializeField] private GameObject rewardCurrencyRoot;
        [SerializeField] private TMP_Text rewardCurrencyAmountText;
        [SerializeField] private Image rewardCurrencyIcon;
        [SerializeField] private Animator rewardCurrencyAnimator;
        [SerializeField] private GameObject rewardItemRoot;
        [SerializeField] private InventorySlotView rewardItemSlot;

        [Header("Complete")]
        [SerializeField] private Button completeButton;
        [SerializeField] private TMP_Text completeButtonText;

        private readonly List<TMP_Text> objectiveLines = new List<TMP_Text>();
        private readonly CharacterNameBinding nameBinding = new CharacterNameBinding();
        private LocalizedTextReference localizedTitle;
        private Action<string, string> completeRequested;
        private Action<string> selected;
        private string characterId;
        private string questId;

        public string CharacterId => characterId ?? string.Empty;
        public string QuestId => questId ?? string.Empty;
        public Button CompleteButton => completeButton;
        public Sprite DefaultSprite => defaultSprite;
        public Sprite SelectedSprite => selectedSprite;
        public bool HasRequiredReferences => selectionImage != null && defaultSprite != null && selectedSprite != null &&
                                             portrait != null && levelText != null && nameText != null &&
                                             questTitleText != null && objectiveLineTemplate != null &&
                                             rewardRoot != null && rewardCurrencyRoot != null && rewardCurrencyAmountText != null &&
                                             rewardItemRoot != null && rewardItemSlot != null &&
                                             completeButton != null && completeButtonText != null;

        public void Bind(
            CharacterDefinition character,
            int level,
            CharacterStoryQuestDefinition quest,
            IReadOnlyList<CharacterStoryQuestObjectiveDefinition> objectives,
            CharacterStoryQuestSnapshot snapshot,
            bool completing,
            Action<string> onSelected,
            Action<string, string> onComplete)
        {
            ClearBindings();
            characterId = character != null ? character.CharacterId : string.Empty;
            questId = quest != null ? quest.QuestId : string.Empty;
            selected = onSelected;
            completeRequested = onComplete;

            if (portrait != null)
            {
                portrait.sprite = character != null ? character.Portrait : null;
                portrait.enabled = portrait.sprite != null;
            }
            if (levelText != null) levelText.text = character != null ? "Lv. " + Mathf.Max(1, level) : string.Empty;
            nameBinding.Bind(character, value => { if (nameText != null) nameText.text = value ?? string.Empty; });

            BindQuestTitle(quest);
            RefreshObjectives(objectives, snapshot);
            RefreshRewards(quest);
            bool ready = quest != null && snapshot != null && snapshot.ReadyToComplete &&
                         string.Equals(snapshot.ActiveQuestId, quest.QuestId, StringComparison.Ordinal);
            if (completeButton != null)
            {
                completeButton.onClick.RemoveListener(RequestComplete);
                completeButton.onClick.AddListener(RequestComplete);
                completeButton.interactable = !completing && ready;
                completeButton.gameObject.SetActive(quest != null);
            }
            if (completeButtonText != null) completeButtonText.text = ready ? "퀘스트 완료" : "진행중";
        }

        public void SetSelected(bool value)
        {
            if (selectionImage != null) selectionImage.sprite = value ? selectedSprite : defaultSprite;
        }

        public void SetCompletionInputEnabled(bool enabled)
        {
            if (completeButton != null) completeButton.interactable = enabled && !string.IsNullOrEmpty(questId);
        }

        public void Clear()
        {
            ClearBindings();
            characterId = string.Empty;
            questId = string.Empty;
            if (portrait != null) { portrait.sprite = null; portrait.enabled = false; }
            if (levelText != null) levelText.text = string.Empty;
            if (nameText != null) nameText.text = string.Empty;
            if (questTitleText != null) questTitleText.text = string.Empty;
            SetLinesActive(0);
            ClearRewards();
            if (completeButton != null) { completeButton.interactable = false; completeButton.gameObject.SetActive(false); }
            SetSelected(false);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (!string.IsNullOrEmpty(characterId) && !string.IsNullOrEmpty(questId)) selected?.Invoke(characterId);
        }

        private void RequestComplete()
        {
            if (completeButton == null || !completeButton.interactable || string.IsNullOrEmpty(questId)) return;
            completeRequested?.Invoke(characterId, questId);
        }

        private void BindQuestTitle(CharacterStoryQuestDefinition quest)
        {
            if (questTitleText == null) return;
            questTitleText.text = quest != null ? quest.QuestId : string.Empty;
            if (quest == null || quest.LocalizedTitle == null || !quest.LocalizedTitle.HasReference) return;
            localizedTitle = quest.LocalizedTitle;
            localizedTitle.StringChanged += ApplyQuestTitle;
        }

        private void ApplyQuestTitle(string value)
        {
            if (questTitleText != null && !string.IsNullOrWhiteSpace(value)) questTitleText.text = value;
        }

        private void RefreshObjectives(IReadOnlyList<CharacterStoryQuestObjectiveDefinition> objectives,
            CharacterStoryQuestSnapshot snapshot)
        {
            int count = objectives != null ? objectives.Count : 0;
            for (int i = 0; i < count; i++)
            {
                TMP_Text line = GetOrCreateObjectiveLine(i);
                if (line != null) line.text = CharacterStoryQuestPresentation.ObjectiveText(objectives[i], snapshot);
            }
            SetLinesActive(count);
        }

        private TMP_Text GetOrCreateObjectiveLine(int index)
        {
            while (objectiveLines.Count <= index)
            {
                TMP_Text line = Instantiate(objectiveLineTemplate, objectiveLineTemplate.transform.parent);
                line.name = objectiveLineTemplate.name + "_Objective";
                if (line.TryGetComponent(out LocalizedTMPText localizer)) localizer.enabled = false;
                RectTransform rect = line.rectTransform;
                rect.anchoredPosition = objectiveLineTemplate.rectTransform.anchoredPosition +
                                        Vector2.down * objectiveLineSpacing * (objectiveLines.Count + 1);
                objectiveLines.Add(line);
            }
            TMP_Text result = objectiveLines[index];
            result.gameObject.SetActive(true);
            return result;
        }

        private void SetLinesActive(int count)
        {
            for (int i = 0; i < objectiveLines.Count; i++)
                objectiveLines[i].gameObject.SetActive(i < count);
        }

        private void RefreshRewards(CharacterStoryQuestDefinition quest)
        {
            CharacterStoryQuestRewardDefinition currency = null;
            CharacterStoryQuestRewardDefinition item = null;
            if (quest != null)
            {
                IReadOnlyList<CharacterStoryQuestRewardDefinition> rewards = quest.Rewards;
                for (int i = 0; i < rewards.Count; i++)
                {
                    CharacterStoryQuestRewardDefinition reward = rewards[i];
                    if (reward == null || !reward.IsValid) continue;
                    if (reward.RewardType == CharacterStoryQuestRewardType.Currency && currency == null) currency = reward;
                    if (reward.RewardType == CharacterStoryQuestRewardType.Item && item == null) item = reward;
                }
            }
            bool hasCurrency = currency != null;
            bool hasItem = item != null;
            SetActive(rewardRoot, hasCurrency || hasItem);
            SetActive(rewardCurrencyRoot, hasCurrency);
            SetActive(rewardItemRoot, hasItem);
            if (hasCurrency)
            {
                rewardCurrencyAmountText.text = currency.Amount.ToString();
                Sprite icon = currency.Currency != null ? currency.Currency.Icon : null;
                if (icon != null && rewardCurrencyIcon != null)
                {
                    if (rewardCurrencyAnimator != null) rewardCurrencyAnimator.enabled = false;
                    rewardCurrencyIcon.sprite = icon;
                    rewardCurrencyIcon.enabled = true;
                }
                else if (rewardCurrencyAnimator != null) rewardCurrencyAnimator.enabled = true;
            }
            if (hasItem) rewardItemSlot.SetItem(item.Item, item.Amount);
            else if (rewardItemSlot != null) rewardItemSlot.SetEmpty();
        }

        private void ClearRewards()
        {
            SetActive(rewardRoot, false);
            if (rewardItemSlot != null) rewardItemSlot.SetEmpty();
        }

        private void ClearBindings()
        {
            if (completeButton != null) completeButton.onClick.RemoveListener(RequestComplete);
            if (localizedTitle != null) localizedTitle.StringChanged -= ApplyQuestTitle;
            localizedTitle = null;
            nameBinding.Unbind();
            selected = null;
            completeRequested = null;
        }

        private void OnDisable() => ClearBindings();
        private void OnDestroy() => ClearBindings();
        private static void SetActive(GameObject target, bool value)
        {
            if (target != null && target.activeSelf != value) target.SetActive(value);
        }
    }
}
