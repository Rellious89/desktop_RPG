using System;
using System.Globalization;
using Character;
using TMPro;
using UnityEngine;

namespace Common
{
    /// <summary>
    /// CharacterHUD_HoverTooltip 프리팹의 내용만 그린다. 표시 수명과 위치는
    /// <see cref="CharacterHudTooltipController"/>가 소유하고, 이 뷰는 로스터에서 읽어 온
    /// 한 캐릭터의 레벨/행동력/오염도/컨디션과 이름 구독만 유지한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CharacterHudTooltipView : MonoBehaviour
    {
        [Header("Dynamic labels")]
        [SerializeField] private TextMeshProUGUI levelText;
        [SerializeField] private TextMeshProUGUI characterNameText;
        [SerializeField] private TextMeshProUGUI staminaValueText;
        [SerializeField] private TextMeshProUGUI purificationValueText;

        [Header("Condition (비워두면 condition 자식 이름으로 자동 탐색)")]
        [SerializeField] private GameObject activeCondition;
        [SerializeField] private GameObject availableCondition;
        [SerializeField] private GameObject exhaustedCondition;
        [SerializeField] private GameObject recoveringCondition;
        [SerializeField] private GameObject recoveryCompleteCondition;

        private readonly CharacterNameBinding characterName = new CharacterNameBinding();
        private LocalizedTextReference levelFormatReference;
        private bool levelFormatSubscribed;
        private bool resolved;
        private CharacterDefinition definition;
        private int level;
        private int stamina;
        private int maxStamina;
        private double corruption;
        private int maxCorruption;
        private string levelFormat;
        private string staminaFormat;
        private string purificationFormat;

        public CharacterDefinition BoundCharacter => definition;

        private void Awake()
        {
            ResolveReferences();
            CaptureStaticFormats();
        }

        private void OnDisable()
        {
            Clear();
        }

        private void OnDestroy()
        {
            Clear();
        }

        /// <summary>
        /// 프리팹의 레벨 형식과 캐릭터 이름의 Locale 변경 구독을 사용해 현재 로스터 상태를 그린다.
        /// 컨디션 문구는 활성화된 상태 오브젝트의 LocalizedTMPText가 자기 구독 수명주기를 소유한다.
        /// 행동력/오염도 title은 프리팹의 LocalizedTMPText가 소유하므로 여기서 덮어쓰지 않는다.
        /// </summary>
        public void Bind(CharacterDefinition nextDefinition, int nextLevel, int currentStamina, int maximumStamina,
                         double currentCorruption, int maximumCorruption,
                         CharacterSwapListItem.DisplayState condition)
        {
            ResolveReferences();
            CaptureStaticFormats();

            definition = nextDefinition;
            level = Mathf.Max(0, nextLevel);
            stamina = Mathf.Max(0, currentStamina);
            maxStamina = Mathf.Max(0, maximumStamina);
            double safeCorruption = double.IsNaN(currentCorruption) || double.IsInfinity(currentCorruption)
                ? 0d : Math.Max(0d, currentCorruption);
            // 기존 정화 패널과 같은 한 자리 소수 표시만 유지한다. 값 형식이 단순 {0}인 프리팹도
            // 저장 부동소수 오차(예: 12.0000000003)를 그대로 노출하지 않는다.
            corruption = Math.Round(safeCorruption, 1, MidpointRounding.AwayFromZero);
            maxCorruption = Mathf.Max(0, maximumCorruption);

            BindLevelFormat();
            characterName.Bind(definition, ApplyCharacterName);
            SetCondition(condition);
            ApplyValues();
        }

        /// <summary>프리팹에 준비된 다섯 상태 중 현재 상태 하나만 표시한다.</summary>
        public void SetCondition(CharacterSwapListItem.DisplayState condition)
        {
            ResolveReferences();

            SetActive(activeCondition, condition == CharacterSwapListItem.DisplayState.InUse);
            SetActive(availableCondition, condition == CharacterSwapListItem.DisplayState.Ready);
            SetActive(exhaustedCondition, condition == CharacterSwapListItem.DisplayState.Exhausted);
            SetActive(recoveringCondition, condition == CharacterSwapListItem.DisplayState.Recovering);
            SetActive(recoveryCompleteCondition,
                condition == CharacterSwapListItem.DisplayState.RecoveryComplete);
        }

        /// <summary>로컬라이즈 구독과 이전 캐릭터 참조를 끊는다. 숨긴 툴팁이 Locale 변경으로 갱신되지 않는다.</summary>
        public void Clear()
        {
            characterName.Unbind();
            UnbindLevelFormat();
            definition = null;
        }

        public void RebuildLayout()
        {
            RectTransform rect = transform as RectTransform;
            if (rect != null) UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
        }

        private void BindLevelFormat()
        {
            UnbindLevelFormat();

            // lb_CharacterLevel은 값이 들어가는 동적 라벨이다. 실수로 LocalizedTMPText가 달려 있어도
            // 그 컴포넌트가 Locale 변경 때 "Lv.{0}"만 다시 쓰지 않도록, 같은 참조의 형식을 이 뷰가
            // 받아 수치를 넣어 그린다. 타이틀 라벨의 LocalizedTMPText는 건드리지 않는다.
            LocalizedTMPText localizer = levelText != null ? levelText.GetComponent<LocalizedTMPText>() : null;
            if (localizer == null || localizer.TextReference == null || !localizer.TextReference.HasReference) return;

            levelFormatReference = localizer.TextReference;
            localizer.enabled = false;
            levelFormatReference.StringChanged += HandleLevelFormatChanged;
            levelFormatSubscribed = true;

            string localized = levelFormatReference.GetLocalizedString();
            if (!string.IsNullOrEmpty(localized)) levelFormat = localized;
        }

        private void UnbindLevelFormat()
        {
            if (levelFormatSubscribed && levelFormatReference != null)
            {
                levelFormatReference.StringChanged -= HandleLevelFormatChanged;
            }

            levelFormatSubscribed = false;
            levelFormatReference = null;
        }

        private void HandleLevelFormatChanged(string localized)
        {
            if (!string.IsNullOrEmpty(localized)) levelFormat = localized;
            ApplyLevel();
        }

        private void ApplyValues()
        {
            ApplyLevel();
            ApplyValue(staminaValueText, staminaFormat, stamina, maxStamina);
            ApplyCorruptionValue();
            RebuildLayout();
        }

        private void ApplyLevel()
        {
            if (levelText == null) return;
            levelText.text = Format(levelFormat, level);
        }

        private void ApplyCorruptionValue()
        {
            if (purificationValueText == null) return;

            // 정화 화면의 퍼센트 표기와 같은 한 자리 소수 규칙이다. Round만으로는 값이 정수일 때
            // 현재 문화권 또는 부동소수 표현에 따라 불필요한 자릿수가 남을 수 있으므로 0.#을 명시한다.
            string current = FormatCorruption(corruption);
            purificationValueText.text = Format(purificationFormat, current, maxCorruption);
        }

        /// <summary>HUD와 정화 화면에서 공통으로 쓰는 한 자리 소수 오염도 표기다.</summary>
        public static string FormatCorruption(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) value = 0d;
            value = Math.Round(Math.Max(0d, value), 1, MidpointRounding.AwayFromZero);
            return value.ToString("0.#", CultureInfo.InvariantCulture);
        }

        private static void ApplyValue(TextMeshProUGUI target, string format, object current, object maximum)
        {
            if (target == null) return;
            target.text = Format(format, current, maximum);
        }

        private static string Format(string format, params object[] arguments)
        {
            if (string.IsNullOrEmpty(format)) return string.Empty;

            try
            {
                return string.Format(CultureInfo.InvariantCulture, format, arguments);
            }
            catch (FormatException)
            {
                Debug.LogWarning($"[CharacterHudTooltipView] 잘못된 툴팁 형식 문자열 '{format}'입니다.");
                return format;
            }
        }

        private void ApplyCharacterName(string localized)
        {
            if (characterNameText == null) return;
            characterNameText.text = localized;
            RebuildLayout();
        }

        private void ResolveReferences()
        {
            if (resolved) return;
            resolved = true;

            Transform characterInfo = FindDeepChild(transform, "CharacterInfo");
            Transform staminaSection = FindDeepChild(transform, "Stamina");
            Transform purificationSection = FindDeepChild(transform, "Purification");
            Transform conditionSection = FindDeepChild(transform, "condition") ?? FindDeepChild(transform, "Condition");

            if (levelText == null) levelText = FindDeepChild(characterInfo, "lb_CharacterLevel")?.GetComponent<TextMeshProUGUI>();
            if (characterNameText == null) characterNameText = FindDeepChild(characterInfo, "lb_CharacterName")?.GetComponent<TextMeshProUGUI>();
            if (staminaValueText == null) staminaValueText = FindDeepChild(staminaSection, "lb_value")?.GetComponent<TextMeshProUGUI>();
            if (purificationValueText == null) purificationValueText = FindDeepChild(purificationSection, "lb_value")?.GetComponent<TextMeshProUGUI>();

            if (activeCondition == null) activeCondition = FindDirectChild(conditionSection, "active")?.gameObject;
            if (availableCondition == null) availableCondition = FindDirectChild(conditionSection, "Available")?.gameObject;
            if (exhaustedCondition == null) exhaustedCondition = FindDirectChild(conditionSection, "Exhausted")?.gameObject;
            if (recoveringCondition == null) recoveringCondition = FindDirectChild(conditionSection, "Recovering")?.gameObject;
            if (recoveryCompleteCondition == null)
            {
                recoveryCompleteCondition = FindDirectChild(conditionSection, "RecoveryComplete")?.gameObject;
            }
        }

        private void CaptureStaticFormats()
        {
            if (string.IsNullOrEmpty(levelFormat) && levelText != null) levelFormat = levelText.text;
            if (string.IsNullOrEmpty(staminaFormat) && staminaValueText != null) staminaFormat = staminaValueText.text;
            if (string.IsNullOrEmpty(purificationFormat) && purificationValueText != null) purificationFormat = purificationValueText.text;
        }

        private static Transform FindDeepChild(Transform root, string childName)
        {
            if (root == null) return null;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (child.name == childName) return child;
                Transform found = FindDeepChild(child, childName);
                if (found != null) return found;
            }

            return null;
        }

        private static Transform FindDirectChild(Transform root, string childName)
        {
            if (root == null) return null;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (child.name == childName) return child;
            }

            return null;
        }

        private static void SetActive(GameObject target, bool active)
        {
            if (target != null && target.activeSelf != active) target.SetActive(active);
        }
    }
}
