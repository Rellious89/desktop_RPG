using Common;
using UnityEngine;

namespace Inventory
{
    /// <summary>
    /// 재화 한 종의 정의 - "이 재화가 무엇인가"만 담는다(저장 키, 이름, 아이콘, 목록 순서).
    /// <see cref="ItemDefinition"/>과 같은 역할 분담이며, <b>보유 잔액은 여기에 없다</b>.
    /// 잔액은 저장 데이터가 소유하고, 이 에셋은 그 잔액을 화면에 그릴 때 필요한 표시 정보만 제공한다.
    ///
    /// 환전 비율, 상한, 획득 연출 같은 값은 아직 넣지 않는다 - 이번 단계의 범위는 재화 목록을
    /// 표시 정보와 함께 들고 있는 것까지라, 쓰이지 않는 필드를 미리 만들어 두지 않는다.
    ///
    /// <b>Currency Id는 절대 다른 값으로 대체하지 않는다.</b> 비어 있으면 빈 문자열이며 에셋 파일
    /// 이름도 표시 이름도 대신 쓰지 않는다 - 저장 파일과 밸런스 테이블이 공유하는 유일한 키가 파일
    /// 이름을 바꾸는 것만으로 함께 바뀌는 경로를 만들지 않기 위함이다(<see cref="ItemDefinition"/> /
    /// <see cref="Dungeon.MonsterDefinition"/>과 같은 규칙). <b>자동 정규화는 일절 하지 않는다</b> -
    /// 'jewel'과 'Jewel'은 서로 다른 재화이고, '  Jewel  '은 'Jewel'이 아니다.
    /// 비어 있는 재화는 <see cref="IsValid"/>가 false이고 <see cref="CurrencyCatalog"/>가
    /// 목록에서 제외한다.
    /// </summary>
    [CreateAssetMenu(fileName = "CurrencyDefinition", menuName = "Inventory/Currency Definition")]
    public class CurrencyDefinition : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("저장 데이터와 밸런스 테이블이 이 재화를 가리키는 키. <b>반드시 직접 적는다</b> - " +
                 "비워두면 이 재화는 목록에 나오지 않는다(파일 이름으로 대체하지 않는다). " +
                 "적은 그대로 쓰인다 - 대소문자를 구분하고 앞뒤 공백도 떼지 않으니, 실수로 " +
                 "넣은 공백은 직접 지워야 한다. 한 번 정하면 바꾸지 않는다 - 바꾸면 기존 저장 " +
                 "항목과 연결이 끊긴다.")]
        [SerializeField] private string currencyId;

        [Header("Localization")]
        [Tooltip("화면에 표시할 재화 이름. 카테고리 번호 + 숫자 키로 지정한다.")]
        [SerializeField] private LocalizedTextReference localizedName = new LocalizedTextReference();

        [Header("Presentation")]
        [Tooltip("재화를 표시할 때 쓰는 아이콘. 비어 있으면 아이콘 없이 금액만 보여준다.")]
        [SerializeField] private Sprite icon;

        [Header("Ordering")]
        [Tooltip("재화를 정렬할 때 쓰는 순서 값. 작을수록 앞이다 - 이 값 자체가 목록을 만들지는 않으며, " +
                 "목록의 순서는 CurrencyCatalog의 작성 순서가 결정한다.")]
        [SerializeField] private int displayOrder;

        /// <summary>저장 데이터가 이 재화를 가리키는 키. 비어 있거나 공백뿐이면 빈 문자열을 돌려주고,
        /// 그 외에는 <b>적힌 문자열을 한 글자도 바꾸지 않고 그대로</b> 돌려준다 - 에셋 이름으로도
        /// 표시 이름으로도 대체하지 않고, 앞뒤 공백을 떼거나 대소문자를 맞추는 <b>자동 정규화도 하지
        /// 않는다</b>. 손으로 적은 '  Jewel  '이 말없이 'Jewel'이 되면, 저작자가 본 값과 저장 파일에
        /// 들어가는 값이 달라진다 - 잘못 적힌 id는 조용히 고쳐 주는 것이 아니라 찾지 못하는 것으로
        /// 드러나야 한다.</summary>
        public string CurrencyId => string.IsNullOrWhiteSpace(currencyId) ? string.Empty : currencyId;

        /// <summary>목록에 올릴 수 있는 재화인지 여부. 지금 기준은 식별자 하나뿐이며, 이름/아이콘이
        /// 비어 있는 것은 표시 문제이지 목록에서 빼야 할 이유가 아니다.</summary>
        public bool IsValid => !string.IsNullOrWhiteSpace(currencyId);

        /// <summary>재화 이름 참조. 표시하는 쪽이 구독해서 현재 Locale 문자열을 받는다.
        /// <b>절대 null을 돌려주지 않는다</b> - 참조가 비어 있을 수는 있어도 객체 자체는 항상 있다.</summary>
        public LocalizedTextReference LocalizedName => localizedName ?? (localizedName = new LocalizedTextReference());

        /// <summary>재화 이름의 Table/Key가 지정되어 있는지 여부(번역 값의 존재를 보장하지는 않는다).</summary>
        public bool HasLocalizedName => localizedName != null && localizedName.HasReference;

        public Sprite Icon => icon;

        /// <summary>정렬용 순서 값. 작을수록 앞이며, 지정하지 않으면 0이다.</summary>
        public int DisplayOrder => displayOrder;
    }
}
