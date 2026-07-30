using System.Collections.Generic;
using Common;
using UnityEngine;

namespace Inventory
{
    /// <summary>
    /// 몬스터를 처치할 때마다 <b>테스트용</b> 고정 보상(재화 + 아이템 1개)을 지급하는 연결 컴포넌트.
    /// 지급 근거는 기존 <see cref="Target.AnyTargetDefeated"/> 하나뿐이다 - 처치가 확정되는 순간에만
    /// 발생하는 이벤트라, 키 입력·공격 시작·타격·피격·애니메이션 종료로는 보상이 나가지 않는다.
    /// PlayerProgress(경험치)와 CharacterRoster(행동력)가 같은 이벤트를 구독하는 것과 같은 방식이다.
    ///
    /// <b>랜덤 드랍도 몬스터별 보상 테이블도 아니다.</b> 재화는 고정값이고 아이템은 등록한 순서대로
    /// 순환할 뿐이며, 순환 인덱스는 실행 중에만 쓰고 저장하지 않는다(앱을 다시 켜면 첫 아이템부터 시작).
    /// 정식 보상 규칙이 정해지면 이 컴포넌트를 그것으로 교체하면 된다.
    ///
    /// <b>행동력과 독립적이다.</b> 행동력 소비는 CharacterRoster가 자기 구독에서 따로 처리하므로,
    /// 행동력이 0이 되는 마지막 처치에서도 보상은 그대로 지급된다(둘은 서로의 결과를 보지 않는다).
    /// 중복 처치 이벤트 방어도 각자 자기 <see cref="DefeatEventFilter"/>로 판정한다.
    /// </summary>
    [DisallowMultipleComponent]
    public class TestDefeatRewardDistributor : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("보상을 적용할 InventoryManager. 비워두면 실행 시 InventoryManager.Instance를 쓴다.")]
        [SerializeField] private InventoryManager inventoryManager;

        [Header("Reward (테스트용 고정값)")]
        [Tooltip("몬스터를 한 번 처치할 때마다 지급할 재화.")]
        [Min(0)]
        [SerializeField] private int currencyPerDefeat = 100;

        [Tooltip("처치할 때마다 지급할 아이템. 위에서부터 순서대로 하나씩 순환한다 " +
                 "(빨간 포션 -> 파란 포션 -> 노란 포션 -> 다시 빨간 포션). " +
                 "여기에 등록한 아이템은 InventoryManager의 Item Catalog에도 있어야 한다.")]
        [SerializeField] private List<ItemDefinition> itemCycle = new List<ItemDefinition>();

        [Min(1)]
        [Tooltip("한 번에 지급할 아이템 수량.")]
        [SerializeField] private int itemCountPerDefeat = 1;

        [Header("Toast")]
        [Tooltip("처치 1회당 한 번 표시할 토스트 문구. {0}=재화, {1}=아이템 이름, {2}=아이템 수량.")]
        [SerializeField] private string rewardToastFormat = "획득: +{0} 재화 / {1} x{2}";

        [Tooltip("재화만 지급된 경우(아이템 목록이 비어 있을 때)의 문구. {0}=재화.")]
        [SerializeField] private string currencyOnlyToastFormat = "획득: +{0} 재화";

        // 다음에 지급할 아이템의 인덱스. 테스트 실행 중에만 쓰고 저장하지 않는다.
        private int nextItemIndex;

        // 중복 처치 이벤트 방어. 행동력 쪽과 공유하지 않는다(위 클래스 주석 참고).
        private readonly DefeatEventFilter defeatFilter = new DefeatEventFilter();

        private void OnEnable()
        {
            Target.AnyTargetDefeated += HandleAnyTargetDefeated;
        }

        private void OnDisable()
        {
            Target.AnyTargetDefeated -= HandleAnyTargetDefeated;
        }

        private void HandleAnyTargetDefeated(string targetId)
        {
            if (!defeatFilter.Accept(targetId, this)) return;

            InventoryManager inventory = ResolveInventory();
            if (inventory == null)
            {
                Debug.LogError("[TestDefeatRewardDistributor] InventoryManager를 찾지 못해 보상을 지급하지 " +
                               "못했습니다.", this);
                return;
            }

            ItemDefinition item = TakeNextItem();

            // 재화와 아이템을 한 번의 처리로 적용한다 - 저장도 InventoryChanged도 보상 1회당 한 번이다.
            inventory.ApplyReward(currencyPerDefeat, item, itemCountPerDefeat);

            ShowRewardToast(item);
        }

        /// <summary>이번에 지급할 아이템을 고르고 다음 순번으로 넘긴다. 목록이 비어 있으면 null
        /// (재화만 지급된다). 비어 있는 슬롯은 건너뛰지 않고 그대로 null로 다뤄, 인덱스가 밀려
        /// 순환 순서가 어긋나는 일이 없게 한다 - 대신 설정 실수를 알 수 있게 경고를 남긴다.</summary>
        private ItemDefinition TakeNextItem()
        {
            if (itemCycle == null || itemCycle.Count == 0) return null;

            ItemDefinition item = itemCycle[nextItemIndex % itemCycle.Count];
            nextItemIndex = (nextItemIndex + 1) % itemCycle.Count;

            if (item == null)
            {
                Debug.LogWarning($"[TestDefeatRewardDistributor] Item Cycle에 비어 있는 항목이 있어 이번 " +
                                 "처치에는 아이템이 지급되지 않았습니다.", this);
            }
            return item;
        }

        /// <summary>처치 1회당 정확히 한 번만 호출된다 - 타격이나 키 입력에는 반응하지 않는다.
        /// ToastManager가 없는 구성에서도 값을 확인할 수 있도록 그때는 로그로 남긴다.</summary>
        private void ShowRewardToast(ItemDefinition item)
        {
            string message = item != null
                ? string.Format(rewardToastFormat, currencyPerDefeat, item.DisplayName, itemCountPerDefeat)
                : string.Format(currencyOnlyToastFormat, currencyPerDefeat);

            if (ToastManager.Instance != null)
            {
                ToastManager.Instance.Show(message);
                return;
            }

            Debug.Log($"[TestDefeatRewardDistributor] {message} (ToastManager가 없어 로그로만 표시합니다)");
        }

        private InventoryManager ResolveInventory()
        {
            if (inventoryManager != null) return inventoryManager;

            inventoryManager = InventoryManager.Instance;
            return inventoryManager;
        }
    }
}
