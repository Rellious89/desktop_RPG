using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Common;
using Dungeon;
using Enemy;
using UnityEngine;

namespace Inventory
{
    /// <summary>
    /// 몬스터를 처치할 때마다 <b>그 몬스터가 가진 드롭 표</b>대로 보상(재화 + 아이템)을 지급하는 연결
    /// 컴포넌트. 지급 근거는 <see cref="MonsterEncounterQueue.MonsterDefeated"/> 하나뿐이다 - 어떤
    /// 몬스터를 처치했는지가 인자로 오는 이벤트라, 드롭 표를 그 몬스터에서 바로 읽을 수 있다.
    ///
    /// <b><see cref="Target.AnyTargetDefeated"/>는 더 이상 구독하지 않는다.</b> 그쪽은 "무언가 처치됐다"만
    /// 알려주므로 무엇을 떨어뜨려야 하는지 알 수 없다. 경험치/킬카운트/행동력/오디오는 지금도 그 이벤트를
    /// 쓰고 있고, 이 컴포넌트가 구독을 옮겼다고 해서 그쪽 의미나 횟수는 전혀 달라지지 않는다.
    ///
    /// <b>고정 순환 지급이 아니다.</b> 예전에는 등록한 아이템을 순서대로 하나씩 돌려 줬지만, 지금은
    /// Monster.csv의 드롭 슬롯 최대 3개를 <b>각각 독립으로</b> 판정한다 - 여러 칸이 동시에 성공할 수 있고,
    /// 하나도 성공하지 않을 수도 있다. 재화만 여전히 고정값이다.
    ///
    /// <b>지급은 처치 1회당 정확히 한 덩어리다.</b> 성공한 칸을 전부 모아
    /// <see cref="InventoryManager.ApplyRewards"/>에 한 번만 넘기므로, 저장도 화면 갱신도 토스트도
    /// 처치당 한 번뿐이다. 이 이벤트는 처치 판정 콜스택 안에서 동기적으로 오므로 여기서 무거운 일을
    /// 하지 않는다 - 판정은 순수 계산이고 I/O는 저장 1회뿐이다.
    ///
    /// <b>행동력과 독립적이다.</b> 행동력 소비는 CharacterRoster가 자기 구독에서 따로 처리하므로,
    /// 행동력이 0이 되는 마지막 처치에서도 보상은 그대로 지급된다(둘은 서로의 결과를 보지 않는다).
    /// </summary>
    [DisallowMultipleComponent]
    public class TestDefeatRewardDistributor : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("보상을 적용할 InventoryManager. 비워두면 실행 시 InventoryManager.Instance를 쓴다.")]
        [SerializeField] private InventoryManager inventoryManager;

        [Tooltip("처치 이벤트를 받을 MonsterEncounterQueue. 비워두면 실행 시 씬에서 하나를 찾아 쓴다 - " +
                 "찾지 못하면 보상이 전혀 지급되지 않으므로 오류를 남긴다.")]
        [SerializeField] private MonsterEncounterQueue encounterQueue;

        [Header("Reward")]
        [Tooltip("몬스터를 한 번 처치할 때마다 지급할 재화. 아이템 드롭과 무관하게 항상 지급된다.")]
        [Min(0)]
        [SerializeField] private int currencyPerDefeat = 100;

        [Header("Toast")]
        [Tooltip("아이템이 정확히 하나 나왔을 때의 문구. {0}=재화, {1}=아이템 이름, {2}=아이템 수량.")]
        [SerializeField] private string rewardToastFormat = "획득: +{0} 재화 / {1} x{2}";

        [Tooltip("아이템이 둘 이상 나왔을 때의 문구. {0}=재화, {1}=아이템 요약(쉼표로 이어 붙인 목록). " +
                 "위 문구와 자리표시자 수가 달라 따로 둔다.")]
        [SerializeField] private string multiRewardToastFormat = "획득: +{0} 재화 / {1}";

        [Tooltip("아이템이 하나도 나오지 않은 경우의 문구. {0}=재화.")]
        [SerializeField] private string currencyOnlyToastFormat = "획득: +{0} 재화";

        /// <summary>실제 게임이 쓰는 난수원. 판정 함수는 이것을 <b>인자로 받으므로</b>, 검증은 같은
        /// 함수에 결정적인 값을 넣어 확인할 수 있다(판정 코드에 테스트 전용 분기를 두지 않는다).</summary>
        private static readonly Func<float> UnityRoll = () => UnityEngine.Random.value;

        /// <summary>드롭 슬롯 최대 개수(Monster.csv의 고정 3세트). 결과 목록의 초기 크기이자,
        /// <see cref="RollDrops"/>가 <b>실제로 판정하는 칸 수의 상한</b>이다.</summary>
        private const int DropSlotCapacity = 3;

        // 지금 실제로 구독 중인 큐. 구독한 그 인스턴스에서만 해제하므로 이중 구독도, 남은 구독도 없다.
        private MonsterEncounterQueue subscribedQueue;

        private bool started;

        private void OnEnable()
        {
            // 첫 활성화에서 큐를 못 찾는 것은 정상일 수 있다(다른 오브젝트의 Awake가 아직 남았을 수 있다) -
            // 그때는 Start가 마지막으로 한 번 더 시도하고, 거기서도 없으면 그때 오류를 남긴다.
            TrySubscribe(logIfMissing: started);
        }

        private void Start()
        {
            started = true;
            TrySubscribe(logIfMissing: true);
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        /// <summary>큐를 찾아 <b>정확히 한 번</b> 구독한다. 이미 구독 중이면 아무것도 하지 않는다 -
        /// OnEnable과 Start가 모두 지나가도 구독은 하나뿐이다.</summary>
        private void TrySubscribe(bool logIfMissing)
        {
            if (subscribedQueue != null) return;

            MonsterEncounterQueue queue = ResolveQueue();
            if (queue == null)
            {
                if (logIfMissing)
                {
                    Debug.LogError("[TestDefeatRewardDistributor] MonsterEncounterQueue를 찾지 못해 처치 보상을 " +
                                   "지급할 수 없습니다 - Inspector의 Encounter Queue에 대기열을 연결하세요.", this);
                }
                return;
            }

            queue.MonsterDefeated += HandleMonsterDefeated;
            subscribedQueue = queue;
        }

        private void Unsubscribe()
        {
            if (subscribedQueue == null) return;

            subscribedQueue.MonsterDefeated -= HandleMonsterDefeated;
            subscribedQueue = null;
        }

        private MonsterEncounterQueue ResolveQueue()
        {
            if (encounterQueue != null) return encounterQueue;

            // 씬을 고치지 않고도 동작하도록 남겨 둔 경로. 비활성 오브젝트까지 찾는 이유는 필드 모드가
            // 마을에서 대기열 루트를 꺼 두기 때문이다 - 꺼져 있어도 컴포넌트 인스턴스는 같은 것이다.
            encounterQueue = FindObjectOfType<MonsterEncounterQueue>(true);
            return encounterQueue;
        }

        /// <summary>
        /// 처치 판정 콜스택 안에서 동기적으로 호출된다(대기열이 Current 처치 1회당 정확히 한 번 보낸다).
        /// Definition을 모르는 경로나 Standby/Exiting 처치에는 이벤트 자체가 오지 않으므로, 여기서
        /// "정말 처치였는지"를 다시 판단할 필요가 없다.
        /// </summary>
        private void HandleMonsterDefeated(MonsterDefinition defeatedMonster)
        {
            InventoryManager inventory = ResolveInventory();
            if (inventory == null)
            {
                Debug.LogError("[TestDefeatRewardDistributor] InventoryManager를 찾지 못해 보상을 지급하지 " +
                               "못했습니다.", this);
                return;
            }

            // 이번 처치의 결과는 이 호출만의 지역 값이다. 멤버 버퍼를 재사용하면, 지급 도중
            // InventoryChanged 구독자가 동기적으로 또 다른 처치 흐름을 일으켰을 때 안쪽 호출이 버퍼를
            // 비워 바깥 호출의 토스트가 엉뚱한 내용이 된다 - 칸이 최대 셋뿐이라 매번 새로 만든다.
            var rewards = new List<InventoryManager.RewardItemStack>(DropSlotCapacity);
            RollDrops(defeatedMonster != null ? defeatedMonster.Drops : null, UnityRoll, rewards);

            // 재화와 성공한 아이템을 한 덩어리로 적용한다 - 저장도 InventoryChanged도 여기서 한 번뿐이다.
            inventory.ApplyRewards(currencyPerDefeat, rewards);

            ShowRewardToast(rewards);
        }

        /// <summary>
        /// 드롭 슬롯을 <b>각각 독립으로</b> 판정해 성공한 칸만 <paramref name="results"/>에 담는다.
        /// 부작용이 없는 순수 계산이며 <see cref="UnityEngine.Random"/>을 직접 부르지 않는다 - 난수원을
        /// 인자로 받으므로 같은 함수에 정해진 값을 넣어 결과를 확인할 수 있다.
        ///
        /// <b>앞에서부터 최대 <see cref="DropSlotCapacity"/>칸까지만 본다.</b> Monster.csv가 고정 3세트인
        /// 것과 별개로 <b>런타임도 같은 상한을 스스로 지킨다</b> - 손으로 만들었거나 잘못 편집돼 4칸 이상을
        /// 가진 MonsterDefinition이 오면 넘치는 칸은 <b>난수도 뽑지 않고 그대로 무시</b>한다(지급되지
        /// 않는다). 데이터 한쪽이 오염돼도 지급 규칙이 조용히 늘어나지 않게 하기 위함이다.
        ///
        /// 판정 규칙:
        /// <list type="bullet">
        /// <item>빈 칸/아이템 없는 칸/확률이 0 이하인 칸은 <b>난수를 뽑지 않고</b> 건너뛴다.</item>
        /// <item>확률이 100 이상이면 <b>난수를 뽑지 않고</b> 항상 성공한다(항상 주는 보상이 난수에 걸리지 않는다).</item>
        /// <item>그 밖에는 확률을 <b>먼저</b> 0~1 문턱값으로 바꾼 뒤 <c>roll() &lt; 문턱값</c>으로 본다.
        /// 25는 25%, 0.5는 0.5%다.</item>
        /// </list>
        ///
        /// <b>비교는 난수와 같은 영역(0~1)에서 한다.</b> <c>roll * 100 &lt; 확률</c>처럼 난수 쪽을 키우면
        /// 곱셈이 만든 오차 때문에 경계값의 판정이 1 ULP 차이로 뒤집힌다(0.5%에서 roll 0.005가 성공으로
        /// 새는 식이다). 확률을 한 번 나눠 문턱값으로 만들어 두면 <c>0.5f / 100f</c>와 <c>0.005f</c>가
        /// 같은 float이 되어 경계가 정확히 배타적으로 갈린다.
        ///
        /// <b>같은 아이템이 여러 칸에서 성공하면 한 칸으로 합친다.</b> 합치는 기준은 ItemId이고, 순서는
        /// <b>처음 성공한 칸의 자리</b>를 유지한다 - 같은 입력이면 결과 목록의 순서와 수량이 언제나 같다.
        /// 인벤토리 자체도 같은 id를 하나로 누적하지만, 여기서 미리 합쳐 두면 토스트 문구에 같은 아이템이
        /// 두 번 나오지 않는다.
        /// </summary>
        public static void RollDrops(
            IReadOnlyList<MonsterDefinition.DropEntry> drops,
            Func<float> roll,
            List<InventoryManager.RewardItemStack> results)
        {
            if (results == null) return;
            if (drops == null || roll == null) return;

            // 슬롯 수 상한은 <b>여기서도</b> 지킨다. 임포터가 만드는 에셋은 언제나 3칸 이하지만, 사람이
            // 손으로 만들었거나 잘못 편집된 MonsterDefinition에는 4칸 이상이 들어 있을 수 있다 - 그것을
            // 그대로 돌리면 표에 없는 보상이 조용히 지급된다. 넘치는 칸은 난수도 뽑지 않고 무시한다.
            int slotCount = Mathf.Min(drops.Count, DropSlotCapacity);

            for (int i = 0; i < slotCount; i++)
            {
                MonsterDefinition.DropEntry drop = drops[i];
                if (drop == null || !drop.IsValid) continue;

                ItemDefinition item = drop.Item;
                if (item == null) continue;

                float chance = drop.ChancePercent;
                if (chance <= 0f) continue;

                if (chance < 100f)
                {
                    float threshold = chance / 100f;
                    if (roll() >= threshold) continue;
                }

                Accumulate(results, item, drop.Count);
            }
        }

        /// <summary>같은 ItemId가 이미 있으면 수량만 더하고, 없으면 뒤에 새 칸으로 붙인다(첫 등장 순서 유지).</summary>
        private static void Accumulate(
            List<InventoryManager.RewardItemStack> results, ItemDefinition item, int count)
        {
            if (count <= 0) return;

            string itemId = item.ItemId;
            for (int i = 0; i < results.Count; i++)
            {
                if (results[i].Definition == null) continue;
                if (!string.Equals(results[i].Definition.ItemId, itemId, StringComparison.Ordinal)) continue;

                results[i] = new InventoryManager.RewardItemStack(
                    results[i].Definition, results[i].Count + count);
                return;
            }

            results.Add(new InventoryManager.RewardItemStack(item, count));
        }

        /// <summary>처치 1회당 정확히 한 번만 호출된다 - 성공한 칸이 몇 개든 문구는 하나다.
        /// 아이템 수에 따라 자리표시자 수가 다른 문구를 골라 쓴다(3자리 문구에 목록을 넣어 형식 오류가
        /// 나지 않게 한다). ToastManager가 없는 구성에서도 값을 확인할 수 있도록 그때는 로그로 남긴다.</summary>
        private void ShowRewardToast(List<InventoryManager.RewardItemStack> rewards)
        {
            string message;

            if (rewards.Count == 0)
            {
                message = string.Format(currencyOnlyToastFormat, currencyPerDefeat);
            }
            else if (rewards.Count == 1)
            {
                message = string.Format(
                    rewardToastFormat, currencyPerDefeat, DescribeName(rewards[0]), rewards[0].Count);
            }
            else
            {
                message = string.Format(multiRewardToastFormat, currencyPerDefeat, DescribeAll(rewards));
            }

            if (ToastManager.Instance != null)
            {
                ToastManager.Instance.Show(message);
                return;
            }

            Debug.Log($"[TestDefeatRewardDistributor] {message} (ToastManager가 없어 로그로만 표시합니다)");
        }

        private static string DescribeName(InventoryManager.RewardItemStack stack)
        {
            return stack.Definition != null ? stack.Definition.DisplayName : string.Empty;
        }

        private static string DescribeAll(List<InventoryManager.RewardItemStack> rewards)
        {
            var builder = new StringBuilder();
            for (int i = 0; i < rewards.Count; i++)
            {
                if (builder.Length > 0) builder.Append(", ");
                builder.Append(DescribeName(rewards[i]))
                    .Append(" x")
                    .Append(rewards[i].Count.ToString(CultureInfo.InvariantCulture));
            }

            return builder.ToString();
        }

        private InventoryManager ResolveInventory()
        {
            if (inventoryManager != null) return inventoryManager;

            inventoryManager = InventoryManager.Instance;
            return inventoryManager;
        }
    }
}
