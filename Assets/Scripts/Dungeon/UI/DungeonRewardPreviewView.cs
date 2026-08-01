using Inventory;
using TMPro;
using UnityEngine;
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
    /// </summary>
    [DisallowMultipleComponent]
    public class DungeonRewardPreviewView : MonoBehaviour
    {
        [Header("References (에디터에서 직접 연결한다 - 이름으로 찾지 않는다)")]
        [Tooltip("아이템 아이콘을 표시할 Image(sp_portrait). 이 칸에는 Image가 여럿 있으므로 " +
                 "반드시 직접 연결한다.")]
        [SerializeField] private Image iconImage;

        [Tooltip("프리팹의 수량 텍스트(lb_count). 연결하면 비우고 꺼 준다 - 이 화면은 수량을 " +
                 "표시하지 않는다.")]
        [SerializeField] private TextMeshProUGUI countText;

        private bool missingIconWarned;

        /// <summary>지금 표시 중인 아이콘. 검증/디버깅용 읽기 전용 값이다.</summary>
        public Sprite CurrentSprite => iconImage != null ? iconImage.sprite : null;

        /// <summary>보상 아이템 하나를 표시한다. 정의가 없거나 아이콘이 없으면 빈 칸이 된다.</summary>
        public void Bind(ItemDefinition item)
        {
            ApplySprite(item != null ? item.Icon : null);
            HideCount();
        }

        /// <summary>표시를 비운다.</summary>
        public void Clear()
        {
            ApplySprite(null);
            HideCount();
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
