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
        private const int MaxLevelKey = 99;
        private const int OwnedCountKey = 100;
        private static readonly Color CompletedColor = new Color32(0x95, 0x95, 0x95, 0xff);

        [Header("Recruitment Catalogs (Inspector에서만 연결)")]
        [SerializeField] private CharacterAcquisitionCatalog acquisitionCatalog;
        [SerializeField] private CharacterUnlockConditionCatalog conditionCatalog;
        [Header("Unlock Info (Inspector에서만 연결)")]
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text countText;
        [SerializeField] private LocalizedTMPText countLocalizer;
        [SerializeField] private RectTransform conditionContent;
        [SerializeField] private TMP_Text conditionTemplate;
        [SerializeField] private GameObject completeRoot;

        private sealed class Line
        {
            public TMP_Text Text;
            public GameObject CheckOn;
            public FontStyles DefaultStyle;
            public Color DefaultColor;
        }

        private readonly List<Line> linePool = new List<Line>();
        private readonly Dictionary<int, string> localized = new Dictionary<int, string>();
        private readonly Dictionary<LocalizedTextReference, LocalizedString.ChangeHandler> handlers = new Dictionary<LocalizedTextReference, LocalizedString.ChangeHandler>();
        private CharacterDefinition character;
        private SaveData document;
        private LocalizedTextReference countFormat;

        public int PooledLineCount => linePool.Count;
        public int ActiveLineCount { get; private set; }
        public bool HasRequiredReferences => acquisitionCatalog != null && conditionCatalog != null && titleText != null &&
            countText != null && countLocalizer != null &&
            conditionContent != null && conditionTemplate != null && completeRoot != null;

        public void BindCharacter(CharacterDefinition value, SaveData data)
        {
            character = value;
            document = data;
            if (isActiveAndEnabled) Refresh();
        }

        private void OnEnable()
        {
            DisableCountLocalizer();
            Refresh();
            SubscribeLocalization();
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
                DisableCountLocalizer();
                if (countText != null) countText.text = string.Empty;
                SetLinesActive(0);
                SetActive(completeRoot, false);
                return;
            }

            RecruitmentUnlockService.UnlockProgressSnapshot snapshot = RecruitmentUnlockService.EvaluateProgress(
                acquisitionCatalog, conditionCatalog, document, character.CharacterId,
                IsPermanentlyUnlocked(document, character.CharacterId));
            ApplyCountFallback(snapshot.SatisfiedConditionCount, snapshot.Conditions.Count);
            for (int i = 0; i < snapshot.Conditions.Count; i++)
            {
                RecruitmentUnlockService.UnlockConditionProgress progress = snapshot.Conditions[i];
                Line line = GetOrCreateLine(i);
                if (line == null) continue;
                line.Text.text = ConditionText(progress);
                // 완료 상태는 취소선 대신 행 안에 저작된 체크 표시로 보여 준다. 템플릿에 체크가
                // 없는 이전 프리팹도 텍스트 표시는 계속 할 수 있도록 CheckOn은 선택 참조다.
                line.Text.fontStyle = line.DefaultStyle;
                line.Text.color = progress.IsSatisfied ? CompletedColor : line.DefaultColor;
                SetActive(line.CheckOn, progress.IsSatisfied);
                SetActive(line.Text.gameObject, true);
            }
            OrderPooledLines();
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
                Transform check = clone.transform.Find("sp_check/sp_checkOn");
                linePool.Add(new Line
                {
                    Text = clone,
                    CheckOn = check != null ? check.gameObject : null,
                    DefaultStyle = clone.fontStyle,
                    DefaultColor = clone.color
                });
            }
            return linePool[index];
        }

        /// <summary>
        /// 풀을 확보한 뒤 한 번만 정렬한다. 각 행을 완료 문구의 현재 위치로 옮기면 앞에서 옮긴
        /// 행의 인덱스가 바뀌어 재바인드마다 순서가 뒤집힐 수 있으므로, 템플릿 뒤의 고정 슬롯을
        /// linePool 순서대로 채운 다음 완료 문구를 마지막에 둔다.
        /// </summary>
        private void OrderPooledLines()
        {
            if (conditionTemplate == null || completeRoot == null) return;
            int siblingIndex = conditionTemplate.transform.GetSiblingIndex() + 1;
            for (int i = 0; i < linePool.Count; i++)
            {
                Line line = linePool[i];
                if (line.Text != null) line.Text.transform.SetSiblingIndex(siblingIndex++);
            }
            completeRoot.transform.SetSiblingIndex(siblingIndex);
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
                    SetActive(line.CheckOn, false);
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

        private void ApplyCountFormat(string format)
        {
            if (countText == null || character == null) return;
            RecruitmentUnlockService.UnlockProgressSnapshot snapshot = RecruitmentUnlockService.EvaluateProgress(
                acquisitionCatalog, conditionCatalog, document, character.CharacterId,
                IsPermanentlyUnlocked(document, character.CharacterId));
            countText.text = SafeFormat(format, "({0}/{1})", snapshot.SatisfiedConditionCount, snapshot.Conditions.Count);
        }

        private void ApplyCountFallback(int satisfied, int total)
        {
            DisableCountLocalizer();
            if (countText != null) countText.text = SafeFormat(null, "({0}/{1})", satisfied, total);
        }

        private void DisableCountLocalizer()
        {
            if (countLocalizer != null && countLocalizer.enabled) countLocalizer.enabled = false;
        }

        private void SubscribeLocalization()
        {
            if (handlers.Count > 0) return;
            AddLocalization(MaxLevelKey); AddLocalization(OwnedCountKey);
            countFormat = countLocalizer != null ? countLocalizer.TextReference : null;
            if (countFormat != null && countFormat.HasReference) countFormat.StringChanged += ApplyCountFormat;
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
            if (countFormat != null) countFormat.StringChanged -= ApplyCountFormat;
            countFormat = null;
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
