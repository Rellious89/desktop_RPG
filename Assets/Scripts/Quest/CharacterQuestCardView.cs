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
        [SerializeField] private Sprite clearSprite;
        [SerializeField] private Sprite clearSelectedSprite;

        [Header("Character")]
        [SerializeField] private Image portrait;
        [SerializeField] private TMP_Text levelText;
        [SerializeField] private TMP_Text nameText;

        [Header("Quest")]
        [SerializeField] private TMP_Text questTitleText;
        [SerializeField] private TMP_Text allClearText;
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
        [SerializeField] private TMP_Text rewardItemAmountText;

        [Header("Complete")]
        [SerializeField] private Button completeButton;
        [SerializeField] private TMP_Text completeButtonText;

        private readonly List<TMP_Text> objectiveLines = new List<TMP_Text>();
        private readonly CharacterNameBinding nameBinding = new CharacterNameBinding();
        private Action<string, string> completeRequested;
        private Action<string> selected;
        private string characterId;
        private string questId;
        private bool allClear;

        public string CharacterId => characterId ?? string.Empty;
        public string QuestId => questId ?? string.Empty;
        public Button CompleteButton => completeButton;
        public Sprite DefaultSprite => defaultSprite;
        public Sprite SelectedSprite => selectedSprite;
        public Sprite ClearSprite => clearSprite;
        public Sprite ClearSelectedSprite => clearSelectedSprite;
        public bool IsAllClear => allClear;
        public bool HasRequiredReferences => selectionImage != null && defaultSprite != null && selectedSprite != null && clearSprite != null &&
                                             clearSelectedSprite != null &&
                                             portrait != null && levelText != null && nameText != null &&
                                             questTitleText != null && allClearText != null && objectiveLineTemplate != null &&
                                             rewardRoot != null && rewardCurrencyRoot != null && rewardCurrencyAmountText != null &&
                                             rewardItemRoot != null && rewardItemSlot != null && rewardItemAmountText != null &&
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
            allClear = quest == null && snapshot != null && snapshot.Graduated;

            if (portrait != null)
            {
                portrait.sprite = character != null ? character.Portrait : null;
                portrait.enabled = portrait.sprite != null;
            }
            if (levelText != null) levelText.text = character != null ? "Lv. " + Mathf.Max(1, level) : string.Empty;
            nameBinding.Bind(character, value => { if (nameText != null) nameText.text = value ?? string.Empty; });

            // lb_QuestName is intentionally not a card heading. The compact party card shows
            // only objective detail/progress; objectiveLineTemplate owns those runtime lines.
            SetActive(questTitleText != null ? questTitleText.gameObject : null, false);
            SetActive(allClearText != null ? allClearText.gameObject : null, allClear);
            if (allClear)
            {
                SetLinesActive(0);
                ClearRewards();
            }
            else
            {
                RefreshObjectives(objectives, snapshot);
                RefreshRewards(quest);
            }
            bool ready = quest != null && snapshot != null && snapshot.ReadyToComplete &&
                         string.Equals(snapshot.ActiveQuestId, quest.QuestId, StringComparison.Ordinal);
            if (completeButton != null)
            {
                completeButton.onClick.RemoveListener(RequestComplete);
                completeButton.onClick.AddListener(RequestComplete);
                completeButton.interactable = !completing && ready;
                completeButton.gameObject.SetActive(quest != null && !allClear);
            }
            if (completeButtonText != null) completeButtonText.text = ready ? "퀘스트 완료" : "진행중";
            SetSelected(false);
        }

        public void SetSelected(bool value)
        {
            if (selectionImage != null)
                selectionImage.sprite = allClear ? (value ? clearSelectedSprite : clearSprite) :
                    (value ? selectedSprite : defaultSprite);
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
            SetActive(questTitleText != null ? questTitleText.gameObject : null, false);
            SetActive(allClearText != null ? allClearText.gameObject : null, false);
            SetLinesActive(0);
            ClearRewards();
            if (completeButton != null) { completeButton.interactable = false; completeButton.gameObject.SetActive(false); }
            allClear = false;
            SetSelected(false);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (!string.IsNullOrEmpty(characterId) && (!string.IsNullOrEmpty(questId) || allClear)) selected?.Invoke(characterId);
        }

        private void RequestComplete()
        {
            if (completeButton == null || !completeButton.interactable || string.IsNullOrEmpty(questId)) return;
            completeRequested?.Invoke(characterId, questId);
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
            var distinctItemIds = new HashSet<string>(StringComparer.Ordinal);
            if (quest != null)
            {
                IReadOnlyList<CharacterStoryQuestRewardDefinition> rewards = quest.Rewards;
                for (int i = 0; i < rewards.Count; i++)
                {
                    CharacterStoryQuestRewardDefinition reward = rewards[i];
                    if (reward == null || !reward.IsValid) continue;
                    if (reward.RewardType == CharacterStoryQuestRewardType.Currency && currency == null) currency = reward;
                    if (reward.RewardType == CharacterStoryQuestRewardType.Item && reward.Item != null &&
                        !string.IsNullOrWhiteSpace(reward.Item.ItemId)) distinctItemIds.Add(reward.Item.ItemId);
                }
            }
            bool hasCurrency = currency != null;
            bool hasItem = distinctItemIds.Count > 0;
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
            // 이 카드는 보상 종류 수만 요약하고, 프리팹에 연결된 상징 아이콘을 그대로 보존한다.
            // InventorySlotView를 비활성화하면 실제 아이템 bind 및 hover tooltip 경로도 함께 차단된다.
            if (rewardItemSlot != null) rewardItemSlot.enabled = false;
            if (rewardItemAmountText != null) rewardItemAmountText.text = hasItem ? distinctItemIds.Count.ToString() : string.Empty;
        }

        private void ClearRewards()
        {
            SetActive(rewardRoot, false);
            if (rewardItemSlot != null) rewardItemSlot.enabled = false;
            if (rewardItemAmountText != null) rewardItemAmountText.text = string.Empty;
        }

        private void ClearBindings()
        {
            if (completeButton != null) completeButton.onClick.RemoveListener(RequestComplete);
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
