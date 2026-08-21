using System.Globalization;
using Common;
using Inventory;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Dungeon
{
    /// <summary>
    /// 정산 결과의 아이템 한 줄. 스냅샷의 아이콘과 실제 획득 수량만 표시한다.
    ///
    /// <b>마우스를 올리면 아이템 툴팁이 뜬다.</b> 띄우는 주인은 씬 Panel_UI의
    /// <see cref="ItemTooltipController"/> 하나이고, 이 줄은 인벤토리 슬롯·던전 보상 미리보기와
    /// <b>같은 컨트롤러</b>를 부모 쪽에서 찾아 쓴다 - 화면마다 새로 만들면 툴팁 인스턴스가 화면
    /// 수만큼 생긴다.
    ///
    /// <b>툴팁의 수량은 이번 세션에 얻은 수량이다.</b> 인벤토리의 보유 수량이 아니라 스냅샷이 들고
    /// 있는 값을 <b>그대로</b> 넘긴다 - 정산 화면의 숫자와 툴팁의 숫자가 어긋나면 어느 쪽이 진짜인지
    /// 알 수 없게 된다. 값이 <c>long</c>인 것도 스냅샷을 그대로 따르기 위한 것이다(int로 좁히면 큰
    /// 값이 조용히 뒤집힌다).
    ///
    /// <b>보상은 이 줄을 지나 움직이지 않는다.</b> 여기서 하는 일은 표시와 Hover 알림뿐이라, 아이템을
    /// 소비하거나 지급하거나 저장하는 경로는 하나도 없다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DungeonResultRewardItemView : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler
    {
        [SerializeField] private Image itemIcon;
        [SerializeField] private TextMeshProUGUI stackCountText;

        private ItemDefinition boundItem;
        private long boundCount;

        private ItemTooltipController tooltipController;
        private bool tooltipControllerResolved;

        /// <summary>이 줄이 지금 그리고 있는 아이템. 스냅샷에 정의가 없으면 null이다.</summary>
        public ItemDefinition BoundItem => boundItem;

        /// <summary>이 줄이 그리고 있는 이번 세션 획득 수량. 그리는 것이 없으면 0이다.</summary>
        public long BoundCount => boundCount;

        /// <summary>결과 한 줄을 그린다. <b>다른 보상으로 바뀌면 떠 있던 툴팁을 내린다</b> - 재사용된
        /// 줄에 예전 아이템의 툴팁이 남으면 화면과 툴팁이 서로 다른 말을 한다.</summary>
        public void Bind(DungeonSessionItemReward reward)
        {
            ItemDefinition definition = reward != null ? reward.ItemDefinition : null;
            long count = reward != null ? reward.Count : 0L;

            if (boundItem != definition || boundCount != count) CancelTooltip();

            boundItem = definition;
            boundCount = count;

            Sprite icon = definition != null ? definition.Icon : null;

            if (itemIcon != null)
            {
                itemIcon.sprite = icon;
                itemIcon.gameObject.SetActive(icon != null);
                itemIcon.enabled = icon != null;
            }

            if (stackCountText != null)
            {
                stackCountText.gameObject.SetActive(true);
                stackCountText.enabled = true;
                stackCountText.text = count.ToString(CultureInfo.InvariantCulture);
            }
        }

        /// <summary>그리던 보상을 비우고 떠 있던 툴팁을 내린다.</summary>
        public void Clear()
        {
            boundItem = null;
            boundCount = 0L;
            CancelTooltip();
        }

        /// <summary>정의가 없는 줄에서는 아무것도 하지 않는다 - 툴팁이 그릴 내용 자체가 없다.</summary>
        public void OnPointerEnter(PointerEventData eventData)
        {
            if (boundItem == null) return;

            ItemTooltipController controller = ResolveTooltipController();
            controller?.RequestShow(this, boundItem, boundCount, transform as RectTransform);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            CancelTooltip();
        }

        /// <summary>패널이 닫히거나 이 줄이 꺼질 때도 툴팁이 남지 않게 한다. 꺼지면 Exit 이벤트가
        /// 오지 않으므로, 이 경로가 없으면 툴팁만 화면에 남는다.</summary>
        private void OnDisable()
        {
            CancelTooltip();
        }

        /// <summary>이 줄이 툴팁의 주인일 때만 내린다 - 다른 줄이 이미 주인이 되었다면 컨트롤러가
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
    }
}
