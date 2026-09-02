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
        [Tooltip("Skill.Icon이 비어 있을 때 복구할 프리팹 제작용 임시 아이콘.")]
        [SerializeField] private Sprite placeholderIcon;

        private SkillDefinition definition;
        private LocalizedTextReference boundName;
        private LocalizedTextReference boundDescription;

        public SkillDefinition Definition => definition;
        public bool HasNameSubscription => boundName != null;
        public bool HasDescriptionSubscription => boundDescription != null;

        public void Bind(SkillDefinition value)
        {
            Unbind();
            definition = value;

            if (definition == null)
            {
                ApplyName(string.Empty);
                ApplyDescription(string.Empty);
                if (cooldownText != null) cooldownText.text = string.Empty;
                if (iconImage != null) iconImage.sprite = placeholderIcon;
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
            if (definition.HasLocalizedDescription)
            {
                boundDescription = definition.LocalizedDescription;
                boundDescription.StringChanged += ApplyDescription;
            }

            if (iconImage != null)
            {
                iconImage.sprite = definition.Icon != null ? definition.Icon : placeholderIcon;
            }
            if (cooldownText != null) cooldownText.text = FormatCooldown(definition.CooldownSeconds);
        }

        public void Unbind()
        {
            if (boundName != null) boundName.StringChanged -= ApplyName;
            if (boundDescription != null) boundDescription.StringChanged -= ApplyDescription;
            boundName = null;
            boundDescription = null;
            definition = null;
        }

        private void OnDisable() => Unbind();
        private void OnDestroy() => Unbind();

        private void ApplyName(string value)
        {
            if (nameText != null) nameText.text = string.IsNullOrEmpty(value) && definition != null
                ? definition.SkillId : value ?? string.Empty;
        }

        private void ApplyDescription(string value)
        {
            if (descriptionText != null) descriptionText.text = value ?? string.Empty;
        }

        public static string FormatCooldown(float seconds)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds)) seconds = 0f;
            return Mathf.Max(0f, seconds).ToString("0.###", CultureInfo.InvariantCulture) + "s";
        }
    }
}
