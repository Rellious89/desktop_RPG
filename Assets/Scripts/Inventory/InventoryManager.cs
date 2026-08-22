using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Common;
using UnityEngine;

namespace Inventory
{
    /// <summary>
    /// 보상 적용의 실제 결과 - 아이템 한 칸이 실제로 얼마나 늘었는가. 요청한 수량이 아니라
    /// <b>적용 후 인벤토리에 실제로 더해진 수량</b>이며, 포화(int.MaxValue)로 잘린 분은 제외된다.
    /// 같은 ItemId가 요청에 여러 번 나타나면 하나의 결과로 합산된다(첫 성공 순서 유지).
    /// </summary>
    public readonly struct InventoryRewardItemDelta
    {
        public InventoryRewardItemDelta(ItemDefinition definition, string itemId, int actualCount)
        {
            Definition = definition;
            ItemId = itemId;
            ActualCount = actualCount;
        }

        public ItemDefinition Definition { get; }
        public string ItemId { get; }
        public int ActualCount { get; }
    }

    /// <summary>
    /// <see cref="InventoryManager.ApplyReward"/> / <see cref="InventoryManager.ApplyRewards"/>가
    /// 돌려주는 불변 결과. 실제로 인벤토리에 적용된 양만 담으며, 요청한 양과 다를 수 있다(포화 등).
    /// 외부 코드가 내부 컬렉션이나 스냅샷을 변경할 수 없다.
    /// </summary>
    public sealed class InventoryRewardApplyResult
    {
        public static readonly InventoryRewardApplyResult Empty =
            new InventoryRewardApplyResult(0, Array.Empty<InventoryRewardItemDelta>());

        private readonly ReadOnlyCollection<InventoryRewardItemDelta> items;

        internal InventoryRewardApplyResult(int actualCurrencyDelta, InventoryRewardItemDelta[] itemDeltas)
        {
            ActualCurrencyDelta = actualCurrencyDelta;
            items = Array.AsReadOnly(itemDeltas);
        }

        public int ActualCurrencyDelta { get; }

        public ReadOnlyCollection<InventoryRewardItemDelta> ItemDeltas => items;

        public bool Changed => ActualCurrencyDelta > 0 || items.Count > 0;

        public bool IsEmpty => !Changed;
    }

    /// <summary>
    /// <see cref="InventoryManager.TrySpendCostWithoutSave"/>가 <b>무엇을 뺐고, 빼기 <i>직전</i>의
    /// 인벤토리가 어떤 모양이었는지</b>를 함께 담는 영수증.
    ///
    /// <b>되돌리기는 "뺀 만큼 더하기"가 아니라 "찍어 둔 모양으로 되돌리기"다.</b> 뺀 값만 들고 있다가
    /// 다시 더하면 <b>수량이 정확히 0이 되어 지워졌던 항목</b>이 목록 <b>맨 뒤</b>에 새로 생긴다 -
    /// 저장 목록의 순서가 곧 획득 순서이자 인벤토리 표시 순서이므로, 실패한 지불 하나가 플레이어의
    /// 아이템 배치를 영구히 바꿔 놓는 셈이다. 그래서 여기서는 빼기 직전의 <b>목록 전체</b>를 찍어
    /// 둔다 - 순서, 같은 Id가 두 줄인 손상된 파일, null 항목, 각 항목의 수량, 그리고 <b>항목 객체의
    /// 정체성</b>까지 그대로 되살아난다(다른 코드가 들고 있던 항목 참조가 끊기지 않는다).
    ///
    /// 밖에서 만들 수 없다 - 이 값을 손으로 지어내
    /// <see cref="InventoryManager.RefundCostWithoutSave"/>에 넘기면 <b>내지도 않은 것을 돌려받거나</b>
    /// 남의 인벤토리 모양을 덮어쓰는 경로가 생긴다.
    /// </summary>
    public sealed class InventoryCostReceipt
    {
        /// <summary>아무것도 빼지 않았다는 영수증. 되돌려도 아무 일도 일어나지 않는다.</summary>
        public static readonly InventoryCostReceipt Empty =
            new InventoryCostReceipt(0, Array.Empty<ItemLine>(), 0, Array.Empty<ItemSlot>());

        /// <summary>실제로 빠진 아이템 한 줄(합산이 끝난 값). 보고용이며, 되돌리기는 이 값을 쓰지 않는다.</summary>
        public readonly struct ItemLine
        {
            public ItemLine(string itemId, int count)
            {
                ItemId = itemId;
                Count = count;
            }

            public string ItemId { get; }
            public int Count { get; }
        }

        /// <summary>
        /// 빼기 직전 저장 목록의 한 칸. <b>항목 객체와 그때의 수량을 함께</b> 들고 있다 - 차감은 항목의
        /// <c>count</c>를 <b>제자리에서</b> 고치므로, 객체만 들고 있으면 되돌릴 값이 이미 사라진다.
        /// 빈 칸(null 항목)도 그대로 한 칸을 차지한다.
        /// </summary>
        internal readonly struct ItemSlot
        {
            public ItemSlot(InventoryItemState state, int count)
            {
                State = state;
                Count = count;
            }

            public InventoryItemState State { get; }
            public int Count { get; }
        }

        private readonly ReadOnlyCollection<ItemLine> items;

        /// <summary>빼기 직전의 잔액. "뺀 만큼 더하기"가 아니라 이 값으로 <b>되돌린다</b> - 포화나
        /// 자르기가 끼어들 자리를 남기지 않는다.</summary>
        private readonly int currencyBefore;

        /// <summary>빼기 직전의 목록 전체(순서 그대로).</summary>
        private readonly ItemSlot[] itemsBefore;

        private InventoryCostReceipt(
            int currency, ItemLine[] itemLines, int currencyBefore, ItemSlot[] itemsBefore)
        {
            Currency = currency;
            items = Array.AsReadOnly(itemLines);
            this.currencyBefore = currencyBefore;
            this.itemsBefore = itemsBefore;
        }

        internal InventoryCostReceipt(
            int currency, List<InventoryItemRequirement> requirements,
            int currencyBefore, ItemSlot[] itemsBefore)
        {
            Currency = currency;

            var lines = new ItemLine[requirements?.Count ?? 0];
            for (int i = 0; i < lines.Length; i++)
            {
                lines[i] = new ItemLine(requirements[i].ItemId, requirements[i].Count);
            }

            items = Array.AsReadOnly(lines);
            this.currencyBefore = currencyBefore;
            this.itemsBefore = itemsBefore ?? Array.Empty<ItemSlot>();
        }

        /// <summary>실제로 빠진 재화.</summary>
        public int Currency { get; }

        /// <summary>실제로 빠진 아이템 줄들(차감한 순서 그대로). 절대 null이 아니다.</summary>
        public IReadOnlyList<ItemLine> Items => items;

        /// <summary>되돌릴 것이 하나라도 있는가. 비용이 아예 없던 요청은 false다.</summary>
        public bool Changed => Currency > 0 || items.Count > 0;

        /// <summary>
        /// 찍어 둔 모양으로 문서를 되돌린다. 목록은 <b>새로 만들지 않고 제자리에서</b> 다시 채운다 -
        /// 다른 코드가 이 목록 자체를 들고 있을 수 있기 때문이다. 항목도 새로 만들지 않고 <b>찍어 둔
        /// 그 객체</b>를 수량까지 되돌려 그대로 다시 넣으므로, 지워졌던 칸이 원래 자리로 돌아온다.
        /// </summary>
        internal void Restore(SaveData data)
        {
            if (data == null) return;

            data.currency = currencyBefore;

            if (data.items == null) data.items = new List<InventoryItemState>();

            data.items.Clear();
            for (int i = 0; i < itemsBefore.Length; i++)
            {
                ItemSlot slot = itemsBefore[i];

                // 차감이 제자리에서 고친 수량을 먼저 되돌린 뒤 그 객체를 그대로 다시 넣는다.
                if (slot.State != null) slot.State.count = slot.Count;
                data.items.Add(slot.State);
            }
        }

        /// <summary>지금 목록의 모양을 그대로 찍는다(항목 객체 + 그때의 수량, null 칸 포함).</summary>
        internal static ItemSlot[] Capture(List<InventoryItemState> states)
        {
            if (states == null || states.Count == 0) return Array.Empty<ItemSlot>();

            var snapshot = new ItemSlot[states.Count];
            for (int i = 0; i < states.Count; i++)
            {
                InventoryItemState state = states[i];
                snapshot[i] = new ItemSlot(state, state?.count ?? 0);
            }

            return snapshot;
        }
    }

    /// <summary>
    /// 보유 재화와 보유 아이템을 소유하는 <b>단일 관리자</b>. 값이 바뀌는 경로는 이 컴포넌트의
    /// 메서드뿐이고, UI(InventoryPanel)는 여기서 읽기만 한다 - 씬에 배치된 오브젝트나 프리팹 상태를
    /// 인벤토리의 근거로 삼지 않는다. CharacterRoster와 같은 구조다.
    ///
    /// 데이터 소유는 두 갈래로 나뉜다.
    ///   - <see cref="ItemDefinition"/> 에셋: 아이템이 무엇인지(이름/아이콘)
    ///   - SaveData.currency / SaveData.items: 지금 얼마나 갖고 있는지
    /// 재화는 아이템 목록과 <b>완전히 분리된 전역 값</b>이라 아이템 슬롯에 나타나지 않고, 경험치/레벨/
    /// 행동력과도 연결되지 않는다.
    ///
    /// <b>정의를 모으는 곳은 두 군데다.</b> Item.csv에서 만들어진 <see cref="ItemCatalog"/> 에셋과,
    /// 카탈로그가 생기기 전부터 씬에 직접 박아 둔 <see cref="itemCatalog"/> 목록이다. 둘 다 있으면
    /// <b>카탈로그를 먼저</b> 등록하고 씬 목록을 뒤에 등록하며, 같은 Item Id가 양쪽에 있으면
    /// <b>먼저 등록된 쪽(= 카탈로그)이 남고</b> 뒤의 것은 오류와 함께 무시한다 - 다른 카탈로그
    /// 에셋들과 같은 규칙이고, 저장 파일의 키 하나가 어느 정의로 그려질지가 실행 순서에 따라
    /// 달라지지 않게 하기 위함이다. 임포터도 같은 충돌을 Rebuild 단계에서 오류로 막으므로,
    /// 이 경로는 사람이 수동 에셋을 나중에 고친 경우를 잡는 마지막 그물이다.
    ///
    /// 같은 아이템은 항상 저장 항목 하나에 수량으로 누적된다(같은 id로 항목이 두 개 생기지 않는다).
    /// 표시 순서는 저장 목록의 순서, 즉 <b>처음 획득한 순서</b>다 - 새 아이템은 뒤에 추가되고 그 뒤로
    /// 자리가 바뀌지 않으므로 저장/불러오기를 거쳐도 순서가 유지된다.
    ///
    /// <b>몬스터 처치 보상은 <see cref="DefeatRewardDistributor"/>가 <see cref="ApplyRewards"/>로
    /// 지급한다.</b> 개발용 진입점은 아래에 별도로 있다.
    /// </summary>
    [DisallowMultipleComponent]
    public class InventoryManager : MonoBehaviour
    {
        /// <summary>
        /// 보상 한 건에 들어가는 아이템 한 칸(정의 + 수량). 여러 칸을 한 번에 넘겨 <b>처치 하나가
        /// 저장과 알림을 각각 한 번만</b> 일으키게 하려고 있는 값이다.
        /// </summary>
        public readonly struct RewardItemStack
        {
            public readonly ItemDefinition Definition;
            public readonly int Count;

            public RewardItemStack(ItemDefinition definition, int count)
            {
                Definition = definition;
                Count = count;
            }
        }

        /// <summary>UI가 한 항목을 그리는 데 필요한 최소 정보(정의 + 수량). 저장 구조를 그대로 밖으로
        /// 내보내면 UI가 저장 필드를 직접 고칠 수 있게 되므로 읽기 전용 구조체로 감싼다.</summary>
        public readonly struct Entry
        {
            public readonly ItemDefinition Definition;
            public readonly int Count;

            public Entry(ItemDefinition definition, int count)
            {
                Definition = definition;
                Count = count;
            }
        }

        [Header("Item Catalog")]
        [Tooltip("Item.csv에서 만들어진 ItemCatalog 에셋(Assets/Generated/TableData/Item/ItemCatalog.asset). " +
                 "비워 두어도 되며, 그 경우 아래 목록만 쓴다 - 연결하면 CSV로 추가한 아이템이 별도 작업 " +
                 "없이 인벤토리에서 인식된다. 같은 Item Id가 아래 목록에도 있으면 이 카탈로그 쪽이 남는다.")]
        [SerializeField] private ItemCatalog generatedItemCatalog;

        [Tooltip("카탈로그 이전부터 씬에 직접 넣어 둔 아이템 정의 목록. 저장 데이터의 itemId를 실제 " +
                 "아이템으로 되돌릴 때 위 카탈로그와 함께 쓴다 - 어느 쪽에도 없는 id가 저장 파일에 " +
                 "있으면 그 항목은 표시하지 않고 경고만 남긴다(저장 값 자체는 지우지 않는다).")]
        [SerializeField] private List<ItemDefinition> itemCatalog = new List<ItemDefinition>();

        [Header("Debug (개발용 - 정식 UI에 노출하지 않는다)")]
        [Tooltip("Debug - Add Currency가 더할 금액.")]
        [SerializeField] private int debugCurrencyAmount = 1000;

        [Tooltip("Debug - Add Item이 추가할 아이템.")]
        [SerializeField] private ItemDefinition debugItem;

        [Min(1)]
        [Tooltip("Debug - Add Item이 한 번에 추가할 수량.")]
        [SerializeField] private int debugItemCount = 1;

        /// <summary>씬에 하나만 둔다. 패널이 정적으로 접근한다(CharacterRoster와 같은 패턴).</summary>
        public static InventoryManager Instance { get; private set; }

        /// <summary>재화나 아이템이 실제로 바뀐 직후 발생. 열려 있는 인벤토리 패널이 이 신호로
        /// 즉시 갱신된다 - 값이 바뀌지 않은 호출에서는 발생하지 않는다.</summary>
        public static event Action InventoryChanged;

        /// <summary>
        /// <see cref="ApplyReward"/> / <see cref="ApplyRewards"/>로 실제로 인벤토리가 바뀐 뒤 발생.
        /// <b>빈 결과에는 발생하지 않으며</b>, 기존 저장 처리와 <see cref="InventoryChanged"/>가 끝난
        /// 뒤에만 발생한다. <see cref="AddCurrency"/>/<see cref="AddItem"/>/개발용 진입점/소비/환불은
        /// 이 이벤트를 발생시키지 않는다.
        /// </summary>
        public event Action<InventoryRewardApplyResult> RewardApplied;

        // itemId -> 정의. 저장 데이터를 화면에 그릴 때마다 목록을 순회하지 않도록 Awake에서 한 번만 만든다.
        private readonly Dictionary<string, ItemDefinition> definitionsById = new Dictionary<string, ItemDefinition>();

        // 등록에 성공한 정의를 등록 순서 그대로. 개발용 진입점이 "모든 아이템"을 돌 때 쓴다 - 두 갈래
        // 출처를 다시 합치는 코드가 여러 곳에 생기지 않게 여기 한 번만 모아 둔다.
        private readonly List<ItemDefinition> registeredDefinitions = new List<ItemDefinition>();

        // Items 프로퍼티가 매번 새 리스트를 만들지 않도록 재사용하는 버퍼. 인벤토리가 바뀔 때만 다시 채운다.
        private readonly List<Entry> entryCache = new List<Entry>();
        private bool entryCacheDirty = true;

        // 카탈로그에 없는 itemId는 처음 한 번만 경고한다(패널을 열 때마다 로그가 쏟아지지 않게).
        private readonly HashSet<string> warnedUnknownItemIds = new HashSet<string>();

        public int Currency => SaveSystem.Data.currency;

        /// <summary>보유 아이템 목록(획득 순서). 카탈로그에 정의가 없는 저장 항목은 제외된다.</summary>
        public IReadOnlyList<Entry> Items
        {
            get
            {
                RebuildEntryCacheIfNeeded();
                return entryCache;
            }
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[InventoryManager] 씬에 InventoryManager가 이미 있습니다. 이 인스턴스는 무시합니다.", this);
                enabled = false;
                return;
            }
            Instance = this;

            BuildDefinitionLookup();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>
        /// 두 출처의 정의를 하나의 조회표로 합친다. <b>등록 순서가 곧 우선순위</b>이며, 생성 카탈로그
        /// (Item.csv)를 먼저 등록하고 씬에 박아 둔 목록을 뒤에 등록한다 - 같은 Item Id가 양쪽에 있으면
        /// 카탈로그 쪽이 남는다. 순서를 코드로 고정해 두었으므로 결과는 실행마다 같다.
        ///
        /// 카탈로그를 연결하지 않았으면 예전과 완전히 같게 동작한다(씬 목록만 등록된다).
        /// </summary>
        private void BuildDefinitionLookup()
        {
            definitionsById.Clear();
            registeredDefinitions.Clear();

            // 1순위 - Item.csv가 만든 카탈로그. ItemCatalog 자체가 빈 칸/식별자 없음/중복을 이미 걸러
            // 주므로 여기서는 남은 항목만 받는다.
            if (generatedItemCatalog != null)
            {
                IReadOnlyList<ItemDefinition> catalogItems = generatedItemCatalog.Items;
                for (int i = 0; i < catalogItems.Count; i++)
                {
                    TryRegister(catalogItems[i], $"Generated Item Catalog[{i}]", generatedItemCatalog);
                }
            }

            // 2순위 - 카탈로그가 생기기 전부터 씬에 있던 목록. 기존 수동 포션 연결이 여기로 그대로 남는다.
            if (itemCatalog == null) return;

            for (int i = 0; i < itemCatalog.Count; i++)
            {
                TryRegister(itemCatalog[i], $"Item Catalog[{i}]", this);
            }
        }

        /// <summary>정의 하나를 조회표에 넣는다. 넣지 못한 이유는 <b>전부 오류로 남긴다</b> - 조용히
        /// 빠진 정의는 "저장은 되는데 화면에 없는 아이템"으로 나타나 원인을 찾기 어렵다.</summary>
        private void TryRegister(ItemDefinition definition, string where, UnityEngine.Object context)
        {
            if (definition == null)
            {
                Debug.LogError($"[InventoryManager] {where}가 비어 있습니다 - 이 항목은 무시합니다.", context);
                return;
            }

            if (!definition.IsValid)
            {
                // Item Id가 없는 정의는 저장 키를 만들 수 없다. 예전에는 에셋 파일 이름으로 대신했지만
                // 그러면 파일 이름을 바꾸는 것만으로 저장 항목과의 연결이 끊기므로, 지금은 조용히
                // 넘어가지 않고 여기서 걸러 낸다(ItemCatalog와 같은 규칙).
                Debug.LogError($"[InventoryManager] {where}('{definition.name}')에 Item Id가 없어 " +
                               "무시합니다 - 에셋에서 식별자를 직접 지정하세요.", definition);
                return;
            }

            if (definitionsById.TryGetValue(definition.ItemId, out ItemDefinition existing))
            {
                if (existing == definition) return;

                Debug.LogError($"[InventoryManager] Item Id '{definition.ItemId}'가 이미 '{existing.name}'으로 " +
                               $"등록되어 있어 {where}('{definition.name}')는 무시합니다 - 저장 데이터가 서로 " +
                               "섞이지 않도록 먼저 등록된 정의(생성 카탈로그 → 씬 목록 순서)가 남습니다. " +
                               "한쪽의 Item Id를 정리하세요.", definition);
                return;
            }

            definitionsById.Add(definition.ItemId, definition);
            registeredDefinitions.Add(definition);
        }

        // ---- 조회 ----

        public int GetItemCount(ItemDefinition definition)
        {
            return definition == null ? 0 : GetItemCount(definition.ItemId);
        }

        public int GetItemCount(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return 0;

            List<InventoryItemState> states = SaveSystem.Data.items;
            for (int i = 0; i < states.Count; i++)
            {
                if (states[i] != null && states[i].itemId == itemId) return states[i].count;
            }
            return 0;
        }

        // ---- 변경 ----
        //
        // 공개 메서드는 전부 "메모리 값을 고치는 Apply* 내부 메서드 + 마지막에 SaveAndNotify 한 번"
        // 구조다. 이렇게 나눠 둔 덕분에 여러 값을 한꺼번에 바꾸는 보상 지급(ApplyReward)도 저장과
        // 알림을 정확히 한 번만 하고, 일부만 저장된 중간 상태가 생기지 않는다.

        /// <summary>재화를 더한다(음수를 넣으면 감소하지만, 소비처는 이번 범위에 없다). 결과는 0 아래로
        /// 내려가지 않는다. 값이 실제로 바뀐 경우에만 저장하고 <see cref="InventoryChanged"/>를 보낸다.</summary>
        public void AddCurrency(int amount)
        {
            if (ApplyCurrencyDelta(amount)) SaveAndNotify();
        }

        /// <summary>재화를 직접 지정한다(0 아래로는 내려가지 않는다).</summary>
        public void SetCurrency(int value)
        {
            if (ApplyCurrencyValue(value)) SaveAndNotify();
        }

        /// <summary>아이템을 추가한다. 이미 갖고 있으면 <b>새 항목을 만들지 않고</b> 기존 항목의 수량만
        /// 늘린다 - 같은 아이템이 여러 슬롯으로 나뉘지 않는 근거가 이 한 곳이다.</summary>
        public void AddItem(ItemDefinition definition, int count = 1)
        {
            if (ApplyItemDelta(definition, count)) SaveAndNotify();
        }

        /// <summary>
        /// 처치 보상 한 건(재화 + 아이템)을 <b>한 번의 처리로</b> 적용한다. 재화와 아이템을 각각
        /// AddCurrency/AddItem으로 주면 저장과 <see cref="InventoryChanged"/>가 두 번씩 발생하고,
        /// 그 사이에 "재화만 오른 상태"가 화면에 한 번 그려진다 - 보상은 한 덩어리이므로 그렇게 나누지 않는다.
        ///
        /// 값 변경은 전부 메모리에서 끝낸 뒤 마지막에 딱 한 번 저장하므로, 저장이 실패하더라도 일부만
        /// 기록된 파일이 남지 않는다(기존 저장 파일이 그대로 유지된다).
        ///
        /// 아이템 없이 재화만 주려면 <paramref name="item"/>에 null을 넘기면 된다. 아이템이 여러 칸이면
        /// <see cref="ApplyRewards"/>를 쓴다 - 이쪽은 한 칸 전용이라 결과 순서 추적용 1칸 목록만 만든다.
        ///
        /// 반환된 <see cref="InventoryRewardApplyResult"/>는 실제로 적용된 양만 담는다. 호출부가
        /// 반환값을 무시해도 기존 동작은 달라지지 않는다.
        /// </summary>
        public InventoryRewardApplyResult ApplyReward(int currencyAmount, ItemDefinition item, int itemCount = 1)
        {
            int currencyBefore = SaveSystem.Data.currency;
            var itemSnapshots = SnapshotItemCounts();

            bool changed = ApplyCurrencyDelta(currencyAmount);
            changed |= ApplyItemDelta(item, itemCount);

            InventoryRewardApplyResult result;
            if (changed)
            {
                var singleStack = item != null
                    ? new List<RewardItemStack>(1) { new RewardItemStack(item, itemCount) }
                    : null;
                result = BuildResult(currencyBefore, itemSnapshots, singleStack);
                SaveAndNotify();
                if (!result.IsEmpty) RewardApplied?.Invoke(result);
            }
            else
            {
                result = InventoryRewardApplyResult.Empty;
            }

            return result;
        }

        /// <summary>
        /// 처치 보상 한 건(재화 + <b>아이템 여러 칸</b>)을 한 번의 처리로 적용한다. 드롭 슬롯이 여러 개
        /// 성공해도 <b>저장은 1회, <see cref="InventoryChanged"/>도 1회</b>다 - 칸마다 AddItem을 부르면
        /// 한 번의 처치가 파일 쓰기 서너 번과 화면 갱신 서너 번이 되고, 그 중간 상태(재화만 오른 화면)가
        /// 실제로 한 프레임 그려진다.
        ///
        /// 값 변경은 전부 메모리에서 끝낸 뒤 마지막에 딱 한 번 저장하므로, 저장이 실패해도 일부만
        /// 기록된 파일이 남지 않는다.
        ///
        /// <b>실제로 바뀐 것이 하나도 없으면 저장도 알림도 하지 않는다</b>(재화 0 + 유효한 아이템 0칸).
        /// 같은 아이템이 여러 칸에 나뉘어 들어와도 수량은 정상 누적된다 - 누적은 저장 항목 하나에서만
        /// 일어나므로(<see cref="ApplyItemDelta"/>) 슬롯이 갈라지지 않는다.
        ///
        /// null 정의나 0 이하 수량은 조용히 건너뛴다(보상 판정에서 이미 걸러진 값이라 정상 입력이다).
        /// 다만 <b>카탈로그에 없는 정의는 기존과 똑같이 오류로 막는다</b> - 저장은 되는데 화면에 없는
        /// 아이템이 생기는 편이 훨씬 찾기 어렵기 때문이다.
        ///
        /// 반환된 <see cref="InventoryRewardApplyResult"/>는 실제로 적용된 양만 담는다. 호출부가
        /// 반환값을 무시해도 기존 동작은 달라지지 않는다.
        /// </summary>
        public InventoryRewardApplyResult ApplyRewards(int currencyAmount, IReadOnlyList<RewardItemStack> itemStacks)
        {
            int currencyBefore = SaveSystem.Data.currency;
            var itemSnapshots = SnapshotItemCounts();

            bool changed = ApplyCurrencyDelta(currencyAmount);

            if (itemStacks != null)
            {
                for (int i = 0; i < itemStacks.Count; i++)
                {
                    changed |= ApplyItemDelta(itemStacks[i].Definition, itemStacks[i].Count);
                }
            }

            InventoryRewardApplyResult result;
            if (changed)
            {
                result = BuildResult(currencyBefore, itemSnapshots, itemStacks);
                SaveAndNotify();
                if (!result.IsEmpty) RewardApplied?.Invoke(result);
            }
            else
            {
                result = InventoryRewardApplyResult.Empty;
            }

            return result;
        }

        /// <summary>
        /// 잔액이 충분할 때만 차감한다. 부족하면 <b>아무것도 바꾸지 않고</b> false를 돌려준다.
        ///
        /// <see cref="AddCurrency"/>에 음수를 넘기는 방식과 결정적으로 다르다 - 그쪽은 결과를 0으로
        /// 자르기 때문에, 300이 있는데 500을 쓰면 "성공한 것처럼" 잔액이 0이 된다(부분 지불). 값을
        /// 소비하는 기능은 반드시 이 경로를 쓴다.
        /// </summary>
        public bool TrySpendCurrency(int amount)
        {
            if (!TrySpendCurrencyWithoutSave(amount)) return false;
            if (amount != 0) SaveAndNotify();
            return true;
        }

        /// <summary>
        /// <see cref="TrySpendCurrency"/>와 같은 판정이지만 <b>메모리만</b> 바꾼다(저장도 알림도 없다).
        /// 재화 차감과 다른 저장 값 변경이 한 트랜잭션이어서 SaveSystem.Save()가 그 사이에 두 번
        /// 일어나면 안 되는 호출부(회복소 시작)를 위한 경로다.
        ///
        /// 호출부는 반드시 뒤이어 SaveSystem.Save()를 하고 <see cref="NotifyChangedAfterExternalSave"/>를
        /// 불러야 한다. 저장이 실패하면 <see cref="RefundCurrencyWithoutSave"/>로 되돌린다.
        /// </summary>
        public bool TrySpendCurrencyWithoutSave(int amount)
        {
            if (amount < 0)
            {
                Debug.LogError($"[InventoryManager] 음수 금액({amount})은 차감할 수 없습니다.", this);
                return false;
            }
            if (amount == 0) return true;
            if (SaveSystem.Data.currency < amount) return false;

            SaveSystem.Data.currency -= amount;
            return true;
        }

        /// <summary>차감했던 금액을 메모리에서 되돌린다(저장/알림 없음). 트랜잭션 취소 전용이며,
        /// 재화 획득에는 <see cref="AddCurrency"/>를 쓴다.</summary>
        public void RefundCurrencyWithoutSave(int amount)
        {
            if (amount <= 0) return;
            SaveSystem.Data.currency += amount;
        }

        /// <summary>다른 시스템이 재화를 바꾸고 SaveSystem.Save()까지 마친 뒤, 인벤토리 표시를
        /// 갱신하라고 알린다. 저장은 하지 않는다 - 저장을 두 번 하지 않기 위해 나눠 둔 경로다.</summary>
        public void NotifyChangedAfterExternalSave()
        {
            entryCacheDirty = true;
            InventoryChanged?.Invoke();
        }

        // ---- 비용 지불 ----
        //
        // 재화만 내는 경로(TrySpendCurrency)와 달리, 아래 두 메서드는 재화와 아이템 여러 종을 한
        // 덩어리로 다룬다. 판정을 전부 끝낸 뒤에만 값을 건드리므로 "재화는 빠졌는데 아이템이 모자라
        // 실패" 같은 중간 상태가 생기지 않는다.
        //
        // 소비를 AddItem(음수)로 만들지 않는 이유는 TrySpendCurrency가 AddCurrency(음수)를 쓰지 않는
        // 이유와 같다 - 그쪽은 결과를 0/포화로 자르기 때문에 3개를 가진 아이템에서 5개를 내면
        // "성공한 것처럼" 0개가 된다(부분 지불). 비용은 전부 내거나 하나도 내지 않거나 둘 중 하나다.

        /// <summary>
        /// 이 비용을 지금 낼 수 있는지 <b>판정만</b> 한다 - 저장 항목을 만들지도, 값을 바꾸지도,
        /// 캐시를 더럽히지도, 저장하지도, <see cref="InventoryChanged"/>나 <see cref="RewardApplied"/>를
        /// 보내지도 않는다. 몇 번을 불러도 결과가 같다.
        ///
        /// UI가 "낼 수 있음/부족함"을 그리는 자리에서 쓰는 경로이며, 실제 지불은
        /// <see cref="TrySpendCost"/>가 처음부터 다시 판정한다 - 이 결과를 근거로 차감하는 경로는
        /// 만들지 않는다(판정과 지불 사이에 값이 바뀔 수 있다).
        /// </summary>
        public InventoryCostResult EvaluateCost(InventoryCostRequest request)
        {
            return EvaluateCost(request, new List<InventoryItemRequirement>());
        }

        /// <summary>
        /// 비용을 <b>원자적으로</b> 낸다. 먼저 전부 판정하고, 하나라도 통과하지 못하면 재화도 아이템도
        /// <b>전혀 건드리지 않은 채</b> 실패 이유를 돌려준다(저장 0회, <see cref="InventoryChanged"/>
        /// 0회). 통과했을 때만 재화와 모든 아이템을 함께 빼고 <b>저장 1회, 알림 1회</b>로 끝낸다.
        ///
        /// <b>기록에 실패하면 낸 것도 없다.</b> 예전에는 쓰기가 실패해도 오류만 남기고 성공을
        /// 돌려주었는데, 그러면 값은 빠졌고 파일에는 남지 않은 상태로 호출부가 "샀다"고 믿는다 -
        /// 그 판단으로 무언가를 지급하면 앱을 다시 켰을 때 <b>낸 것만 되살아나고 받은 것은 사라진다</b>.
        /// 지금은 저장이 실패하면 재화와 아이템을 전부 되돌리고
        /// <see cref="InventoryCostFailureReason.SaveFailed"/>로 실패를 알리며,
        /// <see cref="InventoryChanged"/>도 보내지 않는다(바뀐 것이 없으므로 알릴 것도 없다).
        ///
        /// 수량이 정확히 0이 된 아이템은 저장 항목째 지워지고, 남은 항목들은 순서도 값도 그대로다 -
        /// 표시 순서가 "처음 획득한 순서"라는 규칙이 소비 때문에 흔들리지 않는다.
        ///
        /// <see cref="RewardApplied"/>는 <b>절대</b> 발생하지 않는다. 그 이벤트는 지급 전용이며,
        /// 비용 지불을 "음수 보상"으로 흘려보내면 보상 연출이 소비에서도 뜬다.
        ///
        /// 비용이 아예 없는 요청(재화 0, 유효한 아이템 0칸)은 성공이지만 저장도 알림도 하지 않는다.
        /// </summary>
        public InventoryCostResult TrySpendCost(InventoryCostRequest request)
        {
            InventoryCostResult evaluation = TrySpendCostWithoutSave(request, out InventoryCostReceipt receipt);
            if (!evaluation.Success) return evaluation;

            // 낼 것이 없었던 요청은 바뀐 것도 없다 - 예전과 똑같이 저장도 알림도 하지 않는다.
            if (!receipt.Changed) return evaluation;

            if (!PersistToDisk())
            {
                RefundCostWithoutSave(receipt);

                Debug.LogError("[InventoryManager] 비용 지불을 저장하지 못해 되돌렸습니다 - 재화와 아이템은 " +
                               "지불을 시도하기 전 그대로입니다.", this);
                return InventoryCostResult.SaveFailed(receipt.Currency, SaveSystem.Data.currency);
            }

            NotifyChangedAfterExternalSave();
            return evaluation;
        }

        /// <summary>
        /// <see cref="TrySpendCost"/>와 <b>똑같이 판정하고 똑같이 차감하지만 저장도 알림도 하지
        /// 않는다</b>. 비용 차감과 다른 저장 값 변경(건설 기록 등)이 한 트랜잭션이어서 그 사이에
        /// 저장이 두 번 일어나면 안 되는 호출부를 위한 경로이며,
        /// <see cref="TrySpendCurrencyWithoutSave"/>가 재화 하나에 대해 하던 일을 재화 + 아이템
        /// 여러 종으로 넓힌 것이다.
        ///
        /// 호출부의 의무는 셋이다.
        /// <list type="number">
        ///   <item>이어서 <c>SaveSystem.Save()</c>를 <b>한 번</b> 한다.</item>
        ///   <item>저장이 성공하면 <see cref="NotifyChangedAfterExternalSave"/>로 알린다(여기서는
        ///         알리지 않는다 - 아직 기록되지 않은 값을 화면에 확정으로 보여 줄 수 없다).</item>
        ///   <item>저장이 실패하면 <see cref="RefundCostWithoutSave"/>에 <paramref name="receipt"/>를
        ///         그대로 넘겨 되돌린다.</item>
        /// </list>
        ///
        /// 실패하면 <paramref name="receipt"/>는 <see cref="InventoryCostReceipt.Changed"/>가 false인
        /// 빈 영수증이라, 되돌리기를 불러도 아무 일도 일어나지 않는다.
        /// </summary>
        public InventoryCostResult TrySpendCostWithoutSave(
            InventoryCostRequest request, out InventoryCostReceipt receipt)
        {
            var requirements = new List<InventoryItemRequirement>();
            InventoryCostResult evaluation = EvaluateCost(request, requirements);
            if (!evaluation.Success)
            {
                receipt = InventoryCostReceipt.Empty;
                return evaluation;
            }

            // 값을 건드리기 <b>전에</b> 지금 모양을 찍어 둔다 - 되돌리기는 "뺀 만큼 더하기"가 아니라
            // 이 사진으로 되돌리는 일이라, 지워진 항목도 원래 자리로 돌아온다.
            int currencyBefore = SaveSystem.Data.currency;
            InventoryCostReceipt.ItemSlot[] itemsBefore =
                InventoryCostReceipt.Capture(SaveSystem.Data.items);

            int spentCurrency = 0;

            if (request.Currency > 0)
            {
                // 판정을 통과했으므로 여기서 실패할 수 없다. 그래도 반환값을 버리지 않는 것은, 만약
                // 실패한다면 아이템은 아직 하나도 건드리지 않은 시점이라 그대로 빠져나가는 것이
                // 유일하게 안전한 처리이기 때문이다(재화 차감 경로는 실패 시 값을 바꾸지 않는다).
                if (!TrySpendCurrencyWithoutSave(request.Currency))
                {
                    receipt = InventoryCostReceipt.Empty;
                    return InventoryCostResult.InsufficientCurrency(request.Currency, SaveSystem.Data.currency);
                }
                spentCurrency = request.Currency;
            }

            for (int i = 0; i < requirements.Count; i++)
            {
                SpendItemWithoutSave(requirements[i].ItemId, requirements[i].Count);
            }

            receipt = new InventoryCostReceipt(spentCurrency, requirements, currencyBefore, itemsBefore);
            return evaluation;
        }

        /// <summary>
        /// <see cref="TrySpendCostWithoutSave"/>가 뺀 것을 되돌린다(저장/알림 없음). 트랜잭션 취소
        /// 전용이며, 아이템 지급에는 <see cref="AddItem"/>을 쓴다.
        ///
        /// <b>되돌리기는 빼기 직전의 모양을 그대로 복원하는 일이다</b> - 뺀 값을 다시 더하는 것이
        /// 아니다. 수량이 정확히 0이 되어 지워졌던 항목을 다시 <i>더하면</i> 그 항목이 목록 맨 뒤에
        /// 붙어, 실패한 지불 하나가 <b>획득 순서(= 인벤토리 표시 순서)를 영구히 바꿔</b> 놓는다.
        /// 그래서 영수증이 들고 있는 사진으로 잔액과 목록(순서·중복·null 칸·수량·항목 객체까지)을
        /// 그대로 되돌린다. 카탈로그 등록 여부도 다시 따지지 않는다 - 뺄 때 이미 통과한 값이고,
        /// 되돌리기가 막히면 <b>플레이어가 낸 것이 사라진다</b>.
        ///
        /// 빈 영수증이면 아무 일도 하지 않는다.
        /// </summary>
        public void RefundCostWithoutSave(InventoryCostReceipt receipt)
        {
            if (receipt == null || !receipt.Changed) return;

            receipt.Restore(SaveSystem.Data);
        }

        /// <summary>
        /// 판정 본체. 성공하면 <paramref name="requirements"/>에 같은 Id끼리 합산이 끝난 요구 목록이
        /// 남고, 지불 경로는 <b>이 목록 그대로</b> 차감한다 - 판정이 본 것과 차감하는 것이 어긋날
        /// 자리를 만들지 않기 위해 정규화를 두 번 하지 않는다. 실패하면 목록을 비워, 통과하지 못한
        /// 요구가 실수로 차감에 쓰일 수 없게 한다.
        /// </summary>
        private InventoryCostResult EvaluateCost(
            InventoryCostRequest request, List<InventoryItemRequirement> requirements)
        {
            InventoryCostResult result = EvaluateNormalizedCost(request, requirements);
            if (!result.Success) requirements.Clear();
            return result;
        }

        /// <summary>
        /// 판정 순서는 구조 → 등록 여부 → 재화 → 아이템이다. 잔액 부족이 "요청 자체가 잘못됐다"나
        /// "카탈로그에 없는 아이템"을 가리면 원인을 찾기 어렵기 때문에, 보유량과 무관한 오류부터
        /// 확정한다. 같은 이유로 아이템 부족은 정규화된 순서상 <b>처음 모자란 것</b>을 돌려준다 -
        /// 같은 요청이면 언제나 같은 답이 나온다.
        /// </summary>
        private InventoryCostResult EvaluateNormalizedCost(
            InventoryCostRequest request, List<InventoryItemRequirement> requirements)
        {
            requirements.Clear();

            if (request == null) return InventoryCostResult.Invalid(string.Empty);

            if (!request.TryNormalize(requirements, out string offendingItemId))
                return InventoryCostResult.Invalid(offendingItemId);

            for (int i = 0; i < requirements.Count; i++)
            {
                // 정의 에셋이 아니라 Id로 대조한다 - 저장 항목의 키가 Id이므로, 같은 Id를 가진 다른
                // 정의 인스턴스를 넘겨도 가리키는 저장 항목은 하나다.
                if (definitionsById.ContainsKey(requirements[i].ItemId)) continue;

                return InventoryCostResult.UnknownItem(requirements[i].ItemId, requirements[i].Count);
            }

            if (request.Currency > 0 && SaveSystem.Data.currency < request.Currency)
                return InventoryCostResult.InsufficientCurrency(request.Currency, SaveSystem.Data.currency);

            for (int i = 0; i < requirements.Count; i++)
            {
                int held = GetItemCount(requirements[i].ItemId);
                if (held >= requirements[i].Count) continue;

                return InventoryCostResult.InsufficientItem(requirements[i].ItemId, requirements[i].Count, held);
            }

            return InventoryCostResult.Payable;
        }

        /// <summary>
        /// 아이템 하나를 저장 데이터에서 뺀다(저장/알림 없음). 판정을 통과한 뒤에만 불리므로 보유량이
        /// 모자랄 수 없고, 정확히 0이 되면 <b>항목 자체를 지운다</b> - 수량 0짜리 유령 항목이 저장
        /// 파일에 쌓이지 않게 한다. 나머지 항목은 앞으로 당겨질 뿐 서로의 순서가 바뀌지 않는다.
        ///
        /// 대조는 <see cref="GetItemCount"/>와 똑같이 <b>처음 일치하는 항목 하나</b>만 본다 - 손상된
        /// 저장 파일에 같은 Id가 두 줄 있어도 판정이 본 그 줄에서 빠진다.
        /// </summary>
        private void SpendItemWithoutSave(string itemId, int count)
        {
            List<InventoryItemState> states = SaveSystem.Data.items;
            for (int i = 0; i < states.Count; i++)
            {
                if (states[i] == null || states[i].itemId != itemId) continue;

                int remaining = states[i].count - count;
                if (remaining == 0) states.RemoveAt(i);
                else states[i].count = remaining;
                return;
            }
        }

        /// <summary>
        /// 메모리의 재화 값만 바꾼다(저장/알림 없음). 실제로 값이 달라졌으면 true.
        ///
        /// <b>덧셈은 반드시 long으로 한다.</b> int로 더하면 잔액 1에 int.MaxValue를 더하는 순간 값이
        /// 음수로 넘쳐 <see cref="ApplyCurrencyValue"/>가 그것을 "0 아래"로 보고 <b>잔액을 0으로
        /// 만들어 저장</b>한다 - 보상을 받았는데 가진 것이 사라지는, 되돌릴 수 없는 손실이다.
        /// 보상 표(Monster.csv)가 금액으로 int.MaxValue까지 허용하므로 이것은 도달 가능한 값이다.
        /// </summary>
        private bool ApplyCurrencyDelta(int amount)
        {
            return amount != 0 && ApplyCurrencyValue((long)SaveSystem.Data.currency + amount);
        }

        /// <summary>
        /// 재화 값을 확정한다. 받은 값을 <b>[0, int.MaxValue]로 자른 뒤에</b> int로 좁힌다 - 자르기
        /// 전에 좁히면 넘친 값이 그대로 들어온다.
        ///
        /// 매개변수가 long인 것은 위로 넘치는 쪽을 표현할 수 있어야 하기 때문이다.
        /// <see cref="SetCurrency"/>가 넘기는 int는 그대로 확장되므로 기존 동작은 달라지지 않는다
        /// (음수는 여전히 0으로 잘린다).
        /// </summary>
        private bool ApplyCurrencyValue(long value)
        {
            int clamped = value <= 0L ? 0
                : value >= int.MaxValue ? int.MaxValue
                : (int)value;

            if (clamped == SaveSystem.Data.currency) return false;

            SaveSystem.Data.currency = clamped;
            return true;
        }

        /// <summary>메모리의 아이템 수량만 바꾼다(저장/알림 없음). 실제로 값이 달라졌으면 true.
        /// 수량은 int.MaxValue에서 포화한다 - 넘치지 않는다.</summary>
        private bool ApplyItemDelta(ItemDefinition definition, int count)
        {
            if (definition == null || count <= 0) return false;

            if (!definitionsById.ContainsKey(definition.ItemId))
            {
                Debug.LogError($"[InventoryManager] '{definition.ItemId}'가 Item Catalog에 없어 추가하지 않았습니다 - " +
                               "Inspector의 Generated Item Catalog(Item.csv로 만든 아이템) 또는 Item Catalog 목록에 " +
                               "이 정의를 등록하세요.", this);
                return false;
            }

            List<InventoryItemState> states = SaveSystem.Data.items;
            for (int i = 0; i < states.Count; i++)
            {
                if (states[i] == null || states[i].itemId != definition.ItemId) continue;

                long sum = (long)states[i].count + count;
                int clamped = sum >= int.MaxValue ? int.MaxValue : (int)sum;
                if (clamped == states[i].count) return false;
                states[i].count = clamped;
                return true;
            }

            states.Add(new InventoryItemState { itemId = definition.ItemId, count = count });
            return true;
        }

        /// <summary>재화와 아이템을 모두 비운다. 캐릭터/경험치/행동력 저장 값은 건드리지 않는다.</summary>
        public void ClearInventory()
        {
            SaveData data = SaveSystem.Data;
            if (data.currency == 0 && data.items.Count == 0) return;

            data.currency = 0;
            data.items.Clear();
            SaveAndNotify();
        }

        /// <summary>
        /// 실제 파일 쓰기를 대신 처리할 함수. <b>평소에는 null이고, 그때는 <see cref="SaveSystem.Save"/>가
        /// 그대로 불린다</b> - 게임 동작에는 아무 영향이 없다.
        ///
        /// 오직 시험만 이 자리를 잠시 갈아 끼운다. <see cref="SaveSystem"/>은 정적 클래스라 저장 경로를
        /// 주입할 자리가 없고, 그 경로는 실제 게임과 <b>같은</b>
        /// <see cref="Application.persistentDataPath"/>를 가리킨다 - 그래서 시험이 저장 경로를 그대로
        /// 실행하면 <b>사람이 실제로 플레이한 저장 파일을 덮어쓴다</b>. 저장이 "몇 번" 일어나는지는
        /// 묶음 지급의 핵심 성질이라 반드시 확인해야 하므로, 파일을 건드리지 않고 그 횟수를 셀 수 있는
        /// 최소한의 자리를 여기 하나만 둔다(비공개라 게임 코드에서는 보이지 않는다).
        /// </summary>
        private static Func<bool> saveOverride;

        private static bool PersistToDisk()
        {
            return saveOverride != null ? saveOverride() : SaveSystem.Save();
        }

        private Dictionary<string, int> SnapshotItemCounts()
        {
            List<InventoryItemState> states = SaveSystem.Data.items;
            var snapshot = new Dictionary<string, int>(states.Count);
            for (int i = 0; i < states.Count; i++)
            {
                if (states[i] != null && !string.IsNullOrEmpty(states[i].itemId))
                    snapshot[states[i].itemId] = states[i].count;
            }
            return snapshot;
        }

        private InventoryRewardApplyResult BuildResult(
            int currencyBefore,
            Dictionary<string, int> itemsBefore,
            IReadOnlyList<RewardItemStack> requestedStacks)
        {
            long rawCurrencyDelta = (long)SaveSystem.Data.currency - currencyBefore;
            int actualCurrencyDelta = rawCurrencyDelta <= 0L ? 0
                : rawCurrencyDelta >= int.MaxValue ? int.MaxValue
                : (int)rawCurrencyDelta;

            var deltas = new List<InventoryRewardItemDelta>();

            if (requestedStacks != null)
            {
                var seen = new HashSet<string>();
                for (int i = 0; i < requestedStacks.Count; i++)
                {
                    RewardItemStack stack = requestedStacks[i];
                    if (stack.Definition == null || stack.Count <= 0) continue;

                    string id = stack.Definition.ItemId;
                    if (string.IsNullOrEmpty(id)) continue;
                    if (!seen.Add(id)) continue;

                    if (!definitionsById.TryGetValue(id, out ItemDefinition def)) continue;

                    itemsBefore.TryGetValue(id, out int before);
                    int after = GetItemCount(id);
                    long rawDiff = (long)after - before;
                    if (rawDiff <= 0L) continue;
                    int diff = rawDiff >= int.MaxValue ? int.MaxValue : (int)rawDiff;

                    deltas.Add(new InventoryRewardItemDelta(def, id, diff));
                }
            }

            if (actualCurrencyDelta == 0 && deltas.Count == 0)
                return InventoryRewardApplyResult.Empty;

            return new InventoryRewardApplyResult(actualCurrencyDelta, deltas.ToArray());
        }

        /// <summary>인벤토리가 실제로 바뀐 뒤에만 호출한다 - 저장은 이 경로 하나뿐이라 매 프레임이나
        /// 입력마다 파일을 쓰는 경로가 존재하지 않는다.</summary>
        private void SaveAndNotify()
        {
            entryCacheDirty = true;

            if (!PersistToDisk())
            {
                Debug.LogError("[InventoryManager] 인벤토리를 저장하지 못했습니다 - 이번 실행에는 적용되지만 " +
                               "앱을 다시 켜면 이전 값으로 돌아갑니다.", this);
            }

            InventoryChanged?.Invoke();
        }

        private void RebuildEntryCacheIfNeeded()
        {
            if (!entryCacheDirty) return;
            entryCacheDirty = false;
            entryCache.Clear();

            List<InventoryItemState> states = SaveSystem.Data.items;
            for (int i = 0; i < states.Count; i++)
            {
                InventoryItemState state = states[i];
                if (state == null || state.count <= 0) continue;

                if (!definitionsById.TryGetValue(state.itemId, out ItemDefinition definition))
                {
                    // 정의가 사라졌거나 아직 카탈로그에 없는 id - 저장 값은 그대로 두고 표시만 건너뛴다.
                    if (warnedUnknownItemIds.Add(state.itemId))
                    {
                        Debug.LogWarning($"[InventoryManager] 저장된 아이템 '{state.itemId}'의 정의를 Item Catalog에서 " +
                                         "찾지 못해 인벤토리에 표시하지 않습니다(저장 값은 유지됩니다).", this);
                    }
                    continue;
                }

                entryCache.Add(new Entry(definition, state.count));
            }
        }

        // ---- 개발용 진입점 (정식 UI에 노출하지 않는다) ----

        [ContextMenu("Debug - Add Currency")]
        public void DebugAddCurrency()
        {
            AddCurrency(debugCurrencyAmount);
        }

        [ContextMenu("Debug - Set Currency To Zero")]
        public void DebugSetCurrencyToZero()
        {
            SetCurrency(0);
        }

        /// <summary>같은 아이템을 반복해서 눌러 수량 누적을 확인하는 용도로도 쓴다.</summary>
        [ContextMenu("Debug - Add Item")]
        public void DebugAddItem()
        {
            if (debugItem == null)
            {
                Debug.LogWarning("[InventoryManager] Debug Item이 비어 있어 추가할 아이템이 없습니다.", this);
                return;
            }

            AddItem(debugItem, debugItemCount);
        }

        /// <summary>등록된 모든 아이템을 1개씩 넣는다(두 출처를 합친 결과) - 여러 종류 표시와 빈 슬롯
        /// 처리를 한 번에 확인한다.</summary>
        [ContextMenu("Debug - Add One Of Every Item")]
        public void DebugAddOneOfEveryItem()
        {
            for (int i = 0; i < registeredDefinitions.Count; i++)
            {
                AddItem(registeredDefinitions[i], 1);
            }
        }

        [ContextMenu("Debug - Clear Inventory")]
        public void DebugClearInventory()
        {
            ClearInventory();
        }
    }
}
