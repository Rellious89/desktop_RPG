using System;
using System.Collections.Generic;
using Common;
using UnityEngine;

namespace Inventory
{
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
    /// <b>몬스터 처치 보상과는 아직 연결하지 않았다.</b> 값을 넣는 경로는 아래 개발용 진입점뿐이며,
    /// 정식 획득 규칙이 정해지면 그때 <see cref="AddCurrency"/>/<see cref="AddItem"/>을 호출하면 된다.
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
        /// <see cref="ApplyRewards"/>를 쓴다 - 이쪽은 한 칸 전용이라 목록도 버퍼도 만들지 않는다.
        /// </summary>
        public void ApplyReward(int currencyAmount, ItemDefinition item, int itemCount = 1)
        {
            bool changed = ApplyCurrencyDelta(currencyAmount);
            // 비트 OR로 두 호출을 모두 실행한다 - ||를 쓰면 재화가 바뀐 순간 아이템 지급이 통째로 생략된다.
            changed |= ApplyItemDelta(item, itemCount);

            if (changed) SaveAndNotify();
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
        /// </summary>
        public void ApplyRewards(int currencyAmount, IReadOnlyList<RewardItemStack> itemStacks)
        {
            bool changed = ApplyCurrencyDelta(currencyAmount);

            if (itemStacks != null)
            {
                for (int i = 0; i < itemStacks.Count; i++)
                {
                    // 비트 OR로 모든 칸을 반드시 실행한다 - ||를 쓰면 앞의 칸이 성공한 순간 뒤가 생략된다.
                    changed |= ApplyItemDelta(itemStacks[i].Definition, itemStacks[i].Count);
                }
            }

            if (changed) SaveAndNotify();
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

        /// <summary>메모리의 재화 값만 바꾼다(저장/알림 없음). 실제로 값이 달라졌으면 true.</summary>
        private bool ApplyCurrencyDelta(int amount)
        {
            return amount != 0 && ApplyCurrencyValue(SaveSystem.Data.currency + amount);
        }

        private bool ApplyCurrencyValue(int value)
        {
            int clamped = Mathf.Max(0, value);
            if (clamped == SaveSystem.Data.currency) return false;

            SaveSystem.Data.currency = clamped;
            return true;
        }

        /// <summary>메모리의 아이템 수량만 바꾼다(저장/알림 없음). 실제로 값이 달라졌으면 true.</summary>
        private bool ApplyItemDelta(ItemDefinition definition, int count)
        {
            if (definition == null || count <= 0) return false;

            if (!definitionsById.ContainsKey(definition.ItemId))
            {
                // 카탈로그에 없는 아이템을 넣으면 저장은 되지만 인벤토리에 그려지지 않아 "사라진 것처럼"
                // 보인다 - 조용히 넘어가지 않고 여기서 막는다.
                Debug.LogError($"[InventoryManager] '{definition.ItemId}'가 Item Catalog에 없어 추가하지 않았습니다 - " +
                               "Inspector의 Generated Item Catalog(Item.csv로 만든 아이템) 또는 Item Catalog 목록에 " +
                               "이 정의를 등록하세요.", this);
                return false;
            }

            List<InventoryItemState> states = SaveSystem.Data.items;
            for (int i = 0; i < states.Count; i++)
            {
                if (states[i] == null || states[i].itemId != definition.ItemId) continue;

                states[i].count += count;
                return true;
            }

            // 처음 획득하는 아이템 - 목록 맨 뒤에 추가되고, 이 순서가 그대로 표시 순서가 된다.
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

        /// <summary>인벤토리가 실제로 바뀐 뒤에만 호출한다 - 저장은 이 경로 하나뿐이라 매 프레임이나
        /// 입력마다 파일을 쓰는 경로가 존재하지 않는다.</summary>
        private void SaveAndNotify()
        {
            entryCacheDirty = true;

            if (!SaveSystem.Save())
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
