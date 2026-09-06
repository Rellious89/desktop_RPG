using System;
using System.Globalization;
using Common;
using Skill;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CharacterArchive
{
    /// <summary>캐릭터 정보 화면의 스킬 한 줄을 표시한다. 해금 판정과 목록 구성은 소유하지 않는다.</summary>
    [DisallowMultipleComponent]
    public sealed class SkillListItemView : MonoBehaviour
    {
        [SerializeField] private Image iconImage;
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text descriptionText;
        [SerializeField] private TMP_Text cooldownText;
        [Header("Locked Info (Inspector에서만 연결)")]
        [SerializeField] private GameObject unlockedNameRoot;
        [SerializeField] private GameObject cooldownRoot;
        [SerializeField] private GameObject lockedInfoRoot;
        [SerializeField] private TMP_Text lockedNameText;
        [SerializeField] private TMP_Text lockedDescriptionText;
        [SerializeField] private LocalizedTMPText lockedDescriptionLocalizer;
        [Tooltip("Skill.Icon이 비어 있을 때 복구할 프리팹 제작용 임시 아이콘.")]
        [SerializeField] private Sprite placeholderIcon;

        private SkillDefinition definition;
        private LocalizedTextReference boundName;
        private LocalizedTextReference boundDescription;
        private LocalizedTextReference boundLockedDescriptionFormat;
        private int requiredCharacterLevel = 1;

        public SkillDefinition Definition => definition;
        public bool HasNameSubscription => boundName != null;
        public bool HasDescriptionSubscription => boundDescription != null;
        public bool HasLockedDescriptionSubscription => boundLockedDescriptionFormat != null;

        public void Bind(SkillDefinition value)
        {
            Bind(null, value, true);
        }

        public void Bind(CharacterSkillDefinition relation, SkillDefinition value, bool unlocked)
        {
            Unbind();
            definition = value;
            requiredCharacterLevel = relation != null ? relation.RequiredCharacterLevel : 1;
            // 이 문구는 레벨 인자가 필요하다. 정적 컴포넌트가 포맷 원문을 덮어쓰지 않도록
            // 참조만 빌리고 구독/표시는 이 행이 소유한다.
            if (lockedDescriptionLocalizer != null) lockedDescriptionLocalizer.enabled = false;

            if (definition == null)
            {
                ApplyName(string.Empty);
                ApplyDescription(string.Empty);
                if (cooldownText != null) cooldownText.text = string.Empty;
                if (iconImage != null) iconImage.sprite = placeholderIcon;
                ApplyLockedDescription(string.Empty);
                SetState(true);
                return;
            }

            // 로컬라이즈 비동기 로드 전에는 숫자 키를 동기 조회해 노출하지 않는다.
            // 이름은 안정적인 id, 선택 설명은 빈 문자열을 잠깐 표시한다.
            ApplyName(definition.SkillId);
            ApplyDescription(string.Empty);
            if (definition.HasLocalizedName)
            {
                boundName = definition.LocalizedName;
                boundName.StringChanged += ApplyName;
            }
            if (unlocked && definition.HasLocalizedDescription)
            {
                boundDescription = definition.LocalizedDescription;
                boundDescription.StringChanged += ApplyDescription;
            }

            if (!unlocked)
            {
                boundLockedDescriptionFormat = lockedDescriptionLocalizer != null
                    ? lockedDescriptionLocalizer.TextReference : null;
                if (boundLockedDescriptionFormat != null && boundLockedDescriptionFormat.HasReference)
                    boundLockedDescriptionFormat.StringChanged += ApplyLockedDescription;
                else ApplyLockedDescription(string.Empty);
            }

            if (iconImage != null)
            {
                iconImage.sprite = definition.Icon != null ? definition.Icon : placeholderIcon;
            }
            if (cooldownText != null) cooldownText.text = FormatCooldown(definition.CooldownSeconds);
            SetState(unlocked);
        }

        public void Unbind()
        {
            if (boundName != null) boundName.StringChanged -= ApplyName;
            if (boundDescription != null) boundDescription.StringChanged -= ApplyDescription;
            if (boundLockedDescriptionFormat != null) boundLockedDescriptionFormat.StringChanged -= ApplyLockedDescription;
            boundName = null;
            boundDescription = null;
            boundLockedDescriptionFormat = null;
            definition = null;
            requiredCharacterLevel = 1;
        }

        private void OnDisable() => Unbind();
        private void OnDestroy() => Unbind();

        private void ApplyName(string value)
        {
            string display = string.IsNullOrEmpty(value) && definition != null ? definition.SkillId : value ?? string.Empty;
            if (nameText != null) nameText.text = display;
            if (lockedNameText != null) lockedNameText.text = display;
        }

        private void ApplyDescription(string value)
        {
            if (descriptionText != null) descriptionText.text = value ?? string.Empty;
        }

        private void ApplyLockedDescription(string format)
        {
            if (lockedDescriptionText == null) return;
            try
            {
                lockedDescriptionText.text = string.Format(CultureInfo.CurrentCulture,
                    string.IsNullOrEmpty(format) ? "Lv. {0}" : format, requiredCharacterLevel);
            }
            catch (FormatException)
            {
                lockedDescriptionText.text = string.Format(CultureInfo.InvariantCulture, "Lv. {0}", requiredCharacterLevel);
            }
        }

        private void SetState(bool unlocked)
        {
            SetActive(unlockedNameRoot, unlocked);
            SetActive(cooldownRoot, unlocked);
            SetActive(lockedInfoRoot, !unlocked);
        }

        private static void SetActive(GameObject target, bool value)
        {
            if (target != null && target.activeSelf != value) target.SetActive(value);
        }

        public static string FormatCooldown(float seconds)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds)) seconds = 0f;
            return Mathf.Max(0f, seconds).ToString("0.###", CultureInfo.InvariantCulture) + "s";
        }
    }
}
