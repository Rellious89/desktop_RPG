using System;
using System.Collections.Generic;
using Common;
using Inventory;
using UnityEngine;

namespace Building
{
    /// <summary>
    /// 건물 한 종의 <b>정적 정의</b> - "이 건물이 무엇이고 무엇을 내면 지을 수 있는가"만 담는다
    /// (식별자, 이름, 해금되는 기능 이름, 건설 시간, 비용, 목록 순서).
    /// <see cref="Inventory.ItemDefinition"/> / <see cref="Inventory.CurrencyDefinition"/> /
    /// <see cref="Skill.SkillDefinition"/>과 같은 역할 분담이다.
    ///
    /// <b>여기에 진행 상태는 없다.</b> 지금 지었는지, 언제 완성되는지, 몇 번 지었는지는 하나도 담지
    /// 않는다 - 그것은 저장 데이터의 몫이고, 이 에셋은 "이 게임에 어떤 건물이 있는가"만 말한다.
    /// 이번 단계에서 저장 형식(<see cref="Common.SaveData.CurrentSaveVersion"/>)은 <b>손대지 않는다</b>.
    ///
    /// <b>여기에 동작도 없다.</b> 비용을 실제로 깎거나 건설을 시작하는 코드는 이 에셋 바깥에 있다 -
    /// <see cref="ToCostRequest"/>는 "무엇을 내야 하는가"를 <see cref="InventoryCostRequest"/> 값으로
    /// <b>만들어 돌려주기만</b> 하며, 인벤토리를 읽지도 바꾸지도 저장하지도 않는다.
    ///
    /// <b>Building Id는 절대 다른 값으로 대체하지 않는다.</b> 비어 있으면 빈 문자열이며 에셋 파일
    /// 이름도 표시 이름도 대신 쓰지 않는다(<see cref="Inventory.CurrencyDefinition"/>과 같은 규칙).
    /// <b>자동 정규화도 일절 하지 않는다</b> - '1'과 ' 1 '은 다른 값이다.
    ///
    /// <b><c>enabled</c> 칸은 여기에도 있다.</b> Building.csv의 <c>enabled</c>는 표가 이 건물에 대해
    /// 적어 둔 값이므로 정의 에셋도 <see cref="Enabled"/>로 그대로 들고 있는다 - 에셋 하나만 열어도
    /// "표에서 이 건물이 켜져 있는가"를 알 수 있어야 하기 때문이다.
    ///
    /// 다만 <b>목록을 정하는 것은 여전히 <see cref="BuildingCatalog"/> 하나</b>다. 카탈로그에는 켜진
    /// 행만 들어가므로 꺼진 행은 <see cref="Enabled"/>가 false인 정의 에셋으로 남아 있고(에셋이 남아
    /// 있어야 다시 켤 때 GUID와 참조가 그대로 살아난다) 목록에는 나오지 않는다. 이 칸을 읽어 목록을
    /// 한 번 더 거르는 코드를 두면 "무엇이 목록인가"를 정하는 곳이 둘이 되어 어긋날 자리가 생긴다 -
    /// 이 값은 <b>표가 적어 둔 사실</b>이지 목록 판정이 아니다.
    /// </summary>
    [CreateAssetMenu(fileName = "BuildingDefinition", menuName = "Building/Building Definition")]
    public class BuildingDefinition : ScriptableObject
    {
        /// <summary>
        /// 건설 비용의 아이템 한 칸 - "이 아이템을 몇 개 낸다". <b>Item Id 문자열과 정의 에셋을 함께</b>
        /// 들고 있다: 저장 파일이 아이템을 가리키는 키는 언제나 Id 문자열이고, 정의 에셋은 그 아이템을
        /// 화면에 그릴 때 쓰는 표시 정보다. 둘을 같은 행에서 함께 쓰므로 어긋날 수 없다
        /// (<see cref="Skill.CharacterSkillDefinition"/>이 id와 참조를 함께 들고 있는 것과 같은 방식이다).
        /// </summary>
        [Serializable]
        public sealed class ItemCostEntry
        {
            [Tooltip("낼 아이템의 Item Id. 저장 파일이 아이템을 가리키는 키이며, Building.csv의 " +
                     "cost_item_ids에 적힌 값이 한 글자도 바뀌지 않고 들어온다.")]
            [SerializeField] private string itemId;

            [Tooltip("낼 아이템의 정의. cost_item_ids가 가리키는 Item.csv 행에서 만들어진다. " +
                     "판정의 기준은 언제나 Item Id이고 이 참조는 표시를 위한 것이다.")]
            [SerializeField] private ItemDefinition item;

            [Tooltip("낼 개수. 1 이상만 들어온다 - 0개짜리 비용 칸은 임포터가 아예 만들지 않는다.")]
            [Min(1)]
            [SerializeField] private int count = 1;

            /// <summary>저장 항목을 가리키는 키. 지정되지 않았으면 빈 문자열이다(null을 돌려주지 않는다).
            /// <b>적힌 그대로</b> 돌려주며 앞뒤 공백을 떼거나 대소문자를 맞추지 않는다.</summary>
            public string ItemId => string.IsNullOrWhiteSpace(itemId) ? string.Empty : itemId;

            /// <summary>낼 아이템의 정의. 정의가 사라졌으면 null이며, 그때도 <see cref="ItemId"/>는 남는다.</summary>
            public ItemDefinition Item => item;

            /// <summary>낼 개수. 최소 1을 보장한다 - 0개짜리 비용이 목록에 남아 있지 않게 한다.</summary>
            public int Count => Mathf.Max(1, count);

            /// <summary>실제로 낼 것이 있는 칸인지. Item Id가 비어 있으면 무엇을 내야 하는지 알 수 없다.</summary>
            public bool IsValid => !string.IsNullOrWhiteSpace(itemId);
        }

        private static readonly ItemCostEntry[] EmptyItemCosts = new ItemCostEntry[0];

        [Header("Identity")]
        [Tooltip("표와 저장 데이터가 이 건물을 가리키는 키. <b>반드시 직접 적는다</b> - 비워두면 이 " +
                 "건물은 목록에 나오지 않는다(파일 이름으로 대체하지 않는다). 적은 그대로 쓰이며 " +
                 "대소문자를 구분하고 앞뒤 공백도 떼지 않는다.")]
        [SerializeField] private string buildingId;

        [Header("Localization")]
        [Tooltip("화면에 표시할 건물 이름. 카테고리 번호 + 숫자 키로 지정한다.")]
        [SerializeField] private LocalizedTextReference localizedName = new LocalizedTextReference();

        [Tooltip("이 건물을 지으면 열리는 기능의 이름. 건물 이름과 <b>다른 표를 가리켜도 된다</b> - " +
                 "여관(07_Building)이 여는 기능은 용병 모집(01_UI)처럼 이미 UI 표에 있는 문구다.")]
        [SerializeField] private LocalizedTextReference localizedFunctionName = new LocalizedTextReference();

        [Header("Construction")]
        [Tooltip("건설에 걸리는 시간(초). 0이면 즉시 완성이라는 뜻이며, 음수는 들어오지 않는다. " +
                 "<b>남은 시간은 여기가 아니다</b> - 그것은 저장 데이터의 몫이다.")]
        [Min(0)]
        [SerializeField] private int buildTimeSeconds;

        [Header("Cost")]
        [Tooltip("건설 비용으로 낼 재화의 Currency Id. 저장 파일이 재화를 가리키는 키이며, " +
                 "Building.csv의 cost_currency_id에 적힌 값이 그대로 들어온다. 비어 있으면 이 건물은 " +
                 "표에서 재화 비용을 지정하지 않은 것이다.")]
        [SerializeField] private string costCurrencyId;

        // 재화를 <b>id 문자열만이 아니라 정의 에셋으로도</b> 가리킨다 - 참조가 있다는 것 자체가
        // "그 재화가 Currency.csv에 실제로 있고 활성이다"라는 뜻이 되기 때문이다
        // (<see cref="Dungeon.MonsterDefinition"/>의 처치 재화 보상과 같은 규칙).
        [Tooltip("건설 비용으로 낼 재화의 정의. cost_currency_id가 가리키는 Currency.csv 행에서 " +
                 "만들어진다. 비어 있으면 재화 비용이 없다는 뜻이다.")]
        [SerializeField] private CurrencyDefinition costCurrency;

        [Tooltip("낼 재화 금액. 재화를 지정하지 않았으면 0이다. 음수는 들어오지 않는다.")]
        [Min(0)]
        [SerializeField] private int costCurrencyAmount;

        [Tooltip("건설 비용으로 낼 아이템들. 비어 있는 것이 정상이다 - 재화만 내는 건물이 있다.")]
        [SerializeField] private List<ItemCostEntry> costItems = new List<ItemCostEntry>();

        [Header("Ordering")]
        [Tooltip("건물을 정렬할 때 쓰는 순서 값. 작을수록 앞이다 - 이 값 자체가 목록을 만들지는 " +
                 "않으며, 목록의 순서는 BuildingCatalog의 작성 순서가 결정한다.")]
        [SerializeField] private int displayOrder;

        [Header("Availability")]
        [Tooltip("Building.csv의 enabled 값을 그대로 옮겨 둔 것. 이 값 자체가 목록을 만들지는 " +
                 "않는다 - 목록에 들어갈지는 BuildingCatalog가 이미 정했고, 꺼진 건물의 에셋은 " +
                 "이 칸이 꺼진 채로 남아 있다.")]
        [SerializeField] private bool enabled;

        /// <summary>이 건물을 가리키는 키. 비어 있거나 공백뿐이면 빈 문자열을 돌려주고, 그 외에는
        /// <b>적힌 문자열을 한 글자도 바꾸지 않고 그대로</b> 돌려준다.</summary>
        public string BuildingId => string.IsNullOrWhiteSpace(buildingId) ? string.Empty : buildingId;

        /// <summary>목록에 올릴 수 있는 건물인지 여부. 지금 기준은 식별자 하나뿐이며, 이름이나 비용이
        /// 비어 있는 것은 표시/밸런스 문제이지 목록에서 빼야 할 이유가 아니다.</summary>
        public bool IsValid => !string.IsNullOrWhiteSpace(buildingId);

        /// <summary>건물 이름 참조. <b>절대 null을 돌려주지 않는다</b>.</summary>
        public LocalizedTextReference LocalizedName =>
            localizedName ?? (localizedName = new LocalizedTextReference());

        /// <summary>건물 이름의 Table/Key가 지정되어 있는지 여부(번역 값의 존재를 보장하지는 않는다).</summary>
        public bool HasLocalizedName => localizedName != null && localizedName.HasReference;

        /// <summary>해금되는 기능 이름 참조. <b>절대 null을 돌려주지 않는다</b>.</summary>
        public LocalizedTextReference LocalizedFunctionName =>
            localizedFunctionName ?? (localizedFunctionName = new LocalizedTextReference());

        /// <summary>기능 이름의 Table/Key가 지정되어 있는지 여부.</summary>
        public bool HasLocalizedFunctionName =>
            localizedFunctionName != null && localizedFunctionName.HasReference;

        /// <summary>건설에 걸리는 시간(초). 0 이상을 보장한다.</summary>
        public int BuildTimeSeconds => Mathf.Max(0, buildTimeSeconds);

        /// <summary>낼 재화의 Currency Id. 지정하지 않았으면 빈 문자열이며 <b>적힌 그대로</b> 돌려준다.</summary>
        public string CostCurrencyId => string.IsNullOrWhiteSpace(costCurrencyId) ? string.Empty : costCurrencyId;

        /// <summary>낼 재화의 정의. 지정하지 않았으면 null이다.</summary>
        public CurrencyDefinition CostCurrency => costCurrency;

        /// <summary>낼 재화 금액. 0 이상을 보장한다.</summary>
        public int CostCurrencyAmount => Mathf.Max(0, costCurrencyAmount);

        /// <summary>낼 아이템 칸들(적힌 순서 그대로). 항목이 없으면 빈 목록이며 <b>null이 아니다</b>.</summary>
        public IReadOnlyList<ItemCostEntry> CostItems =>
            (IReadOnlyList<ItemCostEntry>)costItems ?? EmptyItemCosts;

        /// <summary>정렬용 순서 값. 작을수록 앞이며, 지정하지 않으면 0이다.</summary>
        public int DisplayOrder => displayOrder;

        /// <summary>표가 이 건물을 켜 두었는지 여부(Building.csv의 <c>enabled</c> 값 그대로).
        /// <b>목록 판정에 다시 쓰지 않는다</b> - 목록은 <see cref="BuildingCatalog"/>가 이미
        /// 켜진 행만 담고 있으므로, 이 값은 "표에 무엇이 적혀 있었는가"를 말해 줄 뿐이다.</summary>
        public bool Enabled => enabled;

        /// <summary>
        /// 이 건물의 건설 비용을 <see cref="InventoryCostRequest"/> <b>값으로 만들어</b> 돌려준다.
        ///
        /// <b>부작용이 하나도 없다.</b> 인벤토리를 읽지도 바꾸지도 않고, 저장하지도 알림을 내지도
        /// 않으며, <see cref="InventoryManager.EvaluateCost"/>나
        /// <see cref="InventoryManager.TrySpendCost"/>를 부르지도 않는다 - 같은 정의에 대해 몇 번을
        /// 불러도 프로젝트도 저장 파일도 한 글자 달라지지 않는다. "낼 수 있는가"와 "실제로 낸다"는
        /// 언제나 <see cref="InventoryManager"/>가 판정하며, 그 규칙이 이 에셋 쪽으로 새어 나오면
        /// "인벤토리를 바꾸는 경로는 InventoryManager 하나"라는 규칙이 무너진다.
        ///
        /// <b>재화는 금액만 넘어간다.</b> <see cref="InventoryCostRequest.Currency"/>는 지금 게임의
        /// 잔액 하나(<c>SaveData.currency</c>)를 가리키는 칸이라 재화 종류를 담지 않는다 -
        /// <see cref="Dungeon.MonsterDefinition"/>의 처치 재화 보상이 유효한 참조를 그 하나뿐인 잔액으로
        /// 지급하는 것과 <b>같은 규칙</b>이며, 어느 재화인지는 <see cref="CostCurrency"/> /
        /// <see cref="CostCurrencyId"/>가 이미 정확히 말해 준다. 재화별 잔액이 생기는 날 바꿔야 하는
        /// 곳은 이 한 줄이다.
        ///
        /// 아이템 칸은 <b>Item Id를 기준</b>으로 만든다. 정의 에셋이 있고 그 Id가 표에 적힌 Id와 정확히
        /// 같을 때만 정의를 함께 실어 보내며, 어긋나면 Id 쪽을 남긴다 - 저장 파일의 키는 언제나
        /// Id 문자열이기 때문이다. 아이템이 하나도 없으면 재화만 내는 요청이 된다.
        /// </summary>
        public InventoryCostRequest ToCostRequest()
        {
            if (costItems == null || costItems.Count == 0) return new InventoryCostRequest(CostCurrencyAmount);

            var costs = new List<InventoryItemCost>(costItems.Count);
            for (int i = 0; i < costItems.Count; i++)
            {
                ItemCostEntry entry = costItems[i];
                if (entry == null) continue;

                string id = entry.ItemId;
                bool definitionAgrees =
                    entry.Item != null && string.Equals(entry.Item.ItemId, id, StringComparison.Ordinal);

                costs.Add(definitionAgrees
                    ? InventoryItemCost.Of(entry.Item, entry.Count)
                    : InventoryItemCost.ById(id, entry.Count));
            }

            return new InventoryCostRequest(CostCurrencyAmount, costs);
        }
    }
}
