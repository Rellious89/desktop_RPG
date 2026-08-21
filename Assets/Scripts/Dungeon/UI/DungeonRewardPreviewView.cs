using Common;
using Inventory;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Dungeon
{
    /// <summary>
    /// 대표 보상 미리보기 한 칸(item_item). <see cref="ItemDefinition.Icon"/> 한 장만 그린다.
    ///
    /// <b>수량은 표시하지 않는다.</b> 이 칸은 "이런 것이 나온다"만 보여주는 자리라, 획득 개수도 등장
    /// 확률도 정해져 있지 않다. 프리팹에 있는 lb_count는 그래서 비우고 꺼 두며,
    /// <b>InventoryManager의 보유 수량은 조회하지 않는다</b> - 인벤토리 화면과 값의 의미가 다르기
    /// 때문에 같은 숫자를 여기에 끌어오면 안 된다.
    ///
    /// <b>마우스를 올리면 아이템 툴팁이 뜬다.</b> 띄우는 주인은 씬 Panel_UI의 <see cref="ItemTooltipController"/>
    /// 하나이고, 이 칸은 인벤토리 슬롯과 <b>같은 컨트롤러</b>를 부모 쪽에서 찾아 쓴다 - 화면마다 새로
    /// 만들면 툴팁 인스턴스가 화면 수만큼 생긴다. <b>수량은 넘기지 않는다</b>: 여기에 보여줄 수량이
    /// 없다는 사실은 위와 같고, 툴팁의 수량 칸도 같은 이유로 비고 꺼진다.
    ///
    /// <b>그리던 아이템을 기억한다.</b> 툴팁은 아이콘이 아니라 <see cref="ItemDefinition"/>이 있어야
    /// 이름과 설명을 그린다. 기억한 값은 <see cref="Clear"/>에서 반드시 비워지고, 그때 떠 있던 툴팁도
    /// 함께 내려간다 - 남아 있으면 빈 칸이나 이미 사라진 던전의 보상 툴팁이 화면에 남는다.
    ///
    /// <b>보상은 이 칸을 지나 움직이지 않는다.</b> 여기서 하는 일은 표시와 Hover 알림뿐이라, 아이템을
    /// 소비하거나 지급하거나 저장하는 경로는 하나도 없다.
    /// </summary>
    [DisallowMultipleComponent]
    public class DungeonRewardPreviewView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        [Header("References (에디터에서 직접 연결한다 - 이름으로 찾지 않는다)")]
        [Tooltip("아이템 아이콘을 표시할 Image(sp_portrait). 이 칸에는 Image가 여럿 있으므로 " +
                 "반드시 직접 연결한다.")]
        [SerializeField] private Image iconImage;

        [Tooltip("프리팹의 수량 텍스트(lb_count). 연결하면 비우고 꺼 준다 - 이 화면은 수량을 " +
                 "표시하지 않는다.")]
        [SerializeField] private TextMeshProUGUI countText;

        private bool missingIconWarned;

        private ItemDefinition boundItem;

        private ItemTooltipController tooltipController;
        private bool tooltipControllerResolved;

        /// <summary>지금 표시 중인 아이콘. 검증/디버깅용 읽기 전용 값이다.</summary>
        public Sprite CurrentSprite => iconImage != null ? iconImage.sprite : null;

        /// <summary>이 칸이 지금 그리고 있는 보상 아이템. 빈 칸이면 null이다.</summary>
        public ItemDefinition BoundItem => boundItem;

        /// <summary>보상 아이템 하나를 표시한다. 정의가 없거나 아이콘이 없으면 빈 칸이 된다.
        ///
        /// <b>다른 아이템으로 바뀌면 떠 있던 툴팁을 내린다.</b> 마우스를 올린 채 던전 선택이 바뀌면
        /// 같은 자리의 칸이 다른 보상을 그리게 되는데, 그때 툴팁만 예전 아이템으로 남으면 화면과
        /// 툴팁이 서로 다른 말을 한다.</summary>
        public void Bind(ItemDefinition item)
        {
            if (boundItem != item) CancelTooltip();
            boundItem = item;

            ApplySprite(item != null ? item.Icon : null);
            HideCount();
        }

        /// <summary>표시를 비운다. 떠 있던 툴팁도 함께 내려간다.</summary>
        public void Clear()
        {
            boundItem = null;
            CancelTooltip();

            ApplySprite(null);
            HideCount();
        }

        /// <summary>빈 칸에서는 아무것도 하지 않는다 - 툴팁이 뜰 내용 자체가 없다.</summary>
        public void OnPointerEnter(PointerEventData eventData)
        {
            if (boundItem == null) return;

            ItemTooltipController controller = ResolveTooltipController();
            // 수량을 넘기지 않는 오버로드다 - 이 화면에는 보여줄 수량이 없다.
            controller?.RequestShow(this, boundItem, transform as RectTransform);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            CancelTooltip();
        }

        /// <summary>패널이 닫히거나 이 칸이 꺼질 때도 툴팁이 남지 않게 한다. 꺼지면 Exit 이벤트가
        /// 오지 않으므로, 이 경로가 없으면 툴팁만 화면에 남는다.</summary>
        private void OnDisable()
        {
            CancelTooltip();
        }

        /// <summary>이 칸이 툴팁의 주인일 때만 내린다 - 다른 칸이 이미 주인이 되었다면 컨트롤러가
        /// 알아서 무시한다.</summary>
        private void CancelTooltip()
        {
            if (!tooltipControllerResolved) return;

            tooltipController?.CancelShow(this);
        }

        private ItemTooltipController ResolveTooltipController()
        {
            if (tooltipControllerResolved) return tooltipController;
            tooltipControllerResolved = true;

            tooltipController = ItemTooltipController.FindSharedController(this);
            return tooltipController;
        }

        private void ApplySprite(Sprite sprite)
        {
            if (iconImage == null)
            {
                if (!missingIconWarned)
                {
                    missingIconWarned = true;
                    Debug.LogWarning($"[DungeonRewardPreviewView] '{name}': 아이콘 Image가 연결되지 않아 " +
                                     "보상 아이콘을 표시할 수 없습니다 - sp_portrait의 Image를 연결하세요.", this);
                }
                return;
            }

            iconImage.sprite = sprite;

            // 프리팹에 따라 아이콘 오브젝트 자체가 꺼진 채 저장되어 있을 수 있어서, 보여줄 때는
            // GameObject를 먼저 켠다(컴포넌트만 켜면 화면에 나오지 않는다).
            if (sprite != null && !iconImage.gameObject.activeSelf) iconImage.gameObject.SetActive(true);

            iconImage.enabled = sprite != null;
        }

        /// <summary>수량 표시를 비우고 끈다. GameObject는 그대로 두어 레이아웃이 흔들리지 않게 한다.</summary>
        private void HideCount()
        {
            if (countText == null) return;

            countText.text = string.Empty;
            countText.enabled = false;
        }
    }
}
