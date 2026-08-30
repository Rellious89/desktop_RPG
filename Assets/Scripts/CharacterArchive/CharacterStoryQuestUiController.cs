using System;
using System.Collections.Generic;
using Character;
using Common;
using Dungeon;
using Quest;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CharacterArchive
{
    /// <summary>캐릭터 명부 안에서 서사 퀘스트 상태를 읽어 표시한다. 저장 상태를 직접 바꾸지 않고,
    /// 완료는 CharacterStoryQuestService의 명시적 관문으로만 요청한다.</summary>
    [DisallowMultipleComponent]
    public sealed class CharacterStoryQuestUiController : MonoBehaviour
    {
        public enum RightPage { CharacterInfo, QuestInfo }

        private const string QuestTableGuid = "GUID:11805744adb144cd3bb37f325635e0d9";
        private const string UiTableGuid = "GUID:32fd067a20b754a50b20446b9c78d2ae";

        [Header("Catalogs (Inspector에서만 연결)")]
        [SerializeField] private CharacterStoryQuestCatalog questCatalog;
        [SerializeField] private CharacterStoryQuestObjectiveCatalog objectiveCatalog;
        [SerializeField] private MonsterCatalog monsterCatalog;
        [SerializeField] private DungeonCatalog dungeonCatalog;

        [Header("Pages")]
        [SerializeField] private GameObject characterInfoPage;
        [SerializeField] private GameObject questInfoPage;
        [SerializeField] private Button swapButton;
        [SerializeField] private RightPage defaultRightPage = RightPage.CharacterInfo;

        [Header("Quest UI")]
        [SerializeField] private Image currentProgressFill;
        [SerializeField] private Image totalProgressFill;
        [SerializeField] private TMP_Text totalProgressText;
        [SerializeField] private TMP_Text questTypeTitle;
        [SerializeField] private TMP_Text questDescriptionTitle;
        [SerializeField] private TMP_Text questTypeLineTemplate;
        [SerializeField] private TMP_Text questDescriptionLineTemplate;
        [SerializeField] private Button completeButton;
        [SerializeField] private ScrollRect objectiveScroll;

        private readonly List<TMP_Text> typeLines = new List<TMP_Text>();
        private readonly List<TMP_Text> descriptionLines = new List<TMP_Text>();
        private readonly List<LocalizedTextReference> localizationReferences = new List<LocalizedTextReference>();
        private CharacterDefinition selected;
        private bool completionRequested;
        private bool subscribed;

        public bool HasRequiredReferences => questCatalog != null && objectiveCatalog != null &&
                                             monsterCatalog != null && dungeonCatalog != null &&
                                             characterInfoPage != null && questInfoPage != null &&
                                             swapButton != null && completeButton != null &&
                                             questTypeLineTemplate != null && questDescriptionLineTemplate != null;

        public void OpenFor(CharacterDefinition definition)
        {
            selected = definition;
            completionRequested = false;
            ShowPage(defaultRightPage);
            Refresh();
        }

        public void BindCharacter(CharacterDefinition definition)
        {
            selected = definition;
            completionRequested = false;
            Refresh();
        }

        public void Close()
        {
            selected = null;
            completionRequested = false;
            ClearLines(typeLines); ClearLines(descriptionLines);
        }

        private void OnEnable()
        {
            BindButtons();
            SubscribeLocalization();
        }

        private void OnDisable()
        {
            UnbindButtons();
            UnsubscribeLocalization();
        }

        private void BindButtons()
        {
            if (swapButton != null) { swapButton.onClick.RemoveListener(TogglePage); swapButton.onClick.AddListener(TogglePage); }
            if (completeButton != null) { completeButton.onClick.RemoveListener(ConfirmComplete); completeButton.onClick.AddListener(ConfirmComplete); }
        }

        private void UnbindButtons()
        {
            if (swapButton != null) swapButton.onClick.RemoveListener(TogglePage);
            if (completeButton != null) completeButton.onClick.RemoveListener(ConfirmComplete);
        }

        private void SubscribeLocalization()
        {
            if (subscribed) return;
            foreach (string key in new[] { "1", "2", "3", "4", "10001", "10002", "10003", "10004", "100002", "100004" })
                AddLocalization(new LocalizedTextReference(QuestTableGuid, key));
            AddLocalization(new LocalizedTextReference(UiTableGuid, "87"));
            subscribed = true;
        }

        private void AddLocalization(LocalizedTextReference reference)
        {
            localizationReferences.Add(reference);
            reference.StringChanged += HandleLocaleChanged;
        }

        private void UnsubscribeLocalization()
        {
            foreach (LocalizedTextReference reference in localizationReferences) reference.StringChanged -= HandleLocaleChanged;
            localizationReferences.Clear(); subscribed = false;
        }

        private void HandleLocaleChanged(string _) => Refresh();

        private void TogglePage() => ShowPage(questInfoPage != null && questInfoPage.activeSelf ? RightPage.CharacterInfo : RightPage.QuestInfo);

        private void ShowPage(RightPage page)
        {
            SetActive(characterInfoPage, page == RightPage.CharacterInfo);
            SetActive(questInfoPage, page == RightPage.QuestInfo);
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
            float total = CalculateTotalProgress(questCatalog, selected != null ? selected.CharacterId : string.Empty, snapshot, current, out int currentNumber, out int completedCount, out int totalCount);
            if (currentProgressFill != null) currentProgressFill.fillAmount = current;
            if (totalProgressFill != null) totalProgressFill.fillAmount = total;
            if (totalProgressText != null) totalProgressText.text = SafeFormat(Ui(87), "{0}번 퀘스트 진행 중 ({1}/{2})", currentNumber, completedCount, totalCount);

            ClearLines(typeLines); ClearLines(descriptionLines);
            for (int i = 0; i < objectives.Count; i++)
            {
                CharacterStoryQuestObjectiveDefinition objective = objectives[i];
                int required = objective.RequiredValue;
                int progress = GetProgress(snapshot, objective.ObjectiveId, required);
                TMP_Text type = CreateLine(questTypeLineTemplate, typeLines);
                TMP_Text description = CreateLine(questDescriptionLineTemplate, descriptionLines);
                if (type != null) type.text = ConditionTitle(objective.ConditionType);
                if (description != null) description.text = ObjectiveDescription(objective, progress, required);
            }

            SetActive(questTypeTitle != null ? questTypeTitle.gameObject : null, objectives.Count > 0);
            SetActive(questDescriptionTitle != null ? questDescriptionTitle.gameObject : null, objectives.Count > 0);
            if (completeButton != null) completeButton.interactable = !completionRequested && active != null && snapshot.ReadyToComplete;
            if (objectiveScroll != null) { objectiveScroll.verticalNormalizedPosition = 1f; LayoutRebuilder.ForceRebuildLayoutImmediate(objectiveScroll.content); }
        }

        private List<CharacterStoryQuestObjectiveDefinition> EnabledObjectives(string questId)
        {
            List<CharacterStoryQuestObjectiveDefinition> all = objectiveCatalog.ForQuest(questId);
            all.RemoveAll(objective => objective == null || !objective.Enabled);
            return all;
        }

        private TMP_Text CreateLine(TMP_Text template, List<TMP_Text> destination)
        {
            if (template == null || template.transform.parent == null) return null;
            TMP_Text line = Instantiate(template, template.transform.parent);
            line.name = template.name + " (Runtime)";
            line.gameObject.SetActive(true);
            destination.Add(line);
            return line;
        }

        private static void ClearLines(List<TMP_Text> lines)
        {
            for (int i = 0; i < lines.Count; i++) if (lines[i] != null) Destroy(lines[i].gameObject);
            lines.Clear();
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
                case CharacterStoryQuestConditionType.CharacterLevelAtLeast: return Quest(1);
                case CharacterStoryQuestConditionType.MonsterDefeatCount: return Quest(2);
                case CharacterStoryQuestConditionType.StaminaSpent: return Quest(3);
                case CharacterStoryQuestConditionType.DungeonEnterCount: return Quest(4);
                default: return string.Empty;
            }
        }

        private string TargetName(CharacterStoryQuestObjectiveDefinition objective, bool monster)
        {
            IReadOnlyList<string> ids = objective.TargetIds;
            if (ids == null || ids.Count == 0) return Quest(monster ? 100002 : 100004);
            var names = new List<string>();
            for (int i = 0; i < ids.Count; i++) names.Add(monster ? MonsterName(ids[i]) : DungeonName(ids[i]));
            return string.Join(", ", names);
        }

        private string MonsterName(string id)
        {
            MonsterDefinition definition = monsterCatalog != null ? monsterCatalog.Find(id) : null;
            return SafeLocalized(definition != null ? definition.LocalizedName : null, id);
        }

        private string DungeonName(string id)
        {
            DungeonDefinition definition = null;
            if (dungeonCatalog != null)
                foreach (DungeonDefinition candidate in dungeonCatalog.Dungeons)
                    if (candidate != null && string.Equals(candidate.DungeonId, id, StringComparison.Ordinal)) { definition = candidate; break; }
            return SafeLocalized(definition != null ? definition.DungeonName : null, id);
        }

        private static string SafeLocalized(LocalizedTextReference reference, string fallback)
        {
            if (reference == null || !reference.HasReference) return SafeId(fallback);
            string localized = reference.GetLocalizedString();
            return string.IsNullOrWhiteSpace(localized) || localized.StartsWith("No translation found", StringComparison.Ordinal) ? SafeId(fallback) : localized;
        }

        private static string SafeId(string id) => string.IsNullOrWhiteSpace(id) ? "-" : id;
        private string Quest(int key) => SafeLocalized(new LocalizedTextReference(QuestTableGuid, key.ToString()), key.ToString());
        private string Ui(int key) => SafeLocalized(new LocalizedTextReference(UiTableGuid, key.ToString()), "{0}번 퀘스트 진행 중 ({1}/{2})");

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
            float currentProgress, out int currentNumber, out int completedCount, out int totalCount)
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
            currentProgress = currentNumber > 0 ? Mathf.Clamp01(currentProgress) : 0f;
            return Mathf.Clamp01((completedCount + currentProgress) / totalCount);
        }

        private static string SafeFormat(string format, string fallback, params object[] arguments)
        {
            try { return string.Format(string.IsNullOrEmpty(format) ? fallback : format, arguments); }
            catch (FormatException) { return string.Format(fallback, arguments); }
        }

        private static void SetActive(GameObject target, bool value) { if (target != null && target.activeSelf != value) target.SetActive(value); }
    }
}
