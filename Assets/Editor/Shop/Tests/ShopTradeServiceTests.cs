using System;
using System.Collections.Generic;
using System.Reflection;
using Building;
using Common;
using Inventory;
using NUnit.Framework;
using Shop;
using UnityEditor;
using UnityEngine;

namespace ShopEditor.Tests
{
    /// <summary>상점 거래는 실제 저장 파일 대신 명시적으로 주입한 SaveData와 저장 대역만 사용한다.</summary>
    public sealed class ShopTradeServiceTests
    {
        private static readonly FieldInfo SaveDataField =
            typeof(SaveSystem).GetField("data", BindingFlags.NonPublic | BindingFlags.Static);

        private readonly List<UnityEngine.Object> generatedObjects = new List<UnityEngine.Object>();
        private GameObject host;
        private InventoryManager inventory;
        private object originalSaveData;
        private SaveData data;
        private DateTime nowUtc;
        private int saveCount;
        private int changedCount;
        private int rewardAppliedCount;

        [SetUp]
        public void SetUp()
        {
            Assert.IsNotNull(SaveDataField);
            originalSaveData = SaveDataField.GetValue(null);
            data = new SaveData { currency = 100, saveRevision = 12, lastSavedAtUtc = "before" };
            SaveDataField.SetValue(null, data);
            nowUtc = new DateTime(2026, 8, 27, 0, 0, 0, DateTimeKind.Utc);
            saveCount = 0;
            changedCount = 0;
            rewardAppliedCount = 0;

            host = new GameObject("ShopTradeServiceTests");
            inventory = host.AddComponent<InventoryManager>();
            InventoryManager.InventoryChanged += CountChanged;
            inventory.RewardApplied += CountRewardApplied;
        }

        [TearDown]
        public void TearDown()
        {
            InventoryManager.InventoryChanged -= CountChanged;
            if (inventory != null) inventory.RewardApplied -= CountRewardApplied;
            if (host != null) UnityEngine.Object.DestroyImmediate(host);
            for (int i = 0; i < generatedObjects.Count; i++)
                if (generatedObjects[i] != null) UnityEngine.Object.DestroyImmediate(generatedObjects[i]);
            SaveDataField.SetValue(null, originalSaveData);
        }

        [Test]
        public void Buy_ConfirmedShop_AppliesBothSidesAndSavesAndNotifiesExactlyOnce()
        {
            ItemDefinition potion = Item("50000", sellable: true, sellPrice: 10);
            ShopTradeService service = Service(potion, Product("general_shop", "50000", "jewel", 10));

            ShopTradeResult result = service.TryBuy("general_shop", "50000", 3);

            Assert.AreEqual(ShopTradeResultCode.Purchased, result.Code);
            Assert.AreEqual(10, result.UnitPrice);
            Assert.AreEqual(30, result.TotalPrice);
            Assert.AreEqual(0, result.ItemCountBefore);
            Assert.AreEqual(3, result.ItemCountAfter);
            Assert.AreEqual(100, result.CurrencyBefore);
            Assert.AreEqual(70, result.CurrencyAfter);
            Assert.AreEqual(70, data.currency);
            Assert.AreEqual(3, inventory.GetItemCount("50000"));
            Assert.AreEqual(1, saveCount);
            Assert.AreEqual(1, changedCount);
            Assert.AreEqual(0, rewardAppliedCount);
        }

        [Test]
        public void Sell_AcceptingShop_SellsItemNotListedAsProduct()
        {
            ItemDefinition herb = Item("50001", sellable: true, sellPrice: 15);
            Hold("50001", 2);
            ShopTradeService service = Service(herb);

            ShopTradeResult result = service.TrySell("general_shop", "50001", 2);

            Assert.AreEqual(ShopTradeResultCode.Sold, result.Code);
            Assert.AreEqual(30, result.TotalPrice);
            Assert.AreEqual(130, data.currency);
            Assert.AreEqual(0, inventory.GetItemCount("50001"));
            Assert.AreEqual(1, saveCount);
            Assert.AreEqual(1, changedCount);
        }

        [Test]
        public void Access_RequiresConfirmedBuildingCompletion_NotJustElapsedTime()
        {
            ItemDefinition potion = Item("50000", true, 10);
            ShopTradeService service = Service(potion, Product("general_shop", "50000", "jewel", 10), confirmed: false);

            Assert.AreEqual(ShopTradeResultCode.ShopLocked, service.TryBuy("general_shop", "50000", 1).Code);
            AssertNoSideEffects();

            data.buildingConstructions[0].completionNotified = true;
            Assert.AreEqual(ShopTradeResultCode.Purchased, service.TryBuy("general_shop", "50000", 1).Code);
            Assert.AreEqual(1, saveCount);
        }

        [Test]
        public void Purchase_RejectsInvalidRequestsAndUnusableDefinitionsWithoutSideEffects()
        {
            ItemDefinition potion = Item("50000", true, 10);
            ShopTradeService noProduct = Service(potion);
            Assert.AreEqual(ShopTradeResultCode.InvalidRequest, noProduct.TryBuy("", "50000", 1).Code);
            Assert.AreEqual(ShopTradeResultCode.InvalidRequest, noProduct.TryBuy("general_shop", "50000", 0).Code);
            Assert.AreEqual(ShopTradeResultCode.UnknownShop, noProduct.TryBuy("missing", "50000", 1).Code);
            Assert.AreEqual(ShopTradeResultCode.UnknownProduct, noProduct.TryBuy("general_shop", "50000", 1).Code);

            ShopTradeService missingItem = Service(null, Product("general_shop", "50000", "jewel", 10));
            Assert.AreEqual(ShopTradeResultCode.UnknownItem, missingItem.TryBuy("general_shop", "50000", 1).Code);
            ShopTradeService nonJewel = Service(potion, Product("general_shop", "50000", "gold", 10));
            Assert.AreEqual(ShopTradeResultCode.UnsupportedCurrency, nonJewel.TryBuy("general_shop", "50000", 1).Code);
            ShopTradeService badPrice = Service(potion, Product("general_shop", "50000", "jewel", 0));
            Assert.AreEqual(ShopTradeResultCode.InvalidPrice, badPrice.TryBuy("general_shop", "50000", 1).Code);
            AssertNoSideEffects();
        }

        [Test]
        public void Purchase_RejectsInactiveShopAndProductAndMissingSaveData()
        {
            ItemDefinition potion = Item("50000", true, 10);
            ShopTradeService inactiveShop = Service(potion, Product("general_shop", "50000", "jewel", 10),
                shopEnabled: false);
            Assert.AreEqual(ShopTradeResultCode.UnknownShop, inactiveShop.TryBuy("general_shop", "50000", 1).Code);

            ShopTradeService inactiveProduct = Service(potion, Product("general_shop", "50000", "jewel", 10, enabled: false));
            Assert.AreEqual(ShopTradeResultCode.UnknownProduct, inactiveProduct.TryBuy("general_shop", "50000", 1).Code);

            ShopCatalog shops = Shops(Shop("general_shop", 3, true));
            ItemCatalog items = Catalog(potion);
            ShopTradeService noData = new ShopTradeService(() => null, () => { saveCount++; return true; }, () => nowUtc,
                shops, Products(Product("general_shop", "50000", "jewel", 10)), items, inventory);
            Assert.AreEqual(ShopTradeResultCode.NoSaveData, noData.TryBuy("general_shop", "50000", 1).Code);
            AssertNoSideEffects();
        }

        [Test]
        public void Purchase_MapsInsufficientCurrencyItemOverflowAndTotalOverflow()
        {
            ItemDefinition potion = Item("50000", true, 10);
            ShopTradeService service = Service(potion, Product("general_shop", "50000", "jewel", 10));
            data.currency = 9;
            Assert.AreEqual(ShopTradeResultCode.InsufficientCurrency, service.TryBuy("general_shop", "50000", 1).Code);

            data.currency = 100;
            Hold("50000", int.MaxValue);
            Assert.AreEqual(ShopTradeResultCode.ItemOverflow, service.TryBuy("general_shop", "50000", 1).Code);

            ShopTradeService totalOverflow = Service(potion, Product("general_shop", "50000", "jewel", int.MaxValue));
            Assert.AreEqual(ShopTradeResultCode.TotalPriceOverflow, totalOverflow.TryBuy("general_shop", "50000", 2).Code);
            AssertNoSideEffects();
        }

        [Test]
        public void Sell_EnforcesSalesSwitchSellableCurrencyPriceQuantityAndCurrencyOverflow()
        {
            ItemDefinition sellable = Item("50000", true, 10);
            Hold("50000", 1);
            ShopTradeService disabledShop = Service(sellable, acceptSales: false);
            Assert.AreEqual(ShopTradeResultCode.ItemSalesDisabled, disabledShop.TrySell("general_shop", "50000", 1).Code);

            ItemDefinition lockedItem = Item("50004", false, 30);
            Hold("50004", 1);
            ShopTradeService lockedSale = Service(lockedItem);
            Assert.AreEqual(ShopTradeResultCode.ItemNotSellable, lockedSale.TrySell("general_shop", "50004", 1).Code,
                "가격 데이터가 있어도 Sellable=false가 최종 판매 차단 스위치다.");

            ShopTradeService insufficient = Service(sellable);
            Assert.AreEqual(ShopTradeResultCode.InsufficientItem, insufficient.TrySell("general_shop", "50000", 2).Code);
            data.currency = int.MaxValue;
            Assert.AreEqual(ShopTradeResultCode.CurrencyOverflow, insufficient.TrySell("general_shop", "50000", 1).Code);

            ItemDefinition otherCurrency = Item("50002", true, 10, "gold");
            Hold("50002", 1);
            Assert.AreEqual(ShopTradeResultCode.UnsupportedCurrency, Service(otherCurrency).TrySell("general_shop", "50002", 1).Code);
            ItemDefinition zeroPrice = Item("50003", true, 0);
            Hold("50003", 1);
            Assert.AreEqual(ShopTradeResultCode.InvalidPrice, Service(zeroPrice).TrySell("general_shop", "50003", 1).Code);
            ItemDefinition hugePrice = Item("50005", true, int.MaxValue);
            Hold("50005", 2);
            Assert.AreEqual(ShopTradeResultCode.TotalPriceOverflow, Service(hugePrice).TrySell("general_shop", "50005", 2).Code);
            Assert.AreEqual(ShopTradeResultCode.InvalidRequest, insufficient.TrySell("general_shop", "50000", -1).Code);
            AssertNoSideEffects();
        }

        [Test]
        public void SaveFailure_RestoresCurrencyItemsOrderNullSlotsAndMetadataWithoutNotification()
        {
            ItemDefinition potion = Item("50000", true, 10);
            InventoryItemState before = new InventoryItemState { itemId = "before", count = 4 };
            InventoryItemState after = new InventoryItemState { itemId = "after", count = 9 };
            data.items.Add(before);
            data.items.Add(null);
            data.items.Add(after);
            data.currency = 100;
            data.saveVersion = SaveData.CurrentSaveVersion;
            data.saveRevision = 17;
            data.lastSavedAtUtc = "old";
            ShopTradeService service = Service(potion, Product("general_shop", "50000", "jewel", 10),
                save: () => { saveCount++; data.saveVersion = 99; data.saveRevision = 999; data.lastSavedAtUtc = "new"; return false; });

            ShopTradeResult result = service.TryBuy("general_shop", "50000", 1);

            Assert.AreEqual(ShopTradeResultCode.SaveFailed, result.Code);
            Assert.AreEqual(100, data.currency);
            Assert.AreEqual(3, data.items.Count);
            Assert.AreSame(before, data.items[0]);
            Assert.IsNull(data.items[1]);
            Assert.AreSame(after, data.items[2]);
            Assert.AreEqual(17, data.saveRevision);
            Assert.AreEqual("old", data.lastSavedAtUtc);
            Assert.AreEqual(SaveData.CurrentSaveVersion, data.saveVersion);
            Assert.AreEqual(1, saveCount);
            Assert.AreEqual(0, changedCount);
            Assert.AreEqual(0, rewardAppliedCount);
        }

        [Test]
        public void SaveException_RestoresWholeTransactionThenRethrows()
        {
            ItemDefinition potion = Item("50000", true, 10);
            data.currency = 100;
            ShopTradeService service = Service(potion, Product("general_shop", "50000", "jewel", 10),
                save: () => { saveCount++; data.saveRevision = 300; throw new InvalidOperationException("save"); });

            Assert.Throws<InvalidOperationException>(() => service.TryBuy("general_shop", "50000", 1));
            Assert.AreEqual(100, data.currency);
            Assert.AreEqual(0, data.items.Count);
            Assert.AreEqual(12, data.saveRevision);
            Assert.AreEqual(1, saveCount);
            Assert.AreEqual(0, changedCount);
            Assert.AreEqual(0, rewardAppliedCount);
        }

        [Test]
        public void ReentrantRequest_IsRejectedWithoutExtraMutationSaveOrNotification()
        {
            ItemDefinition potion = Item("50000", true, 10);
            ShopTradeService service = null;
            ShopTradeResult nested = default;
            service = Service(potion, Product("general_shop", "50000", "jewel", 10), save: () =>
            {
                saveCount++;
                nested = service.TryBuy("general_shop", "50000", 1);
                return true;
            });

            Assert.AreEqual(ShopTradeResultCode.Purchased, service.TryBuy("general_shop", "50000", 1).Code);
            Assert.AreEqual(ShopTradeResultCode.Reentrant, nested.Code);
            Assert.AreEqual(90, data.currency);
            Assert.AreEqual(1, inventory.GetItemCount("50000"));
            Assert.AreEqual(1, saveCount);
            Assert.AreEqual(1, changedCount);
        }

        private ShopTradeService Service(ItemDefinition item, ShopProductDefinition product = null, bool confirmed = true,
            bool acceptSales = true, Func<bool> save = null, bool shopEnabled = true)
        {
            RegisterInventory(item);
            ItemCatalog items = Catalog(item);
            ShopCatalog shops = Shops(Shop("general_shop", 3, acceptSales, shopEnabled));
            ShopProductCatalog products = Products(product);
            data.buildingConstructions.Clear();
            data.buildingConstructions.Add(new BuildingConstructionSaveState
            {
                buildingId = "3",
                startedAtUtc = SaveData.FormatTimestamp(nowUtc.AddMinutes(-1)),
                completeAtUtc = SaveData.FormatTimestamp(nowUtc.AddSeconds(-1)),
                completionNotified = confirmed,
            });
            return new ShopTradeService(() => data, save ?? (() => { saveCount++; return true; }), () => nowUtc,
                shops, products, items, inventory);
        }

        private void RegisterInventory(ItemDefinition item)
        {
            var serialized = new SerializedObject(inventory);
            SerializedProperty list = serialized.FindProperty("itemCatalog");
            list.arraySize = item == null ? 0 : 1;
            if (item != null) list.GetArrayElementAtIndex(0).objectReferenceValue = item;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Invoke(inventory, "BuildDefinitionLookup");
        }

        private ItemDefinition Item(string id, bool sellable, int sellPrice, string sellCurrency = "jewel")
        {
            ItemDefinition item = ScriptableObject.CreateInstance<ItemDefinition>();
            item.hideFlags = HideFlags.HideAndDontSave;
            generatedObjects.Add(item);
            var serialized = new SerializedObject(item);
            serialized.FindProperty("itemId").stringValue = id;
            serialized.FindProperty("sellable").boolValue = sellable;
            serialized.FindProperty("sellCurrencyId").stringValue = sellCurrency;
            serialized.FindProperty("sellPrice").intValue = sellPrice;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return item;
        }

        private ShopDefinition Shop(string id, int buildingId, bool acceptSales, bool enabled = true)
        {
            ShopDefinition shop = ScriptableObject.CreateInstance<ShopDefinition>();
            shop.hideFlags = HideFlags.HideAndDontSave;
            generatedObjects.Add(shop);
            var serialized = new SerializedObject(shop);
            serialized.FindProperty("shopId").stringValue = id;
            serialized.FindProperty("requiredBuildingId").intValue = buildingId;
            serialized.FindProperty("acceptItemSales").boolValue = acceptSales;
            serialized.FindProperty("enabled").boolValue = enabled;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return shop;
        }

        private ShopProductDefinition Product(string shopId, string itemId, string currency, int price, bool enabled = true)
        {
            ShopProductDefinition product = ScriptableObject.CreateInstance<ShopProductDefinition>();
            product.hideFlags = HideFlags.HideAndDontSave;
            generatedObjects.Add(product);
            var serialized = new SerializedObject(product);
            serialized.FindProperty("shopId").stringValue = shopId;
            serialized.FindProperty("itemId").stringValue = itemId;
            serialized.FindProperty("buyCurrencyId").stringValue = currency;
            serialized.FindProperty("buyPrice").intValue = price;
            serialized.FindProperty("enabled").boolValue = enabled;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return product;
        }

        private ItemCatalog Catalog(ItemDefinition item)
        {
            ItemCatalog catalog = NewCatalog<ItemCatalog>("items");
            SetCatalogItems(catalog, "items", item);
            return catalog;
        }

        private ShopCatalog Shops(ShopDefinition shop)
        {
            ShopCatalog catalog = NewCatalog<ShopCatalog>("shops");
            SetCatalogItems(catalog, "shops", shop);
            return catalog;
        }

        private ShopProductCatalog Products(ShopProductDefinition product)
        {
            ShopProductCatalog catalog = NewCatalog<ShopProductCatalog>("products");
            SetCatalogItems(catalog, "products", product);
            return catalog;
        }

        private T NewCatalog<T>(string _) where T : ScriptableObject
        {
            T value = ScriptableObject.CreateInstance<T>();
            value.hideFlags = HideFlags.HideAndDontSave;
            generatedObjects.Add(value);
            return value;
        }

        private static void SetCatalogItems(UnityEngine.Object catalog, string field, UnityEngine.Object item)
        {
            var serialized = new SerializedObject(catalog);
            SerializedProperty list = serialized.FindProperty(field);
            list.arraySize = item == null ? 0 : 1;
            if (item != null) list.GetArrayElementAtIndex(0).objectReferenceValue = item;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            catalog.GetType().GetMethod("MarkDirty").Invoke(catalog, null);
        }

        private void Hold(string itemId, int count)
        {
            data.items.Add(new InventoryItemState { itemId = itemId, count = count });
        }

        private void AssertNoSideEffects()
        {
            Assert.AreEqual(0, saveCount);
            Assert.AreEqual(0, changedCount);
            Assert.AreEqual(0, rewardAppliedCount);
        }

        private static void Invoke(object target, string methodName)
        {
            MethodInfo method = target.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(method, methodName);
            method.Invoke(target, null);
        }

        private void CountChanged() => changedCount++;
        private void CountRewardApplied(InventoryRewardApplyResult _) => rewardAppliedCount++;
    }
}
