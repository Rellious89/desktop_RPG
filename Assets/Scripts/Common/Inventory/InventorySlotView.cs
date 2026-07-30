using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Common
{
    /// <summary>
    /// 인벤토리 슬롯 한 칸(list_item 프리팹). 자기 값을 계산하지 않고
    /// <see cref="InventoryPanel"/>이 넘겨준 것만 그린다.
    ///
    /// 슬롯은 씬(pn_Inventory 프리팹)에 미리 배치된 개수만큼 존재하고 런타임에 새로 만들지 않는다 -
    /// 슬롯 확장과 페이지는 이번 범위가 아니다. 보유 아이템이 슬롯 수보다 적으면 남는 슬롯은
    /// <see cref="SetEmpty"/>로 비운다.
    ///
    /// 참조를 비워두면 프리팹의 기존 오브젝트 이름(sp_ItemIcon / lb_count)으로 자동 탐색한다.
    /// </summary>
    [DisallowMultipleComponent]
    public class InventorySlotView : MonoBehaviour
    {
        private const string IconName = "sp_ItemIcon";
        private const string CountTextName = "lb_count";

        [Header("References (비워두면 프리팹 이름으로 자동 탐색)")]
        [SerializeField] private Image iconImage;
        [SerializeField] private TextMeshProUGUI countText;

        [Tooltip("수량 표시 형식. 이번 단계에서는 수량이 1이어도 그대로 표시한다.")]
        [SerializeField] private string countFormat = "{0}";

        private bool resolved;

        private void Awake()
        {
            ResolveReferences();
        }

        /// <summary>아이템 하나를 표시한다.
        ///
        /// <b>컴포넌트의 enabled만 켜서는 보이지 않는다.</b> 프리팹의 sp_ItemIcon / lb_count는
        /// GameObject 자체가 비활성 상태로 저장되어 있어서, 부모 오브젝트가 꺼져 있으면 Image나
        /// TextMeshProUGUI를 아무리 켜도 화면에 그려지지 않는다 - 그래서 여기서 GameObject를 먼저
        /// 켠 뒤 컴포넌트를 켠다.
        ///
        /// 아이콘 아트가 아직 없는 아이템(정의의 icon이 비어 있음)이어도 수량은 그대로 표시된다.
        /// 그 경우 Image 컴포넌트만 꺼서, 스프라이트 없는 Image가 흰 사각형으로 그려지는 것을 막는다.</summary>
        public void SetItem(Sprite icon, int count)
        {
            ResolveReferences();

            if (iconImage != null)
            {
                iconImage.gameObject.SetActive(true);
                iconImage.sprite = icon;
                iconImage.enabled = icon != null;
            }
            if (countText != null)
            {
                countText.gameObject.SetActive(true);
                countText.text = string.Format(countFormat, count);
                countText.enabled = true;
            }
        }

        /// <summary>보유 아이템이 없는 슬롯. 아이콘과 수량 오브젝트를 끄고 값도 비워 슬롯 배경만
        /// 남긴다. <b>슬롯 자신(list_item)은 절대 끄지 않는다</b> - 빈 칸도 인벤토리 격자의 한
        /// 자리를 그대로 차지해야 배치가 밀리지 않는다.</summary>
        public void SetEmpty()
        {
            ResolveReferences();

            if (iconImage != null)
            {
                iconImage.sprite = null;
                iconImage.enabled = false;
                iconImage.gameObject.SetActive(false);
            }
            if (countText != null)
            {
                countText.text = string.Empty;
                countText.enabled = false;
                countText.gameObject.SetActive(false);
            }
        }

        private void ResolveReferences()
        {
            if (resolved) return;
            resolved = true;

            if (iconImage == null) iconImage = FindChildComponent<Image>(IconName);
            if (countText == null) countText = FindChildComponent<TextMeshProUGUI>(CountTextName);

            if (iconImage == null)
            {
                Debug.LogWarning($"[InventorySlotView] '{name}': 아이콘 Image('{IconName}')를 찾지 못해 " +
                                 "아이템 아이콘이 표시되지 않습니다.", this);
            }
            if (countText == null)
            {
                Debug.LogWarning($"[InventorySlotView] '{name}': 수량 텍스트('{CountTextName}')를 찾지 못해 " +
                                 "보유 수량이 표시되지 않습니다.", this);
            }
        }

        private T FindChildComponent<T>(string childName) where T : Component
        {
            Transform found = FindDeepChild(transform, childName);
            return found != null ? found.GetComponent<T>() : null;
        }

        private static Transform FindDeepChild(Transform root, string childName)
        {
            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (child.name == childName) return child;

                Transform found = FindDeepChild(child, childName);
                if (found != null) return found;
            }
            return null;
        }
    }
}
