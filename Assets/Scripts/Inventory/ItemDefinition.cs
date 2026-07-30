using UnityEngine;

namespace Inventory
{
    /// <summary>
    /// 아이템 한 종의 정의 - "이 아이템이 무엇인가"만 담는다(저장 키, 이름, 아이콘).
    /// CharacterDefinition과 같은 역할 분담이며, <b>보유 수량은 여기에 없다</b>.
    /// 수량은 저장 데이터(SaveData.items)가 소유하고, 이 에셋은 그 수량을 화면에 그릴 때 필요한
    /// 표시 정보만 제공한다.
    ///
    /// 사용 효과, 장착 슬롯, 가격, 등급 같은 값은 아직 넣지 않는다 - 이번 단계의 인벤토리는 보유
    /// 목록 표시까지가 범위라, 쓰이지 않는 필드를 미리 만들어 두지 않는다.
    /// </summary>
    [CreateAssetMenu(fileName = "ItemDefinition", menuName = "Inventory/Item Definition")]
    public class ItemDefinition : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("저장 데이터에서 이 아이템을 가리키는 키. 비워두면 에셋 파일 이름을 쓴다. " +
                 "한 번 정하면 바꾸지 않는다 - 바꾸면 기존 저장 항목과 연결이 끊긴다.")]
        [SerializeField] private string itemId;

        [Tooltip("아이템 이름. 이번 단계의 인벤토리 슬롯에는 표시하지 않고(아이콘 + 수량만 표시), " +
                 "로그와 이후 상세 정보창을 위해 둔다.")]
        [SerializeField] private string displayName;

        [Header("Presentation")]
        [Tooltip("인벤토리 슬롯(sp_ItemIcon)에 표시할 아이콘. 비어 있으면 슬롯은 아이콘 없이 " +
                 "수량만 보여준다.")]
        [SerializeField] private Sprite icon;

        public string ItemId => string.IsNullOrWhiteSpace(itemId) ? name : itemId;

        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? ItemId : displayName;

        public Sprite Icon => icon;
    }
}
