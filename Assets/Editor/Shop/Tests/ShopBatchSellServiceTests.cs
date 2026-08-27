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
    /// <summary>일괄 판매는 저장 파일 대신 주입된 SaveData만 써서 원자성을 검증한다.</summary>
    public sealed class ShopBatchSellServiceTests
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
            host = new GameObject("ShopBatchSellServiceTests");
            inventory = host.AddComponent<InventoryManager>();
            InventoryManager.InventoryChanged += CountChanged;
        }

        [TearDown]
        public void TearDown()
        {
            InventoryManager.InventoryChanged -= CountChanged;
            if (host != null) UnityEngine.Object.DestroyImmediate(host);
            for (int i = 0; i < generatedObjects.Count; i++)
                if (generatedObjects[i] != null) UnityEngine.Object.DestroyImmediate(generatedObjects[i]);
            SaveDataField.SetValue(null, originalSaveData);
        }

        [Test]
        public void SellBatch_MultipleItems_AppliesAllThenSavesAndNotifiesExactlyOnce()
        {
            ItemDefinition herb = Item("50001", true, 15);
            ItemDefinition ore = Item("50002", true, 40);
            Hold("50001", 3);
            Hold("50002", 2);
            ShopTradeService service = Service(new[] { herb, ore });

            ShopSellBatchResult result = service.TrySellBatch("general_shop", new[]
            {
                new ShopSellLine("50001", 2), new ShopSellLine("50002", 1),
            });

            Assert.AreEqual(ShopTradeResultCode.Sold, result.Code);
            Assert.AreEqual("general_shop", result.ShopId);
            Assert.AreEqual(2, result.Items.Count);
            Assert.AreEqual(30, result.Items[0].TotalPrice);
            Assert.AreEqual(40, result.Items[1].TotalPrice);
            Assert.AreEqual(70, result.TotalPrice);
            Assert.AreEqual(100, result.CurrencyBefore);
            Assert.AreEqual(170, result.CurrencyAfter);
            Assert.AreEqual(1, inventory.GetItemCount("50001"));
            Assert.AreEqual(1, inventory.GetItemCount("50002"));
            Assert.AreEqual(1, saveCount);
            Assert.AreEqual(1, changedCount);
        }

        [Test]
        public void SellBatch_RejectsDuplicateNonSellableInsufficientUnknownAndBadQuantity_WithoutSideEffects()
        {
            ItemDefinition herb = Item("50001", true, 15);
            ItemDefinition locked = Item("50004", false, 30);
            ItemDefinition unlisted = Item("50005", true, 10);
            Hold("50001", 1);
            Hold("50004", 1);
            ShopTradeService service = Service(new[] { herb, locked, unlisted }, catalogItems: new[] { herb, locked });

            Assert.AreEqual(ShopTradeResultCode.DuplicateItemId, service.TrySellBatch("general_shop", new[]
            {
                new ShopSellLine("50001", 1), new ShopSellLine(" 50001 ", 1),
            }).Code);
            Assert.AreEqual(ShopTradeResultCode.ItemNotSellable, service.TrySellBatch("general_shop", new[]
            {
                new ShopSellLine("50001", 1), new ShopSellLine("50004", 1),
            }).Code);
            Assert.AreEqual(ShopTradeResultCode.InsufficientItem, service.TrySellBatch("general_shop", new[]
            {
                new ShopSellLine("50001", 2),
            }).Code);
            Assert.AreEqual(ShopTradeResultCode.UnknownItem, service.TrySellBatch("general_shop", new[]
            {
                new ShopSellLine("50005", 1),
            }).Code);
            Assert.AreEqual(ShopTradeResultCode.InvalidRequest, service.TrySellBatch("general_shop", new[]
            {
                new ShopSellLine("50001", 0),
            }).Code);
            AssertUnchanged(100, 1, 1);
        }

        [Test]
        public void SellBatch_RequiresConfirmedShopAcceptingSalesAndSupportedCurrency()
        {
            ItemDefinition herb = Item("50001", true, 15);
            Hold("50001", 1);
            Assert.AreEqual(ShopTradeResultCode.ShopLocked,
                Service(new[] { herb }, confirmed: false).TrySellBatch("general_shop", Lines("50001", 1)).Code);
            Assert.AreEqual(ShopTradeResultCode.ItemSalesDisabled,
                Service(new[] { herb }, acceptSales: false).TrySellBatch("general_shop", Lines("50001", 1)).Code);

            ItemDefinition goldItem = Item("50002", true, 15, "gold");
            Hold("50002", 1);
            Assert.AreEqual(ShopTradeResultCode.UnsupportedCurrency,
                Service(new[] { herb, goldItem }).TrySellBatch("general_shop", new[]
                {
                    new ShopSellLine("50001", 1), new ShopSellLine("50002", 1),
                }).Code);
            Assert.AreEqual(100, data.currency);
            Assert.AreEqual(1, inventory.GetItemCount("50001"));
            Assert.AreEqual(1, inventory.GetItemCount("50002"));
            Assert.AreEqual(0, saveCount);
            Assert.AreEqual(0, changedCount);
        }

        [Test]
        public void SellBatch_RejectsLineTotalTotalAndCurrencyOverflow_WithoutSideEffects()
        {
            ItemDefinition huge = Item("50001", true, int.MaxValue);
            Hold("50001", 2);
            Assert.AreEqual(ShopTradeResultCode.TotalPriceOverflow,
                Service(new[] { huge }).TrySellBatch("general_shop", Lines("50001", 2)).Code);

            ItemDefinition first = Item("50002", true, int.MaxValue - 1);
            ItemDefinition second = Item("50003", true, 2);
            Hold("50002", 1);
            Hold("50003", 1);
            Assert.AreEqual(ShopTradeResultCode.TotalPriceOverflow,
                Service(new[] { first, second }).TrySellBatch("general_shop", new[]
                {
                    new ShopSellLine("50002", 1), new ShopSellLine("50003", 1),
                }).Code);

            ItemDefinition herb = Item("50004", true, 10);
            Hold("50004", 1);
            data.currency = int.MaxValue;
            Assert.AreEqual(ShopTradeResultCode.CurrencyOverflow,
                Service(new[] { herb }).TrySellBatch("general_shop", Lines("50004", 1)).Code);
            Assert.AreEqual(int.MaxValue, data.currency);
            Assert.AreEqual(1, inventory.GetItemCount("50004"));
            Assert.AreEqual(0, saveCount);
            Assert.AreEqual(0, changedCount);
        }

        [Test]
        public void SellBatch_SaveFalse_RestoresItemsOrderNullSlotsCurrencyAndMetadataWithoutNotification()
        {
            ItemDefinition herb = Item("50001", true, 15);
            ItemDefinition ore = Item("50002", true, 40);
            InventoryItemState before = new InventoryItemState { itemId = "50001", count = 2 };
            InventoryItemState after = new InventoryItemState { itemId = "50002", count = 1 };
            data.items.Add(before);
            data.items.Add(null);
            data.items.Add(after);
            data.saveVersion = SaveData.CurrentSaveVersion;
            data.saveRevision = 17;
            data.lastSavedAtUtc = "old";
            ShopTradeService service = Service(new[] { herb, ore }, save: () =>
            {
                saveCount++;
                data.saveVersion = 99;
                data.saveRevision = 999;
                data.lastSavedAtUtc = "new";
                return false;
            });

            ShopSellBatchResult result = service.TrySellBatch("general_shop", new[]
            {
                new ShopSellLine("50001", 2), new ShopSellLine("50002", 1),
            });

            Assert.AreEqual(ShopTradeResultCode.SaveFailed, result.Code);
            Assert.AreEqual(100, data.currency);
            Assert.AreEqual(3, data.items.Count);
            Assert.AreSame(before, data.items[0]);
            Assert.IsNull(data.items[1]);
            Assert.AreSame(after, data.items[2]);
            Assert.AreEqual(2, before.count);
            Assert.AreEqual(1, after.count);
            Assert.AreEqual(SaveData.CurrentSaveVersion, data.saveVersion);
            Assert.AreEqual(17, data.saveRevision);
            Assert.AreEqual("old", data.lastSavedAtUtc);
            Assert.AreEqual(1, saveCount);
            Assert.AreEqual(0, changedCount);
        }

        [Test]
        public void SellBatch_SaveException_RestoresThenRethrows()
        {
            ItemDefinition herb = Item("50001", true, 15);
            Hold("50001", 1);
            ShopTradeService service = Service(new[] { herb }, save: () =>
            {
                saveCount++;
                data.saveRevision = 999;
                throw new InvalidOperationException("save");
            });

            Assert.Throws<InvalidOperationException>(() => service.TrySellBatch("general_shop", Lines("50001", 1)));
            Assert.AreEqual(100, data.currency);
            Assert.AreEqual(1, inventory.GetItemCount("50001"));
            Assert.AreEqual(12, data.saveRevision);
            Assert.AreEqual(1, saveCount);
            Assert.AreEqual(0, changedCount);
        }

        [Test]
        public void SellBatch_ReentrantRequest_IsRejectedWithoutExtraSideEffects()
        {
            ItemDefinition herb = Item("50001", true, 15);
            Hold("50001", 2);
            ShopTradeService service = null;
            ShopSellBatchResult nested = null;
            service = Service(new[] { herb }, save: () =>
            {
                saveCount++;
                nested = service.TrySellBatch("general_shop", Lines("50001", 1));
                return true;
            });

            Assert.AreEqual(ShopTradeResultCode.Sold, service.TrySellBatch("general_shop", Lines("50001", 1)).Code);
            Assert.AreEqual(ShopTradeResultCode.Reentrant, nested.Code);
            Assert.AreEqual(1, inventory.GetItemCount("50001"));
            Assert.AreEqual(115, data.currency);
            Assert.AreEqual(1, saveCount);
            Assert.AreEqual(1, changedCount);
        }

        [Test]
        public void InventoryBatchMutation_RejectsDuplicateWithoutMutation_AndRollbackRestoresSnapshot()
        {
            ItemDefinition herb = Item("50001", true, 15);
            ItemDefinition ore = Item("50002", true, 40);
            RegisterInventory(herb, ore);
            InventoryItemState herbState = new InventoryItemState { itemId = "50001", count = 2 };
            InventoryItemState oreState = new InventoryItemState { itemId = "50002", count = 1 };
            data.items.Add(herbState);
            data.items.Add(null);
            data.items.Add(oreState);

            InventoryTradeBatchMutationResult rejected = inventory.TryApplyTradeBatchWithoutSave(new[]
            {
                new InventoryTradeBatchLine(herb, -1), new InventoryTradeBatchLine(herb, -1),
            }, 15, out InventoryTradeBatchMutationReceipt empty);
            Assert.AreEqual(InventoryTradeBatchMutationCode.DuplicateItem, rejected.Code);
            Assert.IsFalse(empty.Changed);
            Assert.AreEqual(100, data.currency);
            Assert.AreEqual(2, inventory.GetItemCount("50001"));
            Assert.AreEqual(1, inventory.GetItemCount("50002"));
            Assert.AreEqual(0, saveCount);
            Assert.AreEqual(0, changedCount);

            InventoryTradeBatchMutationResult applied = inventory.TryApplyTradeBatchWithoutSave(new[]
            {
                new InventoryTradeBatchLine(herb, -2), new InventoryTradeBatchLine(ore, -1),
            }, 55, out InventoryTradeBatchMutationReceipt receipt);
            Assert.IsTrue(applied.Success);
            inventory.RollbackTradeBatchWithoutSave(receipt);
            Assert.AreEqual(100, data.currency);
            Assert.AreEqual(3, data.items.Count);
            Assert.AreSame(herbState, data.items[0]);
            Assert.IsNull(data.items[1]);
            Assert.AreSame(oreState, data.items[2]);
            Assert.AreEqual(2, herbState.count);
            Assert.AreEqual(1, oreState.count);
        }

        private ShopTradeService Service(ItemDefinition[] inventoryItems, bool confirmed = true, bool acceptSales = true,
            Func<bool> save = null, ItemDefinition[] catalogItems = null)
        {
            RegisterInventory(inventoryItems);
            ItemCatalog items = Catalog(catalogItems ?? inventoryItems);
            ShopCatalog shops = Shops(Shop("general_shop", 3, acceptSales));
            data.buildingConstructions.Clear();
            data.buildingConstructions.Add(new BuildingConstructionSaveState
            {
                buildingId = "3",
                startedAtUtc = SaveData.FormatTimestamp(nowUtc.AddMinutes(-1)),
                completeAtUtc = SaveData.FormatTimestamp(nowUtc.AddSeconds(-1)),
                completionNotified = confirmed,
            });
            return new ShopTradeService(() => data, save ?? (() => { saveCount++; return true; }), () => nowUtc,
                shops, Products(), items, inventory);
        }

        private void RegisterInventory(params ItemDefinition[] items)
        {
            var serialized = new SerializedObject(inventory);
            SerializedProperty list = serialized.FindProperty("itemCatalog");
            list.arraySize = items == null ? 0 : items.Length;
            for (int i = 0; items != null && i < items.Length; i++)
                list.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
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

        private ShopDefinition Shop(string id, int buildingId, bool acceptSales)
        {
            ShopDefinition shop = ScriptableObject.CreateInstance<ShopDefinition>();
            shop.hideFlags = HideFlags.HideAndDontSave;
            generatedObjects.Add(shop);
            var serialized = new SerializedObject(shop);
            serialized.FindProperty("shopId").stringValue = id;
            serialized.FindProperty("requiredBuildingId").intValue = buildingId;
            serialized.FindProperty("acceptItemSales").boolValue = acceptSales;
            serialized.FindProperty("enabled").boolValue = true;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return shop;
        }

        private T NewCatalog<T>() where T : ScriptableObject
        {
            T catalog = ScriptableObject.CreateInstance<T>();
            catalog.hideFlags = HideFlags.HideAndDontSave;
            generatedObjects.Add(catalog);
            return catalog;
        }

        private ItemCatalog Catalog(params ItemDefinition[] items)
        {
            ItemCatalog catalog = NewCatalog<ItemCatalog>();
            SetCatalogItems(catalog, "items", items);
            return catalog;
        }

        private ShopCatalog Shops(params ShopDefinition[] shops)
        {
            ShopCatalog catalog = NewCatalog<ShopCatalog>();
            SetCatalogItems(catalog, "shops", shops);
            return catalog;
        }

        private ShopProductCatalog Products()
        {
            return NewCatalog<ShopProductCatalog>();
        }

        private static void SetCatalogItems(UnityEngine.Object catalog, string field, UnityEngine.Object[] items)
        {
            var serialized = new SerializedObject(catalog);
            SerializedProperty list = serialized.FindProperty(field);
            list.arraySize = items == null ? 0 : items.Length;
            for (int i = 0; items != null && i < items.Length; i++)
                list.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();
            catalog.GetType().GetMethod("MarkDirty").Invoke(catalog, null);
        }

        private void Hold(string itemId, int count)
        {
            data.items.Add(new InventoryItemState { itemId = itemId, count = count });
        }

        private static ShopSellLine[] Lines(string itemId, int quantity)
        {
            return new[] { new ShopSellLine(itemId, quantity) };
        }

        private void AssertUnchanged(int currency, int herbCount, int lockedCount)
        {
            Assert.AreEqual(currency, data.currency);
            Assert.AreEqual(herbCount, inventory.GetItemCount("50001"));
            Assert.AreEqual(lockedCount, inventory.GetItemCount("50004"));
            Assert.AreEqual(0, saveCount);
            Assert.AreEqual(0, changedCount);
        }

        private static void Invoke(object target, string methodName)
        {
            MethodInfo method = target.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(method, methodName);
            method.Invoke(target, null);
        }

        private void CountChanged() => changedCount++;
    }
}
