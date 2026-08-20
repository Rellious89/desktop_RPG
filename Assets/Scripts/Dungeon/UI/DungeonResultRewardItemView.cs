using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Dungeon
{
    /// <summary>정산 결과의 아이템 한 줄. 스냅샷의 아이콘과 실제 획득 수량만 표시한다.</summary>
    [DisallowMultipleComponent]
    public sealed class DungeonResultRewardItemView : MonoBehaviour
    {
        [SerializeField] private Image itemIcon;
        [SerializeField] private TextMeshProUGUI stackCountText;

        public void Bind(DungeonSessionItemReward reward)
        {
            Sprite icon = reward != null && reward.ItemDefinition != null
                ? reward.ItemDefinition.Icon
                : null;

            if (itemIcon != null)
            {
                itemIcon.sprite = icon;
                itemIcon.gameObject.SetActive(icon != null);
                itemIcon.enabled = icon != null;
            }

            if (stackCountText != null)
            {
                long count = reward != null ? reward.Count : 0L;
                stackCountText.gameObject.SetActive(true);
                stackCountText.enabled = true;
                stackCountText.text = count.ToString(CultureInfo.InvariantCulture);
            }
        }
    }
}
