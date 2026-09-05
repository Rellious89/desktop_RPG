using System;
using System.Collections.Generic;
using Character;
using Common;
using Recruitment;
using TMPro;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Tables;
using UnityEngine.UI;

namespace CharacterArchive
{
    /// <summary>미보유 캐릭터의 모집 등장 조건만 표시한다. 평가와 저장은 Recruitment 도메인에 남긴다.</summary>
    [DisallowMultipleComponent]
    public sealed class CharacterUnlockInfoController : MonoBehaviour
    {
        private const string UiTableGuid = "32fd067a20b754a50b20446b9c78d2ae";
        private const int TitleKey = 98;
        private const int MaxLevelKey = 99;
        private const int OwnedCountKey = 100;
        private static readonly Color CompletedColor = new Color32(0x95, 0x95, 0x95, 0xff);

        [Header("Recruitment Catalogs (Inspector에서만 연결)")]
        [SerializeField] private CharacterAcquisitionCatalog acquisitionCatalog;
        [SerializeField] private CharacterUnlockConditionCatalog conditionCatalog;
        [Header("Unlock Info (Inspector에서만 연결)")]
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private RectTransform conditionContent;
        [SerializeField] private TMP_Text conditionTemplate;
        [SerializeField] private GameObject completeRoot;

        private sealed class Line
        {
            public TMP_Text Text;
            public FontStyles DefaultStyle;
            public Color DefaultColor;
        }

        private readonly List<Line> linePool = new List<Line>();
        private readonly Dictionary<int, string> localized = new Dictionary<int, string>();
        private readonly Dictionary<LocalizedTextReference, LocalizedString.ChangeHandler> handlers = new Dictionary<LocalizedTextReference, LocalizedString.ChangeHandler>();
        private CharacterDefinition character;
        private SaveData document;

        public int PooledLineCount => linePool.Count;
        public int ActiveLineCount { get; private set; }
        public bool HasRequiredReferences => acquisitionCatalog != null && conditionCatalog != null && titleText != null &&
            conditionContent != null && conditionTemplate != null && completeRoot != null;

        public void BindCharacter(CharacterDefinition value, SaveData data)
        {
            character = value;
            document = data;
            if (isActiveAndEnabled) Refresh();
        }

        private void OnEnable()
        {
            if (titleText != null && titleText.TryGetComponent(out LocalizedTMPText localizer)) localizer.enabled = false;
            SubscribeLocalization();
            Refresh();
        }

        private void OnDisable()
        {
            UnsubscribeLocalization();
            SetLinesActive(0);
        }

        private void OnDestroy() => UnsubscribeLocalization();

        public void Refresh()
        {
            if (!HasRequiredReferences || character == null)
            {
                if (titleText != null) titleText.text = string.Empty;
                SetLinesActive(0);
                SetActive(completeRoot, false);
                return;
            }

            RecruitmentUnlockService.UnlockProgressSnapshot snapshot = RecruitmentUnlockService.EvaluateProgress(
                acquisitionCatalog, conditionCatalog, document, character.CharacterId,
                IsPermanentlyUnlocked(document, character.CharacterId));
            titleText.text = SafeFormat(Text(TitleKey), "{0}/{1}", snapshot.SatisfiedConditionCount, snapshot.Conditions.Count);
            for (int i = 0; i < snapshot.Conditions.Count; i++)
            {
                RecruitmentUnlockService.UnlockConditionProgress progress = snapshot.Conditions[i];
                Line line = GetOrCreateLine(i);
                if (line == null) continue;
                line.Text.text = ConditionText(progress);
                line.Text.fontStyle = progress.IsSatisfied ? line.DefaultStyle | FontStyles.Strikethrough : line.DefaultStyle;
                line.Text.color = progress.IsSatisfied ? CompletedColor : line.DefaultColor;
                SetActive(line.Text.gameObject, true);
            }
            SetLinesActive(snapshot.Conditions.Count);
            SetActive(completeRoot, snapshot.IsRecruitmentEligible);
            LayoutRebuilder.MarkLayoutForRebuild(conditionContent);
        }

        private Line GetOrCreateLine(int index)
        {
            if (conditionTemplate == null || conditionContent == null) return null;
            while (linePool.Count <= index)
            {
                TMP_Text clone = Instantiate(conditionTemplate, conditionContent);
                clone.name = conditionTemplate.name + "_Runtime";
                clone.gameObject.SetActive(false);
                linePool.Add(new Line { Text = clone, DefaultStyle = clone.fontStyle, DefaultColor = clone.color });
            }
            return linePool[index];
        }

        private void SetLinesActive(int count)
        {
            ActiveLineCount = Mathf.Clamp(count, 0, linePool.Count);
            for (int i = 0; i < linePool.Count; i++)
            {
                Line line = linePool[i];
                if (line.Text == null) continue;
                if (i >= ActiveLineCount)
                {
                    line.Text.fontStyle = line.DefaultStyle;
                    line.Text.color = line.DefaultColor;
                }
                SetActive(line.Text.gameObject, i < ActiveLineCount);
            }
            if (conditionTemplate != null) SetActive(conditionTemplate.gameObject, false);
        }

        private string ConditionText(RecruitmentUnlockService.UnlockConditionProgress progress)
        {
            int key = progress.Entry.Type == CharacterUnlockConditionType.MaxOwnedCharacterLevelAtLeast ? MaxLevelKey : OwnedCountKey;
            return SafeFormat(Text(key), "{0}/{1}", progress.CurrentValue, progress.Entry.RequiredValue);
        }

        private void SubscribeLocalization()
        {
            if (handlers.Count > 0) return;
            AddLocalization(TitleKey); AddLocalization(MaxLevelKey); AddLocalization(OwnedCountKey);
        }

        private void AddLocalization(int key)
        {
            var reference = new LocalizedTextReference((TableReference)new Guid(UiTableGuid), key.ToString());
            LocalizedString.ChangeHandler handler = value =>
            {
                if (IsUsable(value, key)) localized[key] = value; else localized.Remove(key);
                Refresh();
            };
            handlers.Add(reference, handler);
            reference.StringChanged += handler;
        }

        private void UnsubscribeLocalization()
        {
            foreach (KeyValuePair<LocalizedTextReference, LocalizedString.ChangeHandler> pair in handlers)
                pair.Key.StringChanged -= pair.Value;
            handlers.Clear();
            localized.Clear();
        }

        private string Text(int key) => localized.TryGetValue(key, out string value) ? value : null;
        private static bool IsUsable(string value, int key) => !string.IsNullOrWhiteSpace(value) &&
            !string.Equals(value, key.ToString(), StringComparison.Ordinal) && !value.StartsWith("No translation found", StringComparison.Ordinal);
        private static bool IsPermanentlyUnlocked(SaveData data, string id) => data != null && data.unlockedRecruitmentCharacterIds != null &&
            data.unlockedRecruitmentCharacterIds.Contains(id);
        private static string SafeFormat(string format, string fallback, params object[] args)
        {
            try { return string.Format(string.IsNullOrEmpty(format) ? fallback : format, args); }
            catch (FormatException) { return string.Format(fallback, args); }
        }
        private static void SetActive(GameObject target, bool value) { if (target != null && target.activeSelf != value) target.SetActive(value); }
    }
}
