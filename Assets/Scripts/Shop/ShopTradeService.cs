using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using Building;
using Common;
using Inventory;

namespace Shop
{
    /// <summary>상점 한 건의 구매 또는 판매 처리 결과다. 실패는 저장이나 인벤토리 알림을 발생시키지 않는다.</summary>
    public enum ShopTradeResultCode
    {
        Purchased,
        Sold,
        InvalidRequest,
        NoSaveData,
        UnknownShop,
        ShopLocked,
        UnknownProduct,
        UnknownItem,
        ItemSalesDisabled,
        ItemNotSellable,
        UnsupportedCurrency,
        InvalidPrice,
        InsufficientCurrency,
        InsufficientItem,
        CurrencyOverflow,
        ItemOverflow,
        TotalPriceOverflow,
        SaveFailed,
        Reentrant,
        DuplicateItemId,
    }

    /// <summary>상점 거래가 실제로 적용한 수량과 거래 전후 보유량을 담는 불변 결과다.</summary>
    public readonly struct ShopTradeResult
    {
        internal ShopTradeResult(ShopTradeResultCode code, string shopId, string itemId, int quantity,
            int unitPrice, int totalPrice, int itemCountBefore, int itemCountAfter,
            int currencyBefore, int currencyAfter)
        {
            Code = code;
            ShopId = shopId ?? string.Empty;
            ItemId = itemId ?? string.Empty;
            Quantity = quantity;
            UnitPrice = unitPrice;
            TotalPrice = totalPrice;
            ItemCountBefore = itemCountBefore;
            ItemCountAfter = itemCountAfter;
            CurrencyBefore = currencyBefore;
            CurrencyAfter = currencyAfter;
        }

        public ShopTradeResultCode Code { get; }
        public string ShopId { get; }
        public string ItemId { get; }
        public int Quantity { get; }
        public int UnitPrice { get; }
        public int TotalPrice { get; }
        public int ItemCountBefore { get; }
        public int ItemCountAfter { get; }
        public int CurrencyBefore { get; }
        public int CurrencyAfter { get; }
        public bool Success => Code == ShopTradeResultCode.Purchased || Code == ShopTradeResultCode.Sold;
    }

    /// <summary>일괄 판매에서 한 종류의 아이템과 수량을 요청하는 불변 줄이다.</summary>
    public readonly struct ShopSellLine
    {
        public ShopSellLine(string itemId, int quantity)
        {
            ItemId = itemId ?? string.Empty;
            Quantity = quantity;
        }

        public string ItemId { get; }
        public int Quantity { get; }
    }

    /// <summary>성공한 일괄 판매의 아이템별 확정 내역이다.</summary>
    public readonly struct ShopSellBatchItemResult
    {
        internal ShopSellBatchItemResult(string itemId, int quantity, int unitPrice, int totalPrice)
        {
            ItemId = itemId ?? string.Empty;
            Quantity = quantity;
            UnitPrice = unitPrice;
            TotalPrice = totalPrice;
        }

        public string ItemId { get; }
        public int Quantity { get; }
        public int UnitPrice { get; }
        public int TotalPrice { get; }
    }

    /// <summary>일괄 판매의 불변 결과다. 실패 결과는 아이템 내역을 공개하지 않는다.</summary>
    public sealed class ShopSellBatchResult
    {
        private readonly ReadOnlyCollection<ShopSellBatchItemResult> items;

        internal ShopSellBatchResult(ShopTradeResultCode code, string shopId,
            ShopSellBatchItemResult[] itemResults, int totalPrice, int currencyBefore, int currencyAfter)
        {
            Code = code;
            ShopId = shopId ?? string.Empty;
            items = Array.AsReadOnly(itemResults ?? Array.Empty<ShopSellBatchItemResult>());
            TotalPrice = totalPrice;
            CurrencyBefore = currencyBefore;
            CurrencyAfter = currencyAfter;
        }

        public ShopTradeResultCode Code { get; }
        public string ShopId { get; }
        public ReadOnlyCollection<ShopSellBatchItemResult> Items => items;
        public int TotalPrice { get; }
        public int CurrencyBefore { get; }
        public int CurrencyAfter { get; }
        public bool Success => Code == ShopTradeResultCode.Sold;
    }

    /// <summary>
    /// Shop/ShopProduct/Item 정의를 권위로 삼아 인벤토리 거래 한 건을 원자적으로 저장한다.
    /// 인벤토리 변경은 먼저 메모리에만 적용하고, 저장 한 번이 성공한 뒤에만 인벤토리 갱신을 알린다.
    /// </summary>
    public sealed class ShopTradeService
    {
        private const string SupportedCurrencyId = "jewel";

        /// <summary>판매 UI와 거래가 같은 ItemDefinition 판매 메타데이터를 판정하게 하는 공용 경로다.</summary>
        public static bool TryGetSellUnitPrice(ItemDefinition item, out int unitPrice)
        {
            unitPrice = 0;
            if (item == null || !item.IsValid || !item.Sellable || item.SellPrice <= 0 ||
                !string.Equals(item.SellCurrencyId, SupportedCurrencyId, StringComparison.Ordinal)) return false;
            unitPrice = item.SellPrice;
            return true;
        }

        private readonly Func<SaveData> dataProvider;
        private readonly Func<bool> saveAction;
        private readonly Func<DateTime> utcNowProvider;
        private readonly ShopCatalog shopCatalog;
        private readonly ShopProductCatalog shopProductCatalog;
        private readonly ItemCatalog itemCatalog;
        private readonly InventoryManager inventory;
        private bool inProgress;

        public ShopTradeService(Func<SaveData> dataProvider, Func<bool> saveAction, Func<DateTime> utcNowProvider,
            ShopCatalog shopCatalog, ShopProductCatalog shopProductCatalog, ItemCatalog itemCatalog,
            InventoryManager inventory)
        {
            this.dataProvider = dataProvider;
            this.saveAction = saveAction;
            this.utcNowProvider = utcNowProvider;
            this.shopCatalog = shopCatalog;
            this.shopProductCatalog = shopProductCatalog;
            this.itemCatalog = itemCatalog;
            this.inventory = inventory;
        }

        public ShopTradeResult TryBuy(string shopId, string itemId, int quantity)
        {
            return TryExecute(shopId, itemId, quantity, isPurchase: true);
        }

        public ShopTradeResult TrySell(string shopId, string itemId, int quantity)
        {
            return TryExecute(shopId, itemId, quantity, isPurchase: false);
        }

        /// <summary>
        /// 여러 종류의 아이템을 한 번에 판매한다. 모든 카탈로그/보유량/가격을 먼저 검증하고,
        /// 인벤토리 메모리 변경 한 번, 저장 한 번, 성공 뒤 알림 한 번으로 확정한다.
        /// </summary>
        public ShopSellBatchResult TrySellBatch(string shopId, IReadOnlyList<ShopSellLine> lines)
        {
            if (inProgress) return BatchResult(ShopTradeResultCode.Reentrant, shopId);

            inProgress = true;
            try
            {
                if (string.IsNullOrWhiteSpace(shopId) || lines == null || lines.Count == 0 || shopCatalog == null ||
                    itemCatalog == null || inventory == null || dataProvider == null || saveAction == null ||
                    utcNowProvider == null)
                {
                    return BatchResult(ShopTradeResultCode.InvalidRequest, shopId);
                }

                SaveData data = dataProvider();
                if (data == null) return BatchResult(ShopTradeResultCode.NoSaveData, shopId);

                ShopDefinition shop = shopCatalog.Find(shopId);
                if (shop == null) return BatchResult(ShopTradeResultCode.UnknownShop, shopId, data.currency);
                if (shop.RequiredBuildingId > 0 && !BuildingCompletionPolicy.IsConfirmedCompleted(
                        data, shop.RequiredBuildingId.ToString(CultureInfo.InvariantCulture), utcNowProvider()))
                {
                    return BatchResult(ShopTradeResultCode.ShopLocked, shopId, data.currency);
                }
                if (!shop.AcceptItemSales) return BatchResult(ShopTradeResultCode.ItemSalesDisabled, shopId, data.currency);

                var seenItemIds = new HashSet<string>(StringComparer.Ordinal);
                var mutations = new List<InventoryTradeBatchLine>(lines.Count);
                var results = new List<ShopSellBatchItemResult>(lines.Count);
                long totalPriceLong = 0L;
                for (int i = 0; i < lines.Count; i++)
                {
                    ShopSellLine line = lines[i];
                    if (string.IsNullOrWhiteSpace(line.ItemId) || line.Quantity <= 0)
                        return BatchResult(ShopTradeResultCode.InvalidRequest, shopId, data.currency);

                    ItemDefinition item = itemCatalog.Find(line.ItemId);
                    if (item == null || !item.IsValid)
                        return BatchResult(ShopTradeResultCode.UnknownItem, shopId, data.currency);
                    if (!seenItemIds.Add(item.ItemId))
                        return BatchResult(ShopTradeResultCode.DuplicateItemId, shopId, data.currency);
                    if (!item.Sellable)
                        return BatchResult(ShopTradeResultCode.ItemNotSellable, shopId, data.currency);
                    if (!string.Equals(item.SellCurrencyId, SupportedCurrencyId, StringComparison.Ordinal))
                        return BatchResult(ShopTradeResultCode.UnsupportedCurrency, shopId, data.currency);
                    if (!TryGetSellUnitPrice(item, out int unitPrice))
                        return BatchResult(ShopTradeResultCode.InvalidPrice, shopId, data.currency);

                    long lineTotal = (long)unitPrice * line.Quantity;
                    if (lineTotal > int.MaxValue)
                        return BatchResult(ShopTradeResultCode.TotalPriceOverflow, shopId, data.currency);
                    totalPriceLong += lineTotal;
                    if (totalPriceLong > int.MaxValue)
                        return BatchResult(ShopTradeResultCode.TotalPriceOverflow, shopId, data.currency);

                    int lineTotalInt = (int)lineTotal;
                    mutations.Add(new InventoryTradeBatchLine(item, -line.Quantity));
                    results.Add(new ShopSellBatchItemResult(item.ItemId, line.Quantity, unitPrice, lineTotalInt));
                }

                if ((long)data.currency + totalPriceLong > int.MaxValue)
                    return BatchResult(ShopTradeResultCode.CurrencyOverflow, shopId, data.currency);

                SaveMetadataSnapshot metadata = SaveMetadataSnapshot.Capture(data);
                InventoryTradeBatchMutationResult mutation = inventory.TryApplyTradeBatchWithoutSave(
                    mutations, (int)totalPriceLong, out InventoryTradeBatchMutationReceipt receipt);
                if (!mutation.Success)
                    return BatchResult(MapBatchMutationCode(mutation.Code), shopId, mutation.CurrencyBefore);

                try
                {
                    if (!saveAction())
                    {
                        inventory.RollbackTradeBatchWithoutSave(receipt);
                        SaveData.RestoreMetadata(data, metadata);
                        return BatchResult(ShopTradeResultCode.SaveFailed, shopId, mutation.CurrencyBefore);
                    }
                }
                catch
                {
                    inventory.RollbackTradeBatchWithoutSave(receipt);
                    SaveData.RestoreMetadata(data, metadata);
                    throw;
                }

                inventory.NotifyChangedAfterExternalSave();
                return new ShopSellBatchResult(ShopTradeResultCode.Sold, shopId, results.ToArray(),
                    (int)totalPriceLong, mutation.CurrencyBefore, mutation.CurrencyAfter);
            }
            finally
            {
                inProgress = false;
            }
        }

        private ShopTradeResult TryExecute(string shopId, string itemId, int quantity, bool isPurchase)
        {
            if (inProgress) return Result(ShopTradeResultCode.Reentrant, shopId, itemId, quantity);

            inProgress = true;
            try
            {
                if (string.IsNullOrWhiteSpace(shopId) || string.IsNullOrWhiteSpace(itemId) || quantity <= 0 ||
                    shopCatalog == null || itemCatalog == null || inventory == null || dataProvider == null ||
                    saveAction == null || utcNowProvider == null)
                {
                    return Result(ShopTradeResultCode.InvalidRequest, shopId, itemId, quantity);
                }

                SaveData data = dataProvider();
                if (data == null) return Result(ShopTradeResultCode.NoSaveData, shopId, itemId, quantity);

                ShopDefinition shop = shopCatalog.Find(shopId);
                if (shop == null) return Result(ShopTradeResultCode.UnknownShop, shopId, itemId, quantity);

                if (shop.RequiredBuildingId > 0 && !BuildingCompletionPolicy.IsConfirmedCompleted(
                        data, shop.RequiredBuildingId.ToString(CultureInfo.InvariantCulture), utcNowProvider()))
                {
                    return Result(ShopTradeResultCode.ShopLocked, shopId, itemId, quantity);
                }

                ItemDefinition item;
                int unitPrice;
                int itemDelta;
                int currencyDelta;
                ShopTradeResultCode validation = isPurchase
                    ? ValidatePurchase(shopId, itemId, quantity, out item, out unitPrice, out itemDelta, out currencyDelta)
                    : ValidateSale(shop, itemId, quantity, out item, out unitPrice, out itemDelta, out currencyDelta);
                if (validation != ShopTradeResultCode.Purchased && validation != ShopTradeResultCode.Sold)
                {
                    return Result(validation, shopId, itemId, quantity);
                }

                int totalPrice = unitPrice * quantity;
                SaveMetadataSnapshot metadata = SaveMetadataSnapshot.Capture(data);
                InventoryTradeMutationResult mutation = inventory.TryApplyTradeWithoutSave(
                    item, itemDelta, currencyDelta, out InventoryTradeMutationReceipt receipt);
                if (!mutation.Success)
                {
                    return FromMutation(MapMutationCode(mutation.Code), shopId, itemId, quantity, unitPrice, totalPrice, mutation);
                }

                try
                {
                    if (!saveAction())
                    {
                        inventory.RollbackTradeWithoutSave(receipt);
                        SaveData.RestoreMetadata(data, metadata);
                        return Result(ShopTradeResultCode.SaveFailed, shopId, itemId, quantity, unitPrice, totalPrice,
                            mutation.ItemCountBefore, mutation.ItemCountBefore,
                            mutation.CurrencyBefore, mutation.CurrencyBefore);
                    }
                }
                catch
                {
                    inventory.RollbackTradeWithoutSave(receipt);
                    SaveData.RestoreMetadata(data, metadata);
                    throw;
                }

                inventory.NotifyChangedAfterExternalSave();
                return FromMutation(isPurchase ? ShopTradeResultCode.Purchased : ShopTradeResultCode.Sold,
                    shopId, itemId, quantity, unitPrice, totalPrice, mutation);
            }
            finally
            {
                inProgress = false;
            }
        }

        private ShopTradeResultCode ValidatePurchase(string shopId, string itemId, int quantity,
            out ItemDefinition item, out int unitPrice, out int itemDelta, out int currencyDelta)
        {
            item = null;
            unitPrice = 0;
            itemDelta = 0;
            currencyDelta = 0;
            if (shopProductCatalog == null) return ShopTradeResultCode.InvalidRequest;

            ShopProductDefinition product = shopProductCatalog.Find(shopId, itemId);
            if (product == null) return ShopTradeResultCode.UnknownProduct;

            item = itemCatalog.Find(itemId);
            if (item == null) return ShopTradeResultCode.UnknownItem;
            if (product.BuyPrice <= 0) return ShopTradeResultCode.InvalidPrice;
            if (!string.Equals(product.BuyCurrencyId, SupportedCurrencyId, StringComparison.Ordinal))
            {
                return ShopTradeResultCode.UnsupportedCurrency;
            }

            long total = (long)product.BuyPrice * quantity;
            if (total > int.MaxValue) return ShopTradeResultCode.TotalPriceOverflow;

            unitPrice = product.BuyPrice;
            itemDelta = quantity;
            currencyDelta = -(int)total;
            return ShopTradeResultCode.Purchased;
        }

        private ShopTradeResultCode ValidateSale(ShopDefinition shop, string itemId, int quantity,
            out ItemDefinition item, out int unitPrice, out int itemDelta, out int currencyDelta)
        {
            item = null;
            unitPrice = 0;
            itemDelta = 0;
            currencyDelta = 0;
            if (!shop.AcceptItemSales) return ShopTradeResultCode.ItemSalesDisabled;

            item = itemCatalog.Find(itemId);
            if (item == null) return ShopTradeResultCode.UnknownItem;
            if (!item.Sellable) return ShopTradeResultCode.ItemNotSellable;
            if (!string.Equals(item.SellCurrencyId, SupportedCurrencyId, StringComparison.Ordinal))
            {
                return ShopTradeResultCode.UnsupportedCurrency;
            }
            if (!TryGetSellUnitPrice(item, out unitPrice)) return ShopTradeResultCode.InvalidPrice;

            long total = (long)item.SellPrice * quantity;
            if (total > int.MaxValue) return ShopTradeResultCode.TotalPriceOverflow;

            itemDelta = -quantity;
            currencyDelta = (int)total;
            return ShopTradeResultCode.Sold;
        }

        private static ShopTradeResultCode MapMutationCode(InventoryTradeMutationCode code)
        {
            switch (code)
            {
                case InventoryTradeMutationCode.NoSaveData: return ShopTradeResultCode.NoSaveData;
                case InventoryTradeMutationCode.UnknownItem: return ShopTradeResultCode.UnknownItem;
                case InventoryTradeMutationCode.InsufficientCurrency: return ShopTradeResultCode.InsufficientCurrency;
                case InventoryTradeMutationCode.InsufficientItem: return ShopTradeResultCode.InsufficientItem;
                case InventoryTradeMutationCode.CurrencyOverflow: return ShopTradeResultCode.CurrencyOverflow;
                case InventoryTradeMutationCode.ItemOverflow: return ShopTradeResultCode.ItemOverflow;
                default: return ShopTradeResultCode.InvalidRequest;
            }
        }

        private static ShopTradeResultCode MapBatchMutationCode(InventoryTradeBatchMutationCode code)
        {
            switch (code)
            {
                case InventoryTradeBatchMutationCode.NoSaveData: return ShopTradeResultCode.NoSaveData;
                case InventoryTradeBatchMutationCode.UnknownItem: return ShopTradeResultCode.UnknownItem;
                case InventoryTradeBatchMutationCode.DuplicateItem: return ShopTradeResultCode.DuplicateItemId;
                case InventoryTradeBatchMutationCode.InsufficientCurrency: return ShopTradeResultCode.InsufficientCurrency;
                case InventoryTradeBatchMutationCode.InsufficientItem: return ShopTradeResultCode.InsufficientItem;
                case InventoryTradeBatchMutationCode.CurrencyOverflow: return ShopTradeResultCode.CurrencyOverflow;
                case InventoryTradeBatchMutationCode.ItemOverflow: return ShopTradeResultCode.ItemOverflow;
                default: return ShopTradeResultCode.InvalidRequest;
            }
        }

        private static ShopTradeResult FromMutation(ShopTradeResultCode code, string shopId, string itemId,
            int quantity, int unitPrice, int totalPrice, InventoryTradeMutationResult mutation)
        {
            return Result(code, shopId, itemId, quantity, unitPrice, totalPrice,
                mutation.ItemCountBefore, mutation.ItemCountAfter, mutation.CurrencyBefore, mutation.CurrencyAfter);
        }

        private static ShopTradeResult Result(ShopTradeResultCode code, string shopId, string itemId, int quantity,
            int unitPrice = 0, int totalPrice = 0, int itemCountBefore = 0, int itemCountAfter = 0,
            int currencyBefore = 0, int currencyAfter = 0)
        {
            return new ShopTradeResult(code, shopId, itemId, quantity, unitPrice, totalPrice,
                itemCountBefore, itemCountAfter, currencyBefore, currencyAfter);
        }

        private static ShopSellBatchResult BatchResult(ShopTradeResultCode code, string shopId,
            int currency = 0)
        {
            return new ShopSellBatchResult(code, shopId, Array.Empty<ShopSellBatchItemResult>(), 0, currency, currency);
        }
    }
}
