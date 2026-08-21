using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Inventory
{
    /// <summary>
    /// 비용 지불이 거절된 이유. <see cref="InventoryCostResult"/> 하나로 "왜 못 냈는가"를 돌려주기
    /// 위한 값이며, 성공했을 때는 항상 <see cref="None"/>이다.
    ///
    /// 판정 순서가 곧 이 값의 우선순위다 - 요청 구조 오류(<see cref="InvalidRequest"/>) →
    /// 등록되지 않은 아이템(<see cref="UnknownItem"/>) → 재화 부족 → 아이템 부족. 잔액이 모자란다는
    /// 이유가 "요청 자체가 잘못됐다"를 가리는 일이 없도록 구조 검사를 먼저 끝낸다.
    /// </summary>
    public enum InventoryCostFailureReason
    {
        /// <summary>거절되지 않았다(지불 가능 / 지불 완료).</summary>
        None = 0,

        /// <summary>요청 값 자체가 성립하지 않는다 - 음수 재화, 음수 수량, 수량 1 이상인 칸의 빈
        /// Item Id, 중복 합산이 int 범위를 넘는 경우. 보유량과 무관한 <b>호출부의 버그</b>다.</summary>
        InvalidRequest = 1,

        /// <summary>요청한 Item Id가 Item Catalog에 등록되어 있지 않다. 보유량이 0인 것과는 다르다 -
        /// 그쪽은 <see cref="InsufficientItem"/>이다.</summary>
        UnknownItem = 2,

        /// <summary>재화가 모자란다.</summary>
        InsufficientCurrency = 3,

        /// <summary>아이템 하나가 모자란다(어느 것인지는 <see cref="InventoryCostResult.ItemId"/>).</summary>
        InsufficientItem = 4,
    }

    /// <summary>
    /// 비용 한 칸 - "어떤 아이템을 몇 개 낸다". 아이템을 가리키는 방법은 두 가지이고 <b>어느 쪽이든
    /// 저장 키가 되는 Item Id는 그대로 보존된다</b>.
    ///   - <see cref="Of"/>: <see cref="ItemDefinition"/> 에셋으로 지정(평소 경로)
    ///   - <see cref="ById"/>: Item Id 문자열로 직접 지정(정의 에셋 참조를 들고 있지 않은 호출부)
    ///
    /// Id 문자열은 <b>다듬지 않는다</b> - 앞뒤 공백을 지우거나 대소문자를 맞추면 저장 파일의 키와
    /// 다른 값을 가리키게 되므로, 받은 문자열 그대로 대조한다(등록되지 않은 값이면 그 사실이
    /// <see cref="InventoryCostFailureReason.UnknownItem"/>으로 드러난다).
    /// </summary>
    public readonly struct InventoryItemCost
    {
        private readonly string itemId;

        private InventoryItemCost(ItemDefinition definition, string itemId, int count)
        {
            Definition = definition;
            this.itemId = itemId;
            Count = count;
        }

        /// <summary>정의 에셋으로 비용 한 칸을 만든다. 정의가 null이거나 Item Id가 비어 있으면 빈 Id가
        /// 되고, 수량이 1 이상이면 그 요청은 <see cref="InventoryCostFailureReason.InvalidRequest"/>로
        /// 거절된다(수량 0이면 칸 자체가 무시되므로 빈 Id도 문제가 되지 않는다).</summary>
        public static InventoryItemCost Of(ItemDefinition definition, int count = 1)
        {
            return new InventoryItemCost(definition, definition != null ? definition.ItemId : string.Empty, count);
        }

        /// <summary>Item Id 문자열로 비용 한 칸을 만든다. 문자열은 받은 그대로 쓴다.</summary>
        public static InventoryItemCost ById(string itemId, int count = 1)
        {
            return new InventoryItemCost(null, itemId, count);
        }

        /// <summary>지정에 쓰인 정의 에셋. <see cref="ById"/>로 만들었으면 null이며, 이 값이 null이라고
        /// 해서 요청이 잘못된 것은 아니다 - 판정의 기준은 언제나 <see cref="ItemId"/>다.</summary>
        public ItemDefinition Definition { get; }

        /// <summary>저장 항목을 가리키는 키. 지정되지 않았으면 빈 문자열이다(null을 돌려주지 않는다).</summary>
        public string ItemId => itemId ?? string.Empty;

        /// <summary>낼 수량. 0은 "이 칸은 비용이 아니다"라는 뜻이라 <b>Id를 보지도 않고</b> 통째로
        /// 무시되고, 음수는 거절된다.</summary>
        public int Count { get; }
    }

    /// <summary>
    /// 비용 요청 하나 - 재화와 아이템 여러 칸을 <b>한 덩어리로</b> 표현한다. 만들어진 뒤에는 바뀌지
    /// 않으며(받은 목록도 복사해서 들고 있다), 같은 요청을 여러 번 평가해도 결과가 달라지지 않는다.
    ///
    /// <b>이 타입은 값을 담기만 한다.</b> 실제 판정과 차감은 <see cref="InventoryManager"/>가 한다 -
    /// 요청 객체가 저장 데이터를 직접 건드릴 수 있게 되면 "인벤토리를 바꾸는 경로는 InventoryManager
    /// 하나"라는 규칙이 무너지기 때문이다. 범용 트랜잭션 틀도 만들지 않는다(이번에 필요한 것은
    /// 재화 + 아이템 비용 한 종류뿐이다).
    /// </summary>
    public sealed class InventoryCostRequest
    {
        private static readonly ReadOnlyCollection<InventoryItemCost> NoCosts =
            Array.AsReadOnly(Array.Empty<InventoryItemCost>());

        /// <summary>아무 비용도 없는 요청. 언제나 지불 가능하고, 지불해도 저장/알림이 일어나지 않는다.</summary>
        public static readonly InventoryCostRequest Empty = new InventoryCostRequest(0, null);

        private readonly ReadOnlyCollection<InventoryItemCost> itemCosts;

        /// <summary>재화만 내는 요청.</summary>
        public InventoryCostRequest(int currency) : this(currency, null)
        {
        }

        /// <summary>재화 + 아이템 칸들로 요청을 만든다. 넘긴 목록은 <b>복사</b>되므로, 만든 뒤에
        /// 원본 목록을 바꿔도 이 요청은 달라지지 않는다.</summary>
        public InventoryCostRequest(int currency, IReadOnlyList<InventoryItemCost> itemCosts)
        {
            Currency = currency;

            if (itemCosts == null || itemCosts.Count == 0)
            {
                this.itemCosts = NoCosts;
                return;
            }

            var copy = new InventoryItemCost[itemCosts.Count];
            for (int i = 0; i < itemCosts.Count; i++) copy[i] = itemCosts[i];
            this.itemCosts = Array.AsReadOnly(copy);
        }

        /// <summary>재화 + 아이템 칸들을 한 줄로 적기 위한 편의 생성자.</summary>
        public static InventoryCostRequest Of(int currency, params InventoryItemCost[] itemCosts)
        {
            return new InventoryCostRequest(currency, itemCosts);
        }

        /// <summary>아이템 한 종만 내는 요청(정의 지정).</summary>
        public static InventoryCostRequest ForItem(ItemDefinition definition, int count = 1)
        {
            return new InventoryCostRequest(0, new[] { InventoryItemCost.Of(definition, count) });
        }

        /// <summary>아이템 한 종만 내는 요청(Item Id 지정).</summary>
        public static InventoryCostRequest ForItem(string itemId, int count = 1)
        {
            return new InventoryCostRequest(0, new[] { InventoryItemCost.ById(itemId, count) });
        }

        /// <summary>낼 재화. 음수는 요청 자체가 거절된다 - "음수 비용"은 지급이며, 지급 경로는
        /// <see cref="InventoryManager.ApplyRewards"/> 하나뿐이다.</summary>
        public int Currency { get; }

        /// <summary>낼 아이템 칸들(적은 순서 그대로, 중복 허용). 절대 null이 아니다.</summary>
        public IReadOnlyList<InventoryItemCost> ItemCosts => itemCosts;

        /// <summary>
        /// 아이템 칸들을 <b>같은 Item Id끼리 합산한 요구 목록</b>으로 정리한다. 순서는 각 Id가 처음
        /// 나타난 순서이며, 같은 요청이면 언제나 같은 목록이 나온다 - 판정과 차감이 같은 순서를 보게
        /// 하려고 이 한 곳에서만 정규화한다.
        ///
        /// 여기서 걸러 내는 것은 <b>보유량과 무관한 구조 오류</b>뿐이다(음수 재화/수량, 남아 있는
        /// 칸의 빈 Id, 합산 오버플로우). 카탈로그 등록 여부와 잔액은 <see cref="InventoryManager"/>가 본다.
        ///
        /// 수량 0인 칸은 <b>Id를 보기도 전에</b> 통째로 무시된다 - 요구 목록에 들어가지 않는 것은
        /// 물론이고, 빈 Id든 미등록 Id든 거절 사유가 되지 않는다. 낼 것이 없는 칸은 있으나 마나이므로
        /// 그 칸의 Id를 따질 이유도 없다.
        /// </summary>
        internal bool TryNormalize(List<InventoryItemRequirement> requirements, out string offendingItemId)
        {
            requirements.Clear();
            offendingItemId = string.Empty;

            if (Currency < 0) return false;

            for (int i = 0; i < itemCosts.Count; i++)
            {
                InventoryItemCost cost = itemCosts[i];
                string id = cost.ItemId;

                if (cost.Count < 0)
                {
                    offendingItemId = id;
                    return false;
                }

                // 수량 0인 칸은 여기서 완전히 끝난다 - Id를 아예 보지 않는다. "0개를 낸다"는 낼 것이
                // 없다는 뜻이고, 낼 것이 없는 칸의 Id는 어차피 아무것도 가리키지 않기 때문이다.
                // 그래서 수량 0 + 빈 Id도, 수량 0 + 미등록 Id도 조용히 무시된다. 음수 수량은 그 앞에서
                // 이미 거절됐으므로 "0으로 감춘 잘못된 요청"이 통과할 자리는 없다.
                if (cost.Count == 0) continue;

                if (string.IsNullOrWhiteSpace(id))
                {
                    offendingItemId = string.Empty;
                    return false;
                }

                int existing = IndexOf(requirements, id);
                if (existing < 0)
                {
                    requirements.Add(new InventoryItemRequirement(id, cost.Count));
                    continue;
                }

                // 같은 Id가 여러 칸에 나뉘어 들어온 경우. long으로 더해야 int 범위를 넘는 순간을
                // 볼 수 있다 - int로 더하면 넘친 값이 음수가 되어 "싼 비용"으로 통과한다.
                long sum = (long)requirements[existing].Count + cost.Count;
                if (sum > int.MaxValue)
                {
                    offendingItemId = id;
                    return false;
                }

                requirements[existing] = new InventoryItemRequirement(id, (int)sum);
            }

            return true;
        }

        private static int IndexOf(List<InventoryItemRequirement> requirements, string itemId)
        {
            for (int i = 0; i < requirements.Count; i++)
            {
                if (requirements[i].ItemId == itemId) return i;
            }
            return -1;
        }
    }

    /// <summary>같은 Item Id끼리 합산이 끝난 요구 한 줄. 판정과 차감이 같은 목록을 보게 하려고
    /// 두는 내부 값이며, 밖으로 나가지 않는다.</summary>
    internal readonly struct InventoryItemRequirement
    {
        public InventoryItemRequirement(string itemId, int count)
        {
            ItemId = itemId;
            Count = count;
        }

        public string ItemId { get; }
        public int Count { get; }
    }

    /// <summary>
    /// 비용 판정/지불의 불변 결과. 성공/실패 하나만이 아니라 <b>무엇이 얼마나 모자랐는지</b>까지
    /// 담는다 - 호출부가 실패를 다시 계산하려고 보유량을 또 읽지 않게 하기 위함이다.
    ///
    /// <see cref="InventoryManager.EvaluateCost"/>가 돌려주면 "낼 수 있는가"이고,
    /// <see cref="InventoryManager.TrySpendCost"/>가 돌려주면 "실제로 냈는가"다. 두 경우 모두
    /// 실패 결과의 의미는 같으며, 실패한 지불은 아무것도 바꾸지 않는다.
    /// </summary>
    public sealed class InventoryCostResult
    {
        /// <summary>지불 가능(또는 지불 완료). 비용이 아예 없는 요청도 이 결과다.</summary>
        public static readonly InventoryCostResult Payable =
            new InventoryCostResult(true, InventoryCostFailureReason.None, string.Empty, 0, 0);

        private readonly string itemId;

        private InventoryCostResult(
            bool success,
            InventoryCostFailureReason reason,
            string itemId,
            int requiredAmount,
            int currentAmount)
        {
            Success = success;
            Reason = reason;
            this.itemId = itemId;
            RequiredAmount = requiredAmount;
            CurrentAmount = currentAmount;
        }

        /// <summary>판정이 통과했는가(지불에서는 실제로 차감되었는가).</summary>
        public bool Success { get; }

        /// <summary><see cref="Success"/>와 같은 값. 판정 결과를 읽는 자리에서 더 자연스럽게 읽힌다.</summary>
        public bool IsPayable => Success;

        /// <summary>거절 이유. 성공이면 <see cref="InventoryCostFailureReason.None"/>이다.</summary>
        public InventoryCostFailureReason Reason { get; }

        /// <summary>이유와 관련된 아이템의 Id. 재화 문제이거나 관련 아이템이 없으면 빈 문자열이다
        /// (null을 돌려주지 않는다).</summary>
        public string ItemId => itemId ?? string.Empty;

        /// <summary>필요했던 양(재화 또는 그 아이템의 합산 요구량). 구조 오류로 거절된 경우에는
        /// 요구량이 성립하지 않으므로 0이다.</summary>
        public int RequiredAmount { get; }

        /// <summary>판정 시점의 보유량(재화 또는 그 아이템). 구조 오류/미등록으로 거절된 경우에는 0이다.</summary>
        public int CurrentAmount { get; }

        internal static InventoryCostResult Invalid(string itemId)
        {
            return new InventoryCostResult(false, InventoryCostFailureReason.InvalidRequest, itemId, 0, 0);
        }

        internal static InventoryCostResult UnknownItem(string itemId, int requiredAmount)
        {
            return new InventoryCostResult(false, InventoryCostFailureReason.UnknownItem, itemId, requiredAmount, 0);
        }

        internal static InventoryCostResult InsufficientCurrency(int requiredAmount, int currentAmount)
        {
            return new InventoryCostResult(
                false, InventoryCostFailureReason.InsufficientCurrency, string.Empty, requiredAmount, currentAmount);
        }

        internal static InventoryCostResult InsufficientItem(string itemId, int requiredAmount, int currentAmount)
        {
            return new InventoryCostResult(
                false, InventoryCostFailureReason.InsufficientItem, itemId, requiredAmount, currentAmount);
        }
    }
}
