using System.Collections.Generic;
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
    /// <b>아이템 판정은 처치 1회당 난수 한 번이다.</b> 드롭 슬롯 최대 3개가 0~9999의 <b>누적 구간</b>을
    /// 앞에서부터 나눠 가지며(단위는 만분율, 10000 = 100%), 뽑힌 값이 들어간 구간 하나만 지급된다 -
    /// 그래서 한 번의 처치가 주는 아이템은 <b>최대 한 종류</b>이고, 남은 구간에 들어가면 아무것도 나오지
    /// 않는다. 지급 개수는 <b>뽑힌 그 슬롯의 개수</b>이며, 뽑히지 않은 슬롯의 개수는 절대 더하지 않는다.
    ///
    /// <b>재화는 몬스터가 정한다.</b> 고정 지급액은 없다 - 몬스터에 쓸 수 있는
    /// <see cref="MonsterDefinition.Currency"/>가 연결되어 있지 않으면 0이고, 연결되어 있으면
    /// Min~Max(양 끝 포함) 사이의 금액을 준다. 아이템이 나왔는지와 무관하다.
    ///
    /// <b>지급은 처치 1회당 정확히 한 덩어리다.</b> 아이템(있어도 한 칸)과 재화를 함께
    /// <see cref="InventoryManager.ApplyRewards"/>에 한 번만 넘기므로, 저장도 화면 갱신도 토스트도
    /// 처치당 한 번뿐이다. 받은 것이 하나도 없으면 ApplyRewards가 아무것도 바꾸지 않으므로 저장도
    /// 알림도 일어나지 않고, 토스트도 띄우지 않는다("+0 재화"를 보여 주지 않는다). 이 이벤트는 처치
    /// 판정 콜스택 안에서 동기적으로 오므로 여기서 무거운 일을 하지 않는다 - 판정은 순수 계산이고
    /// I/O는 저장 1회뿐이다.
    ///
    /// <b>행동력과 독립적이다.</b> 행동력 소비는 CharacterRoster가 자기 구독에서 따로 처리하므로,
    /// 행동력이 0이 되는 마지막 처치에서도 보상은 그대로 지급된다(둘은 서로의 결과를 보지 않는다).
    /// </summary>
    [DisallowMultipleComponent]
    public class DefeatRewardDistributor : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("보상을 적용할 InventoryManager. 비워두면 실행 시 InventoryManager.Instance를 쓴다.")]
        [SerializeField] private InventoryManager inventoryManager;

        [Tooltip("처치 이벤트를 받을 MonsterEncounterQueue. 비워두면 실행 시 씬에서 하나를 찾아 쓴다 - " +
                 "찾지 못하면 보상이 전혀 지급되지 않으므로 오류를 남긴다.")]
        [SerializeField] private MonsterEncounterQueue encounterQueue;

        [Header("Toast")]
        [Tooltip("아이템과 재화를 함께 얻었을 때의 문구. {0}=재화, {1}=아이템 이름, {2}=아이템 수량.")]
        [SerializeField] private string rewardToastFormat = "획득: +{0} 재화 / {1} x{2}";

        [Tooltip("아이템만 얻었을 때의 문구(재화 0). {0}=아이템 이름, {1}=아이템 수량. " +
                 "위 문구와 자리표시자 수가 달라 따로 둔다.")]
        [SerializeField] private string itemOnlyToastFormat = "획득: {0} x{1}";

        [Tooltip("재화만 얻었을 때의 문구. {0}=재화.")]
        [SerializeField] private string currencyOnlyToastFormat = "획득: +{0} 재화";

        /// <summary>
        /// 0 이상 <paramref name="exclusiveMax"/> 미만의 정수를 하나 돌려주는 난수원.
        ///
        /// <b>경계는 long으로 주고받는다.</b> 재화 금액이 0~<see cref="int.MaxValue"/>인 몬스터는 양 끝을
        /// 포함하는 구간의 크기가 2^31이라 int에 담기지 않는다 - 경계를 int로 두면 그 한 칸(최댓값)이
        /// 영원히 나오지 않는다. 여기 오는 값의 상한은 <see cref="MaxExclusiveBound"/>다.
        ///
        /// 판정 함수들이 이것을 <b>인자로 받으므로</b>, 검증은 같은 함수에 정해진 값을 넣어 확인할 수
        /// 있다(판정 코드에 테스트 전용 분기를 두지 않는다). 프로젝트 전체에 난수 프레임워크를 두지
        /// 않고 이 파일 안에서만 쓰는 작은 약속으로 남긴다.
        /// </summary>
        public delegate long RandomBelow(long exclusiveMax);

        /// <summary>난수원에 요청할 수 있는 가장 큰 경계. 2^31이며, 재화 범위가 0~int.MaxValue일 때의
        /// 구간 크기(양 끝 포함)와 같다.</summary>
        public const long MaxExclusiveBound = (long)int.MaxValue + 1L;

        /// <summary>실제 게임이 쓰는 난수원.</summary>
        private static readonly RandomBelow UnityRollBelow = RollUnityRandomBelow;

        /// <summary>
        /// 실제 게임의 난수 한 번. <c>Random.Range(int, int)</c>는 위끝을 포함하지 않으므로 결과는
        /// 언제나 0 이상 <paramref name="exclusiveMax"/> 미만이다 - <c>Random.value</c>(실수)는 쓰지 않는다.
        ///
        /// <b>경계가 int에 담기면 요청은 딱 한 번</b>이다(아이템 판정의 0~9999가 여기에 해당한다).
        ///
        /// int로 담기지 않는 경계는 <b>2^31 하나뿐</b>이다 - 재화 구간의 크기는 <c>max - min + 1</c>이고
        /// min은 0 이상, max는 int.MaxValue 이하라 2^31을 넘을 수 없기 때문이다. 그 경우에만 16비트와
        /// 15비트 두 번을 이어 붙여 31비트를 만든다: 두 조각 모두 2의 거듭제곱 범위라 곱이 정확히 2^31이
        /// 되고, 버리는 값도 실수 연산도 없어 <b>균등</b>하다(0과 2^31-1이 모두 나온다).
        /// </summary>
        public static long RollUnityRandomBelow(long exclusiveMax)
        {
            if (exclusiveMax <= 1L) return 0L;
            if (exclusiveMax <= int.MaxValue) return UnityEngine.Random.Range(0, (int)exclusiveMax);

            const int highExclusive = 1 << 16;
            const int lowExclusive = 1 << 15;

            long high = UnityEngine.Random.Range(0, highExclusive);
            long low = UnityEngine.Random.Range(0, lowExclusive);
            long value = (high << 15) | low;

            // 여기 올 수 있는 경계는 2^31뿐이라 이 자름은 실제로는 일어나지 않는다. 약속을 벗어난
            // 요청이 와도 범위 밖 값을 돌려주지 않도록 남겨 두는 방어다.
            return value < exclusiveMax ? value : exclusiveMax - 1L;
        }

        /// <summary>드롭 슬롯 최대 개수(Monster.csv의 고정 3세트). <see cref="SelectDropIndex"/>가
        /// <b>실제로 살펴보는 칸 수의 상한</b>이며, 4번째 이후 칸은 확률에 더해지지도 않는다.</summary>
        private const int DropSlotCapacity = 3;

        /// <summary>아이템 판정 난수의 범위. 0 이상 10000 미만(0~9999)이며, 슬롯 확률의 단위인
        /// 만분율과 같은 눈금을 쓴다 - <b>백분율 실수로 바꾸는 곳은 어디에도 없다</b>.</summary>
        public const int ItemRollRange = MonsterDefinition.DropEntry.ChanceBasisPointsScale;

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
                    Debug.LogError("[DefeatRewardDistributor] MonsterEncounterQueue를 찾지 못해 처치 보상을 " +
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
                Debug.LogError("[DefeatRewardDistributor] InventoryManager를 찾지 못해 보상을 지급하지 " +
                               "못했습니다.", this);
                return;
            }

            DefeatReward reward = BuildReward(defeatedMonster, UnityRollBelow);

            InventoryRewardApplyResult actual =
                inventory.ApplyRewards(reward.Currency, ToRewardStacks(reward));

            if (!actual.IsEmpty) ShowRewardToast(actual);
        }

        /// <summary>보상 결과를 <see cref="InventoryManager.ApplyRewards"/>가 받는 목록으로 바꾼다.
        /// 아이템이 없으면 <b>null</b>을 돌려준다 - 빈 목록을 만들어 넘길 이유가 없다.</summary>
        private static List<InventoryManager.RewardItemStack> ToRewardStacks(DefeatReward reward)
        {
            if (!reward.HasItem) return null;

            return new List<InventoryManager.RewardItemStack>(1)
            {
                new InventoryManager.RewardItemStack(reward.Item, reward.ItemCount),
            };
        }

        /// <summary>
        /// 처치 1회의 보상 결과. 아이템은 <b>있어도 한 종류</b>이고, 재화는 그것과 무관하게 정해진다.
        /// </summary>
        public readonly struct DefeatReward
        {
            public DefeatReward(ItemDefinition item, int itemCount, int currency)
            {
                Item = item;
                ItemCount = itemCount;
                Currency = currency;
            }

            /// <summary>뽑힌 슬롯의 아이템. 아무 구간에도 들어가지 않았으면 null이다.</summary>
            public ItemDefinition Item { get; }

            /// <summary><b>뽑힌 그 슬롯</b>의 개수. 다른 슬롯의 개수는 절대 더하지 않는다.</summary>
            public int ItemCount { get; }

            /// <summary>이번 처치의 재화 금액. 재화 지정이 없으면 0이다.</summary>
            public int Currency { get; }

            public bool HasItem => Item != null && ItemCount > 0;

            public bool HasCurrency => Currency > 0;

            /// <summary>실제로 받은 것이 하나도 없는지. 토스트를 띄울지 여부가 여기에 달려 있다.</summary>
            public bool IsEmpty => !HasItem && !HasCurrency;

            public static DefeatReward None => default;
        }

        /// <summary>
        /// 처치 1회의 보상을 정한다. 부작용이 없는 순수 계산이며 <see cref="UnityEngine.Random"/>을
        /// 직접 부르지 않는다 - 난수원을 인자로 받으므로 같은 함수에 정해진 값을 넣어 결과를 확인할 수 있다.
        ///
        /// <b>아이템 난수는 정확히 한 번</b> 뽑는다. 드롭 슬롯이 하나도 없어도 뽑는 것은 의도적이다 -
        /// "몇 번 뽑았는가"가 데이터에 따라 달라지면 같은 난수원으로 같은 결과를 재현할 수 없다.
        /// 재화 난수는 <b>필요할 때만</b> 뽑는다(지정이 없거나 고정 금액이면 뽑지 않는다).
        /// </summary>
        public static DefeatReward BuildReward(MonsterDefinition monster, RandomBelow roll)
        {
            if (monster == null) return DefeatReward.None;

            long itemRoll = roll != null ? roll(ItemRollRange) : -1L;
            int index = SelectDropIndex(monster.Drops, itemRoll);

            ItemDefinition item = null;
            int count = 0;
            if (index >= 0)
            {
                MonsterDefinition.DropEntry selected = monster.Drops[index];
                item = selected.Item;
                count = selected.Count;
            }

            return new DefeatReward(item, count, RollCurrency(monster, roll));
        }

        /// <summary>
        /// 뽑힌 값 <paramref name="roll"/>(0 이상 <see cref="ItemRollRange"/> 미만)이 어느 슬롯의
        /// <b>누적 구간</b>에 들어가는지 돌려준다. 아무 구간에도 들어가지 않으면 -1이며, 그것이
        /// "아이템 없음"이다.
        ///
        /// 슬롯은 앞에서부터 자기 확률(만분율)만큼의 구간을 차지한다 - 3000과 2000이면 0~2999가 첫째,
        /// 3000~4999가 둘째이고 5000~9999는 아무것도 나오지 않는다. 합이 10000이면 빈 구간이 없다.
        /// <b>구간에 들어가는 순간 멈춘다</b>: 한 번의 처치가 주는 아이템은 최대 한 종류다.
        ///
        /// <b>앞에서부터 최대 <see cref="DropSlotCapacity"/>칸까지만 본다.</b> Monster.csv가 고정 3세트인
        /// 것과 별개로 <b>런타임도 같은 상한을 스스로 지킨다</b> - 손으로 만들었거나 잘못 편집돼 4칸 이상을
        /// 가진 MonsterDefinition이 오면 넘치는 칸은 확률에 더해지지도, 뽑히지도 않는다.
        ///
        /// 아이템이 없거나 확률이 유효 범위(1~10000)를 벗어난 칸은 <b>구간을 차지하지 않는다</b> -
        /// 그런 칸이 뒤 슬롯의 구간을 밀어내지 않게 하기 위함이다. 범위 밖의 난수값은 -1로 답한다
        /// (망가진 난수원이 첫 슬롯을 공짜로 뽑아 주지 않게 한다).
        /// </summary>
        public static int SelectDropIndex(IReadOnlyList<MonsterDefinition.DropEntry> drops, long roll)
        {
            if (drops == null) return -1;
            if (roll < 0L || roll >= ItemRollRange) return -1;

            int slotCount = Mathf.Min(drops.Count, DropSlotCapacity);
            int cumulative = 0;

            for (int i = 0; i < slotCount; i++)
            {
                MonsterDefinition.DropEntry drop = drops[i];

                // IsValid가 아이템 유무와 1~10000 범위를 함께 본다. 확률 0인 칸은 여기서 걸러진다.
                if (drop == null || !drop.IsValid) continue;

                cumulative += drop.ChanceBasisPoints;
                if (roll < cumulative) return i;
            }

            return -1;
        }

        /// <summary>
        /// 이번 처치의 재화 금액. <b>아이템이 나왔는지와 아무 상관이 없다.</b>
        ///
        /// <b>지급의 근거는 연결된 <see cref="CurrencyDefinition"/> 하나뿐이다.</b>
        /// <see cref="MonsterDefinition.HasCurrencyReward"/>가 false면 - 참조가 비었든, 가리키는 정의가
        /// 쓸 수 없는 상태든 - 0이고 <b>난수를 뽑지 않는다</b>. id 문자열만 보고 지급을 허가하는 경로는
        /// 존재하지 않으므로, "표에 없는 재화"가 지급되는 상태를 만들 수 없다.
        ///
        /// 연결이 있으면 Min~Max <b>양 끝을 포함</b>하는 금액을 준다 - 둘이 같으면 고정 금액이라
        /// 역시 난수를 뽑지 않는다.
        ///
        /// <b>재화 종류는 아직 하나뿐이다.</b> 어떤 재화를 가리키든 지금은 <b>기존의 그 하나뿐인 잔액</b>
        /// (SaveData.currency)으로 지급된다 - 재화별 저장소는 아직 없다. 종류가 늘어날 때 무엇을
        /// 지급해야 하는지는 이 참조가 이미 정확히 말해 주므로, 그때 고칠 곳은 저장 쪽 하나다.
        /// </summary>
        public static int RollCurrency(MonsterDefinition monster, RandomBelow roll)
        {
            if (monster == null || !monster.HasCurrencyReward) return 0;

            int min = monster.CurrencyAmountMin;
            int max = monster.CurrencyAmountMax;
            if (max <= min) return min;
            if (roll == null) return min;

            // 양 끝을 포함해야 하므로 구간의 크기는 (max - min + 1)이다. min이 0 이상, max가 int.MaxValue
            // 이하라 이 값은 최대 2^31이며 <b>int에는 담기지 않는다</b> - 경계를 int로 좁히면 그 한 칸,
            // 즉 최댓값이 영원히 나오지 않는다. 그래서 요청도 계산도 long으로 한다.
            long span = (long)max - min + 1L;

            // 난수원이 약속을 어겨도 지급액이 Min~Max 밖으로 나가지 않게 long 상태에서 한 번 더 막는다.
            long offset = roll(span);
            if (offset < 0L) offset = 0L;
            if (offset > span - 1L) offset = span - 1L;

            // 여기서 amount는 min 이상 max 이하임이 증명되었다(offset이 0~span-1이고 min+span-1 == max).
            // int로 좁히는 것은 그 뒤다.
            long amount = min + offset;
            return (int)amount;
        }

        /// <summary>
        /// 처치 1회당 <b>많아야 한 번</b> 호출된다(빈 보상에는 호출되지 않는다). 받은 것의 조합에 따라
        /// 자리표시자 수가 다른 문구를 골라 쓴다 - 아이템+재화 / 아이템만 / 재화만 셋을 각각 두는 이유는
        /// 자리표시자 수가 맞지 않는 문구에 값을 넣으면 형식 오류가 나기 때문이다.
        /// ToastManager가 없는 구성에서도 값을 확인할 수 있도록 그때는 로그로 남긴다.
        /// </summary>
        private void ShowRewardToast(InventoryRewardApplyResult result)
        {
            string message = BuildToastMessage(result);
            if (string.IsNullOrEmpty(message)) return;

            if (ToastManager.Instance != null)
            {
                ToastManager.Instance.Show(message);
                return;
            }

            Debug.Log($"[DefeatRewardDistributor] {message} (ToastManager가 없어 로그로만 표시합니다)");
        }

        private string BuildToastMessage(InventoryRewardApplyResult result)
        {
            bool hasCurrency = result.ActualCurrencyDelta > 0;
            bool hasItem = result.ItemDeltas.Count > 0;

            if (!hasItem && !hasCurrency) return string.Empty;

            string itemName = hasItem ? DescribeName(result.ItemDeltas[0].Definition) : string.Empty;
            int itemCount = hasItem ? result.ItemDeltas[0].ActualCount : 0;

            if (hasItem && hasCurrency)
                return string.Format(rewardToastFormat, result.ActualCurrencyDelta, itemName, itemCount);

            if (hasItem) return string.Format(itemOnlyToastFormat, itemName, itemCount);

            return string.Format(currencyOnlyToastFormat, result.ActualCurrencyDelta);
        }

        private static string DescribeName(ItemDefinition item)
        {
            return item != null ? item.DisplayName : string.Empty;
        }

        private InventoryManager ResolveInventory()
        {
            if (inventoryManager != null) return inventoryManager;

            inventoryManager = InventoryManager.Instance;
            return inventoryManager;
        }
    }
}
