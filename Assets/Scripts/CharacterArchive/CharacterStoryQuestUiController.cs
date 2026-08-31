using System;
using System.Collections.Generic;
using Character;
using Common;
using Dungeon;
using Inventory;
using Quest;
using TMPro;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Tables;
using UnityEngine.UI;

namespace CharacterArchive
{
    /// <summary>캐릭터 명부 안에서 서사 퀘스트 상태를 읽어 표시한다. 저장 상태를 직접 바꾸지 않고,
    /// 완료는 CharacterStoryQuestService의 명시적 관문으로만 요청한다.</summary>
    [DisallowMultipleComponent]
    public sealed class CharacterStoryQuestUiController : MonoBehaviour
    {
        public enum RightPage { CharacterInfo, QuestInfo }

        private const string QuestTableGuid = "11805744adb144cd3bb37f325635e0d9";
        private const string UiTableGuid = "32fd067a20b754a50b20446b9c78d2ae";
        private const int TotalProgressFormatKey = 87;
        private const int CompleteButtonReadyKey = 91;
        private const int CompleteButtonInProgressKey = 93;
        private static readonly int[] QuestLocalizationKeys = { 1, 2, 3, 4, 10001, 10002, 10003, 10004, 100002, 100004 };

        [Header("Catalogs (Inspector에서만 연결)")]
        [SerializeField] private CharacterStoryQuestCatalog questCatalog;
        [SerializeField] private CharacterStoryQuestObjectiveCatalog objectiveCatalog;
        [SerializeField] private MonsterCatalog monsterCatalog;
        [SerializeField] private DungeonCatalog dungeonCatalog;

        [Header("Pages")]
        [SerializeField] private GameObject characterInfoPage;
        [SerializeField] private GameObject questInfoPage;
        [SerializeField] private Button swapButton;
        [SerializeField] private Button closeButton;
        [SerializeField] private RightPage defaultRightPage = RightPage.CharacterInfo;

        [Header("Quest UI")]
        [SerializeField] private Slider currentProgressSlider;
        [SerializeField] private Slider totalProgressSlider;
        [SerializeField] private TMP_Text currentProgressPercentText;
        [SerializeField] private TMP_Text totalProgressPercentText;
        [SerializeField] private TMP_Text totalProgressText;
        [SerializeField] private TMP_Text questTypeTitle;
        [SerializeField] private TMP_Text questDescriptionTitle;
        [SerializeField] private TMP_Text questTypeLineTemplate;
        [SerializeField] private TMP_Text questDescriptionLineTemplate;
        [SerializeField] private Button completeButton;
        [SerializeField] private TMP_Text completeButtonText;
        [SerializeField] private ScrollRect objectiveScroll;

        [Header("Quest Reward UI")]
        [SerializeField] private GameObject rewardRoot;
        [SerializeField] private GameObject rewardCurrencyRoot;
        [SerializeField] private TMP_Text rewardCurrencyAmountText;
        [SerializeField] private Image rewardCurrencyIcon;
        [SerializeField] private Animator rewardCurrencyAnimator;
        [SerializeField] private GameObject rewardItemRoot;
        [SerializeField] private InventorySlotView rewardItemSlot;

        private readonly List<TMP_Text> typeLines = new List<TMP_Text>();
        private readonly List<TMP_Text> descriptionLines = new List<TMP_Text>();
        // 퀘스트 표 문구는 이 컨트롤러의 수명 동안 하나의 참조만 유지한다. Refresh마다 새
        // LocalizedString을 동기 조회하면 테이블 로드 전에는 Entry key가 화면에 보일 수 있다.
        private readonly Dictionary<int, LocalizedTextReference> questTextReferences = new Dictionary<int, LocalizedTextReference>();
        private readonly Dictionary<int, string> localizedQuestTexts = new Dictionary<int, string>();
        private readonly Dictionary<LocalizedTextReference, LocalizedString.ChangeHandler> localizationHandlers = new Dictionary<LocalizedTextReference, LocalizedString.ChangeHandler>();
        private LocalizedTextReference totalProgressFormatReference;
        private string totalProgressFormat;
        private LocalizedTextReference completeButtonReadyReference;
        private LocalizedTextReference completeButtonInProgressReference;
        private string completeButtonReadyText;
        private string completeButtonInProgressText;
        private CharacterDefinition selected;
        private bool completionRequested;
        private bool subscribed;
        private bool refreshing;
        private bool refreshQueued;
        private string displayedCharacterId;
        private string displayedQuestId;

        public event Action CloseRequested;

        public bool HasRequiredReferences => questCatalog != null && objectiveCatalog != null &&
                                             monsterCatalog != null && dungeonCatalog != null &&
                                             characterInfoPage != null && questInfoPage != null &&
                                             swapButton != null && closeButton != null && completeButton != null &&
                                             currentProgressSlider != null && totalProgressSlider != null &&
                                             currentProgressPercentText != null && totalProgressPercentText != null && totalProgressText != null &&
                                             questTypeLineTemplate != null && questDescriptionLineTemplate != null && completeButtonText != null &&
                                             rewardRoot != null && rewardCurrencyRoot != null && rewardCurrencyAmountText != null &&
                                             rewardItemRoot != null && rewardItemSlot != null;

        public void OpenFor(CharacterDefinition definition)
        {
            EnsureInitialized();
            selected = definition;
            completionRequested = false;
            ShowPage(defaultRightPage);
            Refresh();
        }

        public void BindCharacter(CharacterDefinition definition)
        {
            EnsureInitialized();
            selected = definition;
            completionRequested = false;
            Refresh();
        }

        public void Close()
        {
            selected = null;
            completionRequested = false;
            SetLinesActive(typeLines, 0); SetLinesActive(descriptionLines, 0);
            ClearRewardView();
            displayedCharacterId = null;
            displayedQuestId = null;
            TearDown();
        }

        private void OnEnable()
        {
            EnsureInitialized();
        }

        private void OnDestroy()
        {
            TearDown();
        }

        // QuestInfo 자체가 비활성 페이지이므로, OnDisable은 페이지 전환마다 호출된다. 이때
        // btn_swap/로컬라이즈 구독을 끊으면 CharacterInfo에서 다시 QuestInfo로 갈 수 없다.
        private void EnsureInitialized()
        {
            BindButtons();
            SubscribeLocalization();
            DisableDynamicLocalizer(totalProgressText);
            DisableDynamicLocalizer(completeButtonText);
        }

        private void TearDown()
        {
            UnbindButtons();
            UnsubscribeLocalization();
        }

        private void BindButtons()
        {
            if (swapButton != null) { swapButton.onClick.RemoveListener(TogglePage); swapButton.onClick.AddListener(TogglePage); }
            if (closeButton != null) { closeButton.onClick.RemoveListener(RequestClose); closeButton.onClick.AddListener(RequestClose); }
            if (completeButton != null) { completeButton.onClick.RemoveListener(ConfirmComplete); completeButton.onClick.AddListener(ConfirmComplete); }
        }

        private void UnbindButtons()
        {
            if (swapButton != null) swapButton.onClick.RemoveListener(TogglePage);
            if (closeButton != null) closeButton.onClick.RemoveListener(RequestClose);
            if (completeButton != null) completeButton.onClick.RemoveListener(ConfirmComplete);
        }

        private void SubscribeLocalization()
        {
            if (subscribed) return;
            foreach (int key in QuestLocalizationKeys) AddQuestLocalization(key);
            totalProgressFormatReference = CreateLocalizedReference(UiTableGuid, TotalProgressFormatKey);
            AddLocalization(totalProgressFormatReference, value =>
            {
                totalProgressFormat = IsUsableLocalizedValue(value, TotalProgressFormatKey.ToString()) ? value : null;
                Refresh();
            });
            completeButtonReadyReference = CreateLocalizedReference(UiTableGuid, CompleteButtonReadyKey);
            AddLocalization(completeButtonReadyReference, value =>
            {
                completeButtonReadyText = IsUsableLocalizedValue(value, CompleteButtonReadyKey.ToString()) ? value : null;
                Refresh();
            });
            completeButtonInProgressReference = CreateLocalizedReference(UiTableGuid, CompleteButtonInProgressKey);
            AddLocalization(completeButtonInProgressReference, value =>
            {
                completeButtonInProgressText = IsUsableLocalizedValue(value, CompleteButtonInProgressKey.ToString()) ? value : null;
                Refresh();
            });
            subscribed = true;
        }

        private void AddQuestLocalization(int key)
        {
            var reference = CreateLocalizedReference(QuestTableGuid, key);
            questTextReferences.Add(key, reference);
            AddLocalization(reference, value =>
            {
                if (IsUsableLocalizedValue(value, key.ToString())) localizedQuestTexts[key] = value;
                else localizedQuestTexts.Remove(key);
                Refresh();
            });
        }

        private void AddLocalization(LocalizedTextReference reference, LocalizedString.ChangeHandler handler)
        {
            if (reference == null || !reference.HasReference || localizationHandlers.ContainsKey(reference)) return;
            localizationHandlers.Add(reference, handler);
            reference.StringChanged += handler;
        }

        // TableReference의 string 암시 변환은 "GUID:..."를 테이블 이름으로 취급한다.
        // 런타임 동적 참조는 Guid를 명시해 Addressables의 SharedTableData GUID를 사용한다.
        private static LocalizedTextReference CreateLocalizedReference(string tableGuid, int key)
        {
            return new LocalizedTextReference((TableReference)new Guid(tableGuid), key.ToString());
        }

        private void BindObjectiveTargetLocalization(IReadOnlyList<CharacterStoryQuestObjectiveDefinition> objectives)
        {
            if (objectives == null) return;
            for (int i = 0; i < objectives.Count; i++)
            {
                CharacterStoryQuestObjectiveDefinition objective = objectives[i];
                if (objective == null || objective.TargetIds == null) continue;
                bool monster = objective.ConditionType == CharacterStoryQuestConditionType.MonsterDefeatCount;
                bool dungeon = objective.ConditionType == CharacterStoryQuestConditionType.DungeonEnterCount;
                if (!monster && !dungeon) continue;
                foreach (string id in objective.TargetIds)
                {
                    LocalizedTextReference reference = monster ? MonsterLocalizedName(id) : DungeonLocalizedName(id);
                    AddLocalization(reference, HandleLocaleChanged);
                }
            }
        }

        private void UnsubscribeLocalization()
        {
            foreach (KeyValuePair<LocalizedTextReference, LocalizedString.ChangeHandler> pair in localizationHandlers)
                pair.Key.StringChanged -= pair.Value;
            localizationHandlers.Clear();
            questTextReferences.Clear();
            localizedQuestTexts.Clear();
            totalProgressFormatReference = null;
            totalProgressFormat = null;
            completeButtonReadyReference = null;
            completeButtonInProgressReference = null;
            completeButtonReadyText = null;
            completeButtonInProgressText = null;
            subscribed = false;
        }

        private void HandleLocaleChanged(string _) => Refresh();

        private void TogglePage() => ShowPage(questInfoPage != null && questInfoPage.activeSelf ? RightPage.CharacterInfo : RightPage.QuestInfo);

        private void RequestClose() => CloseRequested?.Invoke();

        private void ShowPage(RightPage page)
        {
            SetActive(characterInfoPage, page == RightPage.CharacterInfo);
            SetActive(questInfoPage, page == RightPage.QuestInfo);
            if (page == RightPage.QuestInfo) RefreshObjectiveLayout(false);
        }

        private void ConfirmComplete()
        {
            if (completionRequested || selected == null || completeButton == null || !completeButton.interactable) return;
            CharacterStoryQuestService service = CharacterStoryQuestService.Instance;
            if (service == null) return;
            completionRequested = true;
            bool completed = service.TryConfirmComplete(selected.CharacterId);
            completionRequested = false;
            if (completed) Refresh();
        }

        public void Refresh()
        {
            // StringChanged can arrive more than once while a table is being resolved.  Rendering
            // recursively would be harmless to the data, but makes the UI work needlessly harder
            // to reason about; finish the current pass, then synchronously apply one final state.
            if (refreshing)
            {
                refreshQueued = true;
                return;
            }

            do
            {
                refreshQueued = false;
                refreshing = true;
                try { RefreshImmediate(); }
                finally { refreshing = false; }
            }
            while (refreshQueued);
        }

        private void RefreshImmediate()
        {
            if (!HasRequiredReferences)
            {
                if (completeButton != null) completeButton.interactable = false;
                return;
            }

            CharacterStoryQuestService service = CharacterStoryQuestService.Instance;
            CharacterStoryQuestSnapshot snapshot = service != null && selected != null
                ? service.GetSnapshot(selected.CharacterId) : CharacterStoryQuestSnapshot.Empty(selected != null ? selected.CharacterId : string.Empty);
            CharacterStoryQuestDefinition active = !string.IsNullOrEmpty(snapshot.ActiveQuestId) ? questCatalog.Find(snapshot.ActiveQuestId) : null;
            List<CharacterStoryQuestObjectiveDefinition> objectives = active != null ? EnabledObjectives(active.QuestId) : new List<CharacterStoryQuestObjectiveDefinition>();

            float current = CalculateCurrentProgress(objectives, snapshot);
            float total = CalculateTotalProgress(questCatalog, selected != null ? selected.CharacterId : string.Empty, snapshot, out int currentNumber, out int completedCount, out int totalCount);
            SetSliderProgress(currentProgressSlider, current);
            SetSliderProgress(totalProgressSlider, total);
            if (currentProgressPercentText != null) currentProgressPercentText.text = FormatProgressPercent(current);
            if (totalProgressPercentText != null) totalProgressPercentText.text = FormatProgressPercent(total);
            if (totalProgressText != null) totalProgressText.text = SafeFormat(totalProgressFormat, "{0}번 퀘스트 진행 중 ({1}/{2})", currentNumber, completedCount, totalCount);

            BindObjectiveTargetLocalization(objectives);
            UpdateObjectiveLines(objectives, snapshot);

            SetActive(questTypeTitle != null ? questTypeTitle.gameObject : null, objectives.Count > 0);
            SetActive(questDescriptionTitle != null ? questDescriptionTitle.gameObject : null, objectives.Count > 0);
            RefreshRewards(active);
            bool readyToComplete = active != null && snapshot.ReadyToComplete;
            if (completeButton != null) completeButton.interactable = !completionRequested && readyToComplete;
            if (completeButtonText != null)
                completeButtonText.text = readyToComplete
                    ? TextOrFallback(completeButtonReadyText, "퀘스트 완료")
                    : TextOrFallback(completeButtonInProgressText, "진행중");

            string characterId = selected != null ? selected.CharacterId : null;
            string questId = active != null ? active.QuestId : null;
            bool selectionChanged = !string.Equals(displayedCharacterId, characterId, StringComparison.Ordinal) ||
                                  !string.Equals(displayedQuestId, questId, StringComparison.Ordinal);
            RefreshObjectiveLayout(selectionChanged);
            displayedCharacterId = characterId;
            displayedQuestId = questId;
        }

        private void RefreshRewards(CharacterStoryQuestDefinition quest)
        {
            CharacterStoryQuestRewardDefinition currency = null;
            CharacterStoryQuestRewardDefinition item = null;
            if (quest != null && quest.Rewards != null)
            {
                for (int i = 0; i < quest.Rewards.Count; i++)
                {
                    CharacterStoryQuestRewardDefinition reward = quest.Rewards[i];
                    if (reward == null || !reward.IsValid) continue;
                    if (reward.RewardType == CharacterStoryQuestRewardType.Currency && currency == null)
                        currency = reward;
                    else if (reward.RewardType == CharacterStoryQuestRewardType.Item && item == null)
                        item = reward;
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
                // jewel은 프리팹의 애니메이션 아이콘을 유지한다. 이후 정적 아이콘 재화가 생기면
                // Definition.Icon이 연결된 경우에만 Animator 대신 그 스프라이트를 표시한다.
                Sprite icon = currency.Currency != null ? currency.Currency.Icon : null;
                if (icon != null && rewardCurrencyIcon != null)
                {
                    if (rewardCurrencyAnimator != null) rewardCurrencyAnimator.enabled = false;
                    rewardCurrencyIcon.sprite = icon;
                    rewardCurrencyIcon.enabled = true;
                }
                else if (rewardCurrencyAnimator != null)
                    rewardCurrencyAnimator.enabled = true;
            }

            if (hasItem) rewardItemSlot.SetItem(item.Item, item.Amount);
            else rewardItemSlot.SetEmpty();
        }

        private void ClearRewardView()
        {
            SetActive(rewardRoot, false);
            if (rewardItemSlot != null) rewardItemSlot.SetEmpty();
        }

        private List<CharacterStoryQuestObjectiveDefinition> EnabledObjectives(string questId)
        {
            List<CharacterStoryQuestObjectiveDefinition> all = objectiveCatalog.ForQuest(questId);
            all.RemoveAll(objective => objective == null || !objective.Enabled);
            return all;
        }

        private void UpdateObjectiveLines(IReadOnlyList<CharacterStoryQuestObjectiveDefinition> objectives, CharacterStoryQuestSnapshot snapshot)
        {
            int count = objectives != null ? objectives.Count : 0;
            for (int i = 0; i < count; i++)
            {
                CharacterStoryQuestObjectiveDefinition objective = objectives[i];
                int required = objective.RequiredValue;
                int progress = GetProgress(snapshot, objective.ObjectiveId, required);
                TMP_Text type = GetOrCreateLine(questTypeLineTemplate, typeLines, i);
                TMP_Text description = GetOrCreateLine(questDescriptionLineTemplate, descriptionLines, i);
                if (type != null) type.text = ConditionTitle(objective.ConditionType);
                if (description != null) description.text = ObjectiveDescription(objective, progress, required);
            }
            SetLinesActive(typeLines, count);
            SetLinesActive(descriptionLines, count);
        }

        // Runtime lines are retained for the life of this panel.  In particular, do not Destroy
        // them during Refresh: destruction is deferred to end-of-frame and a localization callback
        // in that gap used to instantiate a second set of lines.
        private static TMP_Text GetOrCreateLine(TMP_Text template, List<TMP_Text> pool, int index)
        {
            if (template == null || template.transform.parent == null) return null;
            TMP_Text line;
            if (index < pool.Count)
            {
                line = pool[index];
            }
            else
            {
                line = Instantiate(template, template.transform.parent);
                line.name = template.name + " (Runtime)";
                pool.Add(line);
            }
            if (line != null) line.gameObject.SetActive(true);
            return line;
        }

        private static void SetLinesActive(List<TMP_Text> lines, int activeCount)
        {
            for (int i = activeCount; i < lines.Count; i++)
                if (lines[i] != null) lines[i].gameObject.SetActive(false);
        }

        private void RefreshObjectiveLayout(bool resetToTop)
        {
            if (objectiveScroll == null || objectiveScroll.content == null) return;

            RectTransform content = objectiveScroll.content;
            // ContentSizeFitter reads the preferred height of its direct children.  Rebuild those
            // variable-height groups first so their values are current before Content is measured.
            for (int i = 0; i < content.childCount; i++)
            {
                RectTransform child = content.GetChild(i) as RectTransform;
                if (child != null && child.gameObject.activeInHierarchy)
                    LayoutRebuilder.ForceRebuildLayoutImmediate(child);
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            Canvas.ForceUpdateCanvases();

            if (resetToTop)
            {
                objectiveScroll.StopMovement();
                objectiveScroll.verticalNormalizedPosition = 1f;
            }
        }

        private string ObjectiveDescription(CharacterStoryQuestObjectiveDefinition objective, int current, int required)
        {
            switch (objective.ConditionType)
            {
                case CharacterStoryQuestConditionType.CharacterLevelAtLeast:
                    return SafeFormat(Quest(10001), "Level {0} ({1}/{2})", required, current, required);
                case CharacterStoryQuestConditionType.MonsterDefeatCount:
                    return SafeFormat(Quest(10002), "{0} {1} ({2}/{3})", TargetName(objective, true), required, current, required);
                case CharacterStoryQuestConditionType.StaminaSpent:
                    return SafeFormat(Quest(10003), "Stamina {0} ({1}/{2})", required, current, required);
                case CharacterStoryQuestConditionType.DungeonEnterCount:
                    return SafeFormat(Quest(10004), "{0} {1} ({2}/{3})", TargetName(objective, false), required, current, required);
                default: return string.Format("{0}/{1}", current, required);
            }
        }

        private string ConditionTitle(CharacterStoryQuestConditionType type)
        {
            switch (type)
            {
                case CharacterStoryQuestConditionType.CharacterLevelAtLeast: return TextOrFallback(Quest(1), "Level");
                case CharacterStoryQuestConditionType.MonsterDefeatCount: return TextOrFallback(Quest(2), "Defeat");
                case CharacterStoryQuestConditionType.StaminaSpent: return TextOrFallback(Quest(3), "Stamina");
                case CharacterStoryQuestConditionType.DungeonEnterCount: return TextOrFallback(Quest(4), "Dungeon");
                default: return string.Empty;
            }
        }

        private string TargetName(CharacterStoryQuestObjectiveDefinition objective, bool monster)
        {
            IReadOnlyList<string> ids = objective.TargetIds;
            if (ids == null || ids.Count == 0) return TextOrFallback(Quest(monster ? 100002 : 100004), monster ? "Any monster" : "Any dungeon");
            var names = new List<string>();
            for (int i = 0; i < ids.Count; i++) names.Add(monster ? MonsterName(ids[i]) : DungeonName(ids[i]));
            return string.Join(", ", names);
        }

        private string MonsterName(string id)
        {
            MonsterDefinition definition = monsterCatalog != null ? monsterCatalog.Find(id) : null;
            return SafeLocalized(definition != null ? definition.LocalizedName : null, id);
        }

        private LocalizedTextReference MonsterLocalizedName(string id)
        {
            MonsterDefinition definition = monsterCatalog != null ? monsterCatalog.Find(id) : null;
            return definition != null ? definition.LocalizedName : null;
        }

        private string DungeonName(string id)
        {
            DungeonDefinition definition = null;
            if (dungeonCatalog != null)
                foreach (DungeonDefinition candidate in dungeonCatalog.Dungeons)
                    if (candidate != null && string.Equals(candidate.DungeonId, id, StringComparison.Ordinal)) { definition = candidate; break; }
            return SafeLocalized(definition != null ? definition.DungeonName : null, id);
        }

        private LocalizedTextReference DungeonLocalizedName(string id)
        {
            DungeonDefinition definition = null;
            if (dungeonCatalog != null)
                foreach (DungeonDefinition candidate in dungeonCatalog.Dungeons)
                    if (candidate != null && string.Equals(candidate.DungeonId, id, StringComparison.Ordinal)) { definition = candidate; break; }
            return definition != null ? definition.DungeonName : null;
        }

        private static string SafeLocalized(LocalizedTextReference reference, string fallback)
        {
            if (reference == null || !reference.HasReference) return SafeId(fallback);
            string localized = reference.GetLocalizedString();
            return string.IsNullOrWhiteSpace(localized) || localized.StartsWith("No translation found", StringComparison.Ordinal) ? SafeId(fallback) : localized;
        }

        private static string SafeId(string id) => string.IsNullOrWhiteSpace(id) ? "-" : id;
        private string Quest(int key) => localizedQuestTexts.TryGetValue(key, out string value) ? value : null;
        private static string TextOrFallback(string value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;
        private static bool IsUsableLocalizedValue(string value, string key) => !string.IsNullOrWhiteSpace(value) &&
            !string.Equals(value, key, StringComparison.Ordinal) && !value.StartsWith("No translation found", StringComparison.Ordinal);

        public static string FormatProgressPercent(float progress) => Mathf.FloorToInt(Mathf.Clamp01(progress) * 100f + .5f) + "%";

        private static void SetSliderProgress(Slider slider, float progress)
        {
            if (slider != null) slider.normalizedValue = Mathf.Clamp01(progress);
        }

        private static int GetProgress(CharacterStoryQuestSnapshot snapshot, string objectiveId, int required)
        {
            int value = snapshot != null && snapshot.ObjectiveProgress != null && snapshot.ObjectiveProgress.TryGetValue(objectiveId, out int progress) ? progress : 0;
            return Mathf.Clamp(value, 0, Mathf.Max(1, required));
        }

        public static float CalculateCurrentProgress(IReadOnlyList<CharacterStoryQuestObjectiveDefinition> objectives, CharacterStoryQuestSnapshot snapshot)
        {
            if (objectives == null || objectives.Count == 0) return 0f;
            float sum = 0f;
            for (int i = 0; i < objectives.Count; i++)
            {
                CharacterStoryQuestObjectiveDefinition objective = objectives[i];
                if (objective == null) continue;
                sum += (float)GetProgress(snapshot, objective.ObjectiveId, objective.RequiredValue) / objective.RequiredValue;
            }
            return Mathf.Clamp01(sum / objectives.Count);
        }

        public static float CalculateTotalProgress(CharacterStoryQuestCatalog catalog, string characterId, CharacterStoryQuestSnapshot snapshot,
            out int currentNumber, out int completedCount, out int totalCount)
        {
            currentNumber = 0; completedCount = 0; totalCount = 0;
            if (catalog == null || string.IsNullOrEmpty(characterId)) return 0f;
            var enabled = new List<CharacterStoryQuestDefinition>();
            foreach (CharacterStoryQuestDefinition quest in catalog.Quests)
                if (quest != null && quest.Enabled && string.Equals(quest.CharacterId, characterId, StringComparison.Ordinal)) enabled.Add(quest);
            enabled.Sort((left, right) => left.DisplayOrder != right.DisplayOrder ? left.DisplayOrder.CompareTo(right.DisplayOrder) : string.CompareOrdinal(left.QuestId, right.QuestId));
            totalCount = enabled.Count;
            if (totalCount == 0) return 0f;
            var completed = new HashSet<string>(snapshot != null && snapshot.CompletedQuestIds != null ? snapshot.CompletedQuestIds : new List<string>(), StringComparer.Ordinal);
            foreach (CharacterStoryQuestDefinition quest in enabled)
            {
                if (completed.Contains(quest.QuestId)) completedCount++;
                if (snapshot != null && string.Equals(quest.QuestId, snapshot.ActiveQuestId, StringComparison.Ordinal)) currentNumber = enabled.IndexOf(quest) + 1;
            }
            completedCount = Mathf.Clamp(completedCount, 0, totalCount);
            return (float)completedCount / totalCount;
        }

        private static string SafeFormat(string format, string fallback, params object[] arguments)
        {
            try { return string.Format(string.IsNullOrEmpty(format) ? fallback : format, arguments); }
            catch (FormatException) { return string.Format(fallback, arguments); }
        }

        private static void DisableDynamicLocalizer(TMP_Text target)
        {
            if (target != null && target.TryGetComponent(out LocalizedTMPText localizer)) localizer.enabled = false;
        }

        private static void SetActive(GameObject target, bool value) { if (target != null && target.activeSelf != value) target.SetActive(value); }
    }
}
