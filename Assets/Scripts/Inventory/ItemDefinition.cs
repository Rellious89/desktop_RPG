using Common;
using UnityEngine;

namespace Inventory
{
    /// <summary>
    /// 아이템 한 종의 정의 - "이 아이템이 무엇인가"만 담는다(저장 키, 이름, 아이콘, 목록 순서).
    /// CharacterDefinition과 같은 역할 분담이며, <b>보유 수량은 여기에 없다</b>.
    /// 수량은 저장 데이터(SaveData.items)가 소유하고, 이 에셋은 그 수량을 화면에 그릴 때 필요한
    /// 표시 정보만 제공한다.
    ///
    /// 사용 효과, 장착 슬롯, 가격, 등급 같은 값은 아직 넣지 않는다 - 이번 단계의 인벤토리는 보유
    /// 목록 표시까지가 범위라, 쓰이지 않는 필드를 미리 만들어 두지 않는다.
    ///
    /// <b>Item Id는 절대 다른 값으로 대체하지 않는다.</b> 비어 있으면 빈 문자열이며 에셋 파일 이름을
    /// 대신 쓰지 않는다 - 저장 파일의 유일한 키가 파일 이름을 바꾸는 것만으로 함께 바뀌는 경로를
    /// 만들지 않기 위함이다(<see cref="Dungeon.WorldDefinition"/> / <see cref="Dungeon.MonsterDefinition"/>과
    /// 같은 규칙). 비어 있는 아이템은 <see cref="IsValid"/>가 false이고
    /// <see cref="ItemCatalog"/>가 목록에서 제외한다.
    /// </summary>
    [CreateAssetMenu(fileName = "ItemDefinition", menuName = "Inventory/Item Definition")]
    public class ItemDefinition : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("저장 데이터에서 이 아이템을 가리키는 키. <b>반드시 직접 적는다</b> - 비워두면 이 아이템은 " +
                 "목록에 나오지 않는다(파일 이름으로 대체하지 않는다). 한 번 정하면 바꾸지 않는다 - " +
                 "바꾸면 기존 저장 항목과 연결이 끊긴다.")]
        [SerializeField] private string itemId;

        [Tooltip("로컬라이징 이전에 쓰던 표시 이름. 새로 만드는 아이템은 아래 Localized Name을 쓰고 " +
                 "이 칸은 비워 둔다 - Item.csv 임포터는 이 칸을 읽지도 쓰지도 않는다.")]
        [SerializeField] private string displayName;

        [Header("Localization")]
        [Tooltip("화면에 표시할 아이템 이름. 카테고리 번호 + 숫자 키로 지정한다.")]
        [SerializeField] private LocalizedTextReference localizedName = new LocalizedTextReference();

        [Header("Presentation")]
        [Tooltip("인벤토리 슬롯(sp_ItemIcon)에 표시할 아이콘. 비어 있으면 슬롯은 아이콘 없이 " +
                 "수량만 보여준다.")]
        [SerializeField] private Sprite icon;

        [Header("Ordering")]
        [Tooltip("아이템을 정렬할 때 쓰는 순서 값. 작을수록 앞이다 - 이 값 자체가 목록을 만들지는 않으며, " +
                 "목록의 순서는 ItemCatalog의 작성 순서가 결정한다.")]
        [SerializeField] private int displayOrder;

        /// <summary>저장 데이터가 이 아이템을 가리키는 키. 비어 있으면 빈 문자열을 돌려준다 -
        /// <b>에셋 이름으로 대체하지 않는다</b>. 앞뒤 공백은 제거해서, 공백만 적힌 값이 유효한 id처럼
        /// 보이지 않게 한다.</summary>
        public string ItemId => string.IsNullOrWhiteSpace(itemId) ? string.Empty : itemId.Trim();

        /// <summary>목록에 올릴 수 있는 아이템인지 여부. 지금 기준은 식별자 하나뿐이며, 이름/아이콘이
        /// 비어 있는 것은 표시 문제이지 목록에서 빼야 할 이유가 아니다.</summary>
        public bool IsValid => !string.IsNullOrWhiteSpace(itemId);

        /// <summary>로컬라이징 이전 경로에서 쓰던 표시 이름. 비어 있으면 <see cref="ItemId"/>를 돌려준다.
        /// 새 코드는 <see cref="LocalizedName"/>을 쓴다.</summary>
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? ItemId : displayName;

        /// <summary>아이템 이름 참조. 표시하는 쪽이 구독해서 현재 Locale 문자열을 받는다.
        /// <b>절대 null을 돌려주지 않는다</b> - 참조가 비어 있을 수는 있어도 객체 자체는 항상 있다.</summary>
        public LocalizedTextReference LocalizedName => localizedName ?? (localizedName = new LocalizedTextReference());

        /// <summary>아이템 이름의 Table/Key가 지정되어 있는지 여부(번역 값의 존재를 보장하지는 않는다).</summary>
        public bool HasLocalizedName => localizedName != null && localizedName.HasReference;

        public Sprite Icon => icon;

        /// <summary>정렬용 순서 값. 작을수록 앞이며, 지정하지 않으면 0이다.</summary>
        public int DisplayOrder => displayOrder;
    }
}
