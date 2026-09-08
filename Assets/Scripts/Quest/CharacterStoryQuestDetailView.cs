using System;
using System.Collections.Generic;
using Common;
using Dungeon;
using TMPro;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.UI;

namespace Quest
{
    /// <summary>독립 pn_Quest의 Sub_Panel 전용 상세 뷰. 용병명부 페이지/전체목록 계약과 분리한다.</summary>
    [DisallowMultipleComponent]
    public sealed class CharacterStoryQuestDetailView : MonoBehaviour
    {
        private static readonly Color CompletedTextColor = new Color32(0x95, 0x95, 0x95, 0xFF);

        [Header("Detail State")]
        [SerializeField] private GameObject currentRoot;
        [SerializeField] private TMP_Text allClearText;

        [SerializeField] private TMP_Text questTitleText;
        [SerializeField] private TMP_Text questDescriptionText;
        [SerializeField] private TMP_Text objectiveTypeLineTemplate;
        [SerializeField] private TMP_Text objectiveDescriptionLineTemplate;
        [SerializeField] private Slider progressSlider;
        [SerializeField] private TMP_Text progressPercentText;
        [SerializeField] private ScrollRect objectiveScroll;

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

        [Header("Objective Localization")]
        [SerializeField] private MonsterCatalog monsterCatalog;
        [SerializeField] private DungeonCatalog dungeonCatalog;

        private readonly List<TMP_Text> typeLines = new List<TMP_Text>();
        private readonly List<TMP_Text> descriptionLines = new List<TMP_Text>();
        private LocalizedTextReference localizedTitle;
        private LocalizedTextReference localizedDescription;
        private readonly Dictionary<int, string> localizedObjectiveTexts = new Dictionary<int, string>();
        private readonly Dictionary<LocalizedTextReference, LocalizedString.ChangeHandler> objectiveLocalizationHandlers =
            new Dictionary<LocalizedTextReference, LocalizedString.ChangeHandler>();
        // The independent panel has its own typography.  Remember each text's prefab color so a
        // completed-detail selection never leaves a later active quest grey.
        private readonly Dictionary<TMP_Text, Color> detailTextDefaultColors = new Dictionary<TMP_Text, Color>();
        private IReadOnlyList<CharacterStoryQuestObjectiveDefinition> boundObjectives;
        private CharacterStoryQuestSnapshot boundSnapshot;
        private Action<string, string> completeRequested;
        private string characterId;
        private string questId;

        public string CharacterId => characterId ?? string.Empty;
        public string QuestId => questId ?? string.Empty;
        public Button CompleteButton => completeButton;
        public InventorySlotView RewardItemSlot => rewardItemSlot;
        public bool IsAllClear => allClearText != null && allClearText.gameObject.activeSelf;
        public bool HasRequiredReferences => currentRoot != null && allClearText != null &&
                                             questTitleText != null && questDescriptionText != null &&
                                             objectiveTypeLineTemplate != null && objectiveDescriptionLineTemplate != null &&
                                             progressSlider != null && progressPercentText != null && objectiveScroll != null &&
                                             monsterCatalog != null && dungeonCatalog != null &&
                                             rewardRoot != null && rewardCurrencyRoot != null && rewardCurrencyAmountText != null &&
                                             rewardItemRoot != null && rewardItemSlot != null &&
                                             completeButton != null && completeButtonText != null;

        public void Bind(
            string selectedCharacterId,
            CharacterStoryQuestDefinition quest,
            IReadOnlyList<CharacterStoryQuestObjectiveDefinition> objectives,
            CharacterStoryQuestSnapshot snapshot,
            bool completing,
            Action<string, string> onComplete)
        {
            ClearBindings();
            characterId = selectedCharacterId ?? string.Empty;
            questId = quest != null ? quest.QuestId : string.Empty;
            completeRequested = onComplete;
            boundObjectives = objectives;
            boundSnapshot = snapshot;
            // 졸업한 캐릭터도 독립 패널에서는 마지막 서사 퀘스트를 완료 상태로 다시 보여 준다.
            // 이 뷰는 pn_Quest 전용이므로, 용병명부 QuestInfo의 과거 퀘스트 선택 계약에는 관여하지 않는다.
            bool allClear = snapshot != null && snapshot.Graduated;
            SetActive(currentRoot, quest != null);
            SetActive(allClearText != null ? allClearText.gameObject : null, allClear);
            if (quest == null)
            {
                ClearVisuals();
                return;
            }

            BindLocalizedText(quest.LocalizedTitle, questTitleText, quest.QuestId, ApplyTitle, out localizedTitle);
            BindLocalizedText(quest.LocalizedDescription, questDescriptionText, string.Empty, ApplyDescription, out localizedDescription);
            RefreshObjectives(objectives, snapshot);
            float progress = CharacterStoryQuestPresentation.OverallProgress(objectives, snapshot);
            progressSlider.normalizedValue = progress;
            progressPercentText.text = Mathf.RoundToInt(progress * 100f) + "%";
            RefreshRewards(quest);

            bool ready = snapshot != null && snapshot.ReadyToComplete &&
                         string.Equals(snapshot.ActiveQuestId, quest.QuestId, StringComparison.Ordinal);
            completeButton.onClick.RemoveListener(RequestComplete);
            completeButton.onClick.AddListener(RequestComplete);
            completeButton.interactable = !allClear && !completing && ready;
            completeButton.gameObject.SetActive(!allClear);
            completeButtonText.text = ready ? "퀘스트 완료" : "진행중";
            ApplyCompletionTextColor(allClear || IsCompleted(snapshot, quest.QuestId));
            RefreshLayout();
        }

        public void SetCompletionInputEnabled(bool enabled)
        {
            if (completeButton != null)
                completeButton.interactable = enabled && !IsAllClear && !string.IsNullOrEmpty(questId);
        }

        public void Clear()
        {
            ClearBindings();
            characterId = string.Empty;
            questId = string.Empty;
            SetActive(currentRoot, false);
            SetActive(allClearText != null ? allClearText.gameObject : null, false);
            ClearVisuals();
        }

        private void ClearVisuals()
        {
            if (questTitleText != null) questTitleText.text = string.Empty;
            if (questDescriptionText != null) questDescriptionText.text = string.Empty;
            SetLinesActive(typeLines, 0);
            SetLinesActive(descriptionLines, 0);
            if (progressSlider != null) progressSlider.normalizedValue = 0f;
            if (progressPercentText != null) progressPercentText.text = "0%";
            SetActive(rewardRoot, false);
            if (rewardItemSlot != null) rewardItemSlot.SetEmpty();
            if (completeButton != null) { completeButton.interactable = false; completeButton.gameObject.SetActive(false); }
            boundObjectives = null;
            boundSnapshot = null;
        }

        private void RefreshObjectives(IReadOnlyList<CharacterStoryQuestObjectiveDefinition> objectives,
            CharacterStoryQuestSnapshot snapshot)
        {
            boundObjectives = objectives;
            boundSnapshot = snapshot;
            BindObjectiveLocalization(objectives);
            RenderObjectives(objectives, snapshot);
        }

        private void RenderObjectives(IReadOnlyList<CharacterStoryQuestObjectiveDefinition> objectives,
            CharacterStoryQuestSnapshot snapshot)
        {
            int count = objectives != null ? objectives.Count : 0;
            for (int i = 0; i < count; i++)
            {
                TMP_Text type = GetOrCreateLine(objectiveTypeLineTemplate, typeLines, i);
                TMP_Text description = GetOrCreateLine(objectiveDescriptionLineTemplate, descriptionLines, i);
                if (type != null) type.text = CharacterStoryQuestPresentation.ConditionTitle(objectives[i].ConditionType, QuestText);
                if (description != null) description.text = CharacterStoryQuestPresentation.ObjectiveText(
                    objectives[i], snapshot, monsterCatalog, dungeonCatalog, QuestText);
            }
            SetLinesActive(typeLines, count);
            SetLinesActive(descriptionLines, count);
        }

        private void ApplyCompletionTextColor(bool completed)
        {
            ApplyDetailTextColor(questTitleText, completed);
            ApplyDetailTextColor(questDescriptionText, completed);
            ApplyDetailTextColor(progressPercentText, completed);
            for (int i = 0; i < typeLines.Count; i++) ApplyDetailTextColor(typeLines[i], completed);
            for (int i = 0; i < descriptionLines.Count; i++) ApplyDetailTextColor(descriptionLines[i], completed);
        }

        private void ApplyDetailTextColor(TMP_Text text, bool completed)
        {
            if (text == null) return;
            if (!detailTextDefaultColors.TryGetValue(text, out Color defaultColor))
            {
                defaultColor = text.color;
                detailTextDefaultColors.Add(text, defaultColor);
            }
            text.color = completed ? CompletedTextColor : defaultColor;
        }

        private static bool IsCompleted(CharacterStoryQuestSnapshot snapshot, string id)
        {
            if (snapshot?.CompletedQuestIds == null || string.IsNullOrEmpty(id)) return false;
            for (int i = 0; i < snapshot.CompletedQuestIds.Count; i++)
                if (string.Equals(snapshot.CompletedQuestIds[i], id, StringComparison.Ordinal)) return true;
            return false;
        }

        private static TMP_Text GetOrCreateLine(TMP_Text template, List<TMP_Text> pool, int index)
        {
            while (pool.Count <= index)
            {
                TMP_Text line = Instantiate(template, template.transform.parent);
                line.name = template.name + "_Runtime";
                if (line.TryGetComponent(out LocalizedTMPText localizer)) localizer.enabled = false;
                pool.Add(line);
            }
            TMP_Text result = pool[index];
            result.gameObject.SetActive(true);
            return result;
        }

        private static void SetLinesActive(List<TMP_Text> lines, int activeCount)
        {
            for (int i = 0; i < lines.Count; i++) lines[i].gameObject.SetActive(i < activeCount);
        }

        private void RefreshRewards(CharacterStoryQuestDefinition quest)
        {
            CharacterStoryQuestRewardDefinition currency = null;
            CharacterStoryQuestRewardDefinition item = null;
            IReadOnlyList<CharacterStoryQuestRewardDefinition> rewards = quest.Rewards;
            for (int i = 0; i < rewards.Count; i++)
            {
                CharacterStoryQuestRewardDefinition reward = rewards[i];
                if (reward == null || !reward.IsValid) continue;
                if (reward.RewardType == CharacterStoryQuestRewardType.Currency && currency == null) currency = reward;
                if (reward.RewardType == CharacterStoryQuestRewardType.Item && item == null) item = reward;
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
            else rewardItemSlot.SetEmpty();
        }

        private void RequestComplete()
        {
            if (completeButton == null || !completeButton.interactable || string.IsNullOrEmpty(questId)) return;
            completeRequested?.Invoke(characterId, questId);
        }

        private static void BindLocalizedText(LocalizedTextReference reference, TMP_Text target, string fallback,
            LocalizedString.ChangeHandler handler, out LocalizedTextReference bound)
        {
            bound = null;
            if (target != null) target.text = fallback ?? string.Empty;
            if (reference == null || !reference.HasReference) return;
            bound = reference;
            reference.StringChanged += handler;
        }

        private void ApplyTitle(string value)
        {
            if (questTitleText != null && !string.IsNullOrWhiteSpace(value)) questTitleText.text = value;
        }

        private void ApplyDescription(string value)
        {
            if (questDescriptionText != null && !string.IsNullOrWhiteSpace(value)) questDescriptionText.text = value;
        }

        private void ClearBindings()
        {
            if (completeButton != null) completeButton.onClick.RemoveListener(RequestComplete);
            if (localizedTitle != null) localizedTitle.StringChanged -= ApplyTitle;
            if (localizedDescription != null) localizedDescription.StringChanged -= ApplyDescription;
            localizedTitle = null;
            localizedDescription = null;
            foreach (KeyValuePair<LocalizedTextReference, LocalizedString.ChangeHandler> pair in objectiveLocalizationHandlers)
                pair.Key.StringChanged -= pair.Value;
            objectiveLocalizationHandlers.Clear();
            localizedObjectiveTexts.Clear();
            completeRequested = null;
        }

        private void BindObjectiveLocalization(IReadOnlyList<CharacterStoryQuestObjectiveDefinition> objectives)
        {
            foreach (LocalizedTextReference reference in CharacterStoryQuestPresentation.ObjectiveTextReferences(
                objectives, monsterCatalog, dungeonCatalog))
            {
                if (reference == null || !reference.HasReference || objectiveLocalizationHandlers.ContainsKey(reference)) continue;
                LocalizedString.ChangeHandler handler = value =>
                {
                    int key = ReferenceKey(reference);
                    if (key != 0)
                    {
                        if (IsUsableLocalizedValue(value, key)) localizedObjectiveTexts[key] = value;
                        else localizedObjectiveTexts.Remove(key);
                    }
                    if (boundObjectives != null)
                    {
                        RenderObjectives(boundObjectives, boundSnapshot);
                        RefreshLayout();
                    }
                };
                objectiveLocalizationHandlers.Add(reference, handler);
                reference.StringChanged += handler;
            }
        }

        private string QuestText(int key) => localizedObjectiveTexts.TryGetValue(key, out string value) ? value : null;

        private static int ReferenceKey(LocalizedTextReference reference)
        {
            if (reference == null || !int.TryParse(reference.TableEntryReference.Key, out int key)) return 0;
            foreach (int candidate in CharacterStoryQuestPresentation.ObjectiveLocalizationKeys)
                if (candidate == key) return key;
            return 0;
        }

        private static bool IsUsableLocalizedValue(string value, int key) => !string.IsNullOrWhiteSpace(value) &&
            !string.Equals(value, key.ToString(), StringComparison.Ordinal) && !value.StartsWith("No translation found", StringComparison.Ordinal);

        private void RefreshLayout()
        {
            if (objectiveScroll == null || objectiveScroll.content == null) return;
            LayoutRebuilder.ForceRebuildLayoutImmediate(objectiveScroll.content);
        }

        private void OnDisable() => ClearBindings();
        private void OnDestroy() => ClearBindings();
        private static void SetActive(GameObject target, bool value)
        {
            if (target != null && target.activeSelf != value) target.SetActive(value);
        }
    }
}
