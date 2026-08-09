using Common;
using UnityEngine;

namespace Skill
{
    /// <summary>
    /// 스킬 한 종의 <b>정적 정의</b> - "이 스킬이 무엇인가"만 담는다(식별자, 이름/설명, 아이콘,
    /// 분류 키, 동작 키, 목록 순서). <see cref="Inventory.ItemDefinition"/> /
    /// <see cref="Inventory.CurrencyDefinition"/>과 같은 역할 분담이다.
    ///
    /// <b>여기에 동작은 없다.</b> 데미지 계산, 쿨다운, 사거리, 발동 조건, 연출 같은 값은 하나도 넣지
    /// 않았고 <see cref="BehaviorKey"/>를 실제 동작으로 바꾸는 코드도 없다 - 이번 단계의 범위는
    /// "스킬 표가 존재하고 검증되며 에셋으로 만들어진다"까지다. 쓰이지 않는 필드를 미리 만들어 두면
    /// 나중에 어떤 값이 실제로 쓰이는 값인지 구분할 수 없게 된다.
    ///
    /// <b><see cref="BehaviorKey"/>는 그저 문자열이다.</b> 나중에 이 키를 보고 동작을 고르는 곳이
    /// 생기더라도, 그 해석기는 이 에셋 바깥에 있어야 한다 - 정의가 스스로 동작을 들고 있으면
    /// 데이터와 규칙이 한 덩어리가 되어 표만 고쳐서는 확인할 수 없는 상태가 된다.
    ///
    /// <b>Skill Id는 절대 다른 값으로 대체하지 않는다.</b> 비어 있으면 빈 문자열이며 에셋 파일
    /// 이름도 표시 이름도 대신 쓰지 않는다(<see cref="Inventory.CurrencyDefinition"/>과 같은 규칙).
    /// <b>자동 정규화도 일절 하지 않는다.</b>
    /// </summary>
    [CreateAssetMenu(fileName = "SkillDefinition", menuName = "Skill/Skill Definition")]
    public class SkillDefinition : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("표와 저장 데이터가 이 스킬을 가리키는 키. <b>반드시 직접 적는다</b> - 비워두면 이 " +
                 "스킬은 목록에 나오지 않는다(파일 이름으로 대체하지 않는다). 적은 그대로 쓰이며 " +
                 "대소문자를 구분하고 앞뒤 공백도 떼지 않는다.")]
        [SerializeField] private string skillId;

        [Header("Localization")]
        [Tooltip("화면에 표시할 스킬 이름. 카테고리 번호 + 숫자 키로 지정한다.")]
        [SerializeField] private LocalizedTextReference localizedName = new LocalizedTextReference();

        [Tooltip("스킬 설명. 선택 항목이라 비어 있을 수 있다.")]
        [SerializeField] private LocalizedTextReference localizedDescription = new LocalizedTextReference();

        [Header("Presentation")]
        [Tooltip("스킬을 표시할 때 쓰는 아이콘. 비어 있으면 아이콘 없이 이름만 보여준다.")]
        [SerializeField] private Sprite icon;

        [Header("Classification")]
        [Tooltip("스킬 분류 키(소문자 snake_case). 비어 있을 수 있다 - 분류를 아직 정하지 않았다는 " +
                 "뜻이며, 이 값을 읽어 무엇을 바꾸는 코드는 없다.")]
        [SerializeField] private string skillType;

        [Tooltip("나중에 동작을 고를 때 쓸 키(소문자 snake_case). 비어 있을 수 있다 - <b>지금은 이 " +
                 "값을 해석하는 코드가 없다</b>.")]
        [SerializeField] private string behaviorKey;

        [Header("Ordering")]
        [Tooltip("스킬을 정렬할 때 쓰는 순서 값. 작을수록 앞이다 - 이 값 자체가 목록을 만들지는 " +
                 "않으며, 목록의 순서는 SkillCatalog의 작성 순서가 결정한다.")]
        [SerializeField] private int displayOrder;

        /// <summary>이 스킬을 가리키는 키. 비어 있거나 공백뿐이면 빈 문자열을 돌려주고, 그 외에는
        /// <b>적힌 문자열을 한 글자도 바꾸지 않고 그대로</b> 돌려준다.</summary>
        public string SkillId => string.IsNullOrWhiteSpace(skillId) ? string.Empty : skillId;

        /// <summary>목록에 올릴 수 있는 스킬인지 여부. 지금 기준은 식별자 하나뿐이며, 이름/아이콘이
        /// 비어 있는 것은 표시 문제이지 목록에서 빼야 할 이유가 아니다.</summary>
        public bool IsValid => !string.IsNullOrWhiteSpace(skillId);

        /// <summary>스킬 이름 참조. <b>절대 null을 돌려주지 않는다</b>.</summary>
        public LocalizedTextReference LocalizedName =>
            localizedName ?? (localizedName = new LocalizedTextReference());

        /// <summary>스킬 이름의 Table/Key가 지정되어 있는지 여부(번역 값의 존재를 보장하지는 않는다).</summary>
        public bool HasLocalizedName => localizedName != null && localizedName.HasReference;

        /// <summary>스킬 설명 참조. <b>절대 null을 돌려주지 않는다</b>.</summary>
        public LocalizedTextReference LocalizedDescription =>
            localizedDescription ?? (localizedDescription = new LocalizedTextReference());

        /// <summary>설명의 Table/Key가 지정되어 있는지 여부. 설명은 선택 항목이라 false가 정상이다.</summary>
        public bool HasLocalizedDescription => localizedDescription != null && localizedDescription.HasReference;

        public Sprite Icon => icon;

        /// <summary>분류 키. 지정하지 않았으면 빈 문자열이며, <b>적힌 그대로</b> 돌려준다.</summary>
        public string SkillType => skillType ?? string.Empty;

        /// <summary>동작 키. 지정하지 않았으면 빈 문자열이며, <b>적힌 그대로</b> 돌려준다.
        /// 이 값을 동작으로 바꾸는 코드는 아직 어디에도 없다.</summary>
        public string BehaviorKey => behaviorKey ?? string.Empty;

        /// <summary>정렬용 순서 값. 작을수록 앞이며, 지정하지 않으면 0이다.</summary>
        public int DisplayOrder => displayOrder;
    }
}
