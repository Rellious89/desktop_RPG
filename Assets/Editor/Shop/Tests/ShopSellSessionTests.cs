using System;
using System.Collections.Generic;
using System.Reflection;
using Common;
using Inventory;
using NUnit.Framework;
using Shop;
using UnityEditor;
using UnityEngine;

namespace ShopEditor.Tests
{
    /// <summary>판매 화면의 선택은 실제 거래 전까지 저장을 건드리지 않는다는 계약을 고정한다.</summary>
    public sealed class ShopSellSessionTests
    {
        private static readonly FieldInfo SaveDataField =
            typeof(SaveSystem).GetField("data", BindingFlags.NonPublic | BindingFlags.Static);

        private readonly List<UnityEngine.Object> generatedObjects = new List<UnityEngine.Object>();
        private GameObject host;
        private InventoryManager inventory;
        private object originalSaveData;
        private SaveData data;

        [SetUp]
        public void SetUp()
        {
            Assert.IsNotNull(SaveDataField);
            originalSaveData = SaveDataField.GetValue(null);
            data = new SaveData { currency = 70 };
            SaveDataField.SetValue(null, data);
            host = new GameObject("ShopSellSessionTests");
            inventory = host.AddComponent<InventoryManager>();
        }

        [TearDown]
        public void TearDown()
        {
            if (host != null) UnityEngine.Object.DestroyImmediate(host);
            for (int i = 0; i < generatedObjects.Count; i++)
                if (generatedObjects[i] != null) UnityEngine.Object.DestroyImmediate(generatedObjects[i]);
            SaveDataField.SetValue(null, originalSaveData);
        }

        [Test]
        public void Session_OnlyRegistersHeldCanonicalSellableItems_WithoutMutatingInventory()
        {
            ItemDefinition herb = Item("50001", true, 15);
            ItemDefinition locked = Item("50002", false, 20);
            ItemDefinition gold = Item("50003", true, 20, "gold");
            ItemDefinition free = Item("50004", true, 0);
            ItemDefinition missing = Item("50005", true, 10);
            RegisterInventory(herb, locked, gold, free, missing);
            Hold("50001", 2);
            Hold("50002", 1);
            Hold("50003", 1);
            Hold("50004", 1);
            int currencyBefore = data.currency;

            var session = new ShopSellSession(Catalog(herb, locked, gold, free), inventory);

            Assert.IsTrue(session.TryAdd(herb));
            Assert.IsFalse(session.TryAdd(herb), "같은 종류는 세션에 한 번만 들어간다.");
            Assert.IsFalse(session.TryAdd(locked));
            Assert.IsFalse(session.TryAdd(gold));
            Assert.IsFalse(session.TryAdd(free));
            Assert.IsFalse(session.TryAdd(missing), "카탈로그 정의가 없는 저장 항목은 등록하지 않는다.");
            Assert.AreEqual(1, session.Entries.Count);
            Assert.AreEqual(2, inventory.GetItemCount("50001"));
            Assert.AreEqual(currencyBefore, data.currency);
        }

        [Test]
        public void Session_RevalidatesExternalInventoryChange_BeforeSnapshot()
        {
            ItemDefinition herb = Item("50001", true, 15);
            RegisterInventory(herb);
            Hold("50001", 1);
            var session = new ShopSellSession(Catalog(herb), inventory);
            Assert.IsTrue(session.TryAdd(herb));

            data.items.Clear();

            Assert.IsTrue(session.Revalidate());
            Assert.AreEqual(0, session.Entries.Count);
            Assert.IsFalse(session.TryCreateSnapshot(out ShopSellLine[] snapshot, out int total));
            Assert.AreEqual(0, snapshot.Length);
            Assert.AreEqual(0, total);
        }

        [Test]
        public void Session_SnapshotUsesOnePerTypeAndLatestAuthoritativePrice()
        {
            ItemDefinition herb = Item("50001", true, 15);
            ItemDefinition ore = Item("50002", true, 40);
            RegisterInventory(herb, ore);
            Hold("50001", 5);
            Hold("50002", 3);
            var session = new ShopSellSession(Catalog(herb, ore), inventory);
            Assert.IsTrue(session.TryAdd(herb));
            Assert.IsTrue(session.TryAdd(ore));

            var serialized = new SerializedObject(ore);
            serialized.FindProperty("sellPrice").intValue = 55;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            Assert.IsTrue(session.TryCreateSnapshot(out ShopSellLine[] snapshot, out int total));
            Assert.AreEqual(2, snapshot.Length);
            Assert.AreEqual("50001", snapshot[0].ItemId);
            Assert.AreEqual(1, snapshot[0].Quantity);
            Assert.AreEqual("50002", snapshot[1].ItemId);
            Assert.AreEqual(1, snapshot[1].Quantity);
            Assert.AreEqual(70, total);
            Assert.AreEqual(5, inventory.GetItemCount("50001"));
            Assert.AreEqual(3, inventory.GetItemCount("50002"));
        }

        private ItemDefinition Item(string id, bool sellable, int sellPrice, string currency = "jewel")
        {
            ItemDefinition item = ScriptableObject.CreateInstance<ItemDefinition>();
            item.hideFlags = HideFlags.HideAndDontSave;
            generatedObjects.Add(item);
            var serialized = new SerializedObject(item);
            serialized.FindProperty("itemId").stringValue = id;
            serialized.FindProperty("sellable").boolValue = sellable;
            serialized.FindProperty("sellCurrencyId").stringValue = currency;
            serialized.FindProperty("sellPrice").intValue = sellPrice;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return item;
        }

        private ItemCatalog Catalog(params ItemDefinition[] items)
        {
            ItemCatalog catalog = ScriptableObject.CreateInstance<ItemCatalog>();
            catalog.hideFlags = HideFlags.HideAndDontSave;
            generatedObjects.Add(catalog);
            var serialized = new SerializedObject(catalog);
            SerializedProperty list = serialized.FindProperty("items");
            list.arraySize = items.Length;
            for (int i = 0; i < items.Length; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();
            catalog.MarkDirty();
            return catalog;
        }

        private void RegisterInventory(params ItemDefinition[] items)
        {
            var serialized = new SerializedObject(inventory);
            SerializedProperty list = serialized.FindProperty("itemCatalog");
            list.arraySize = items.Length;
            for (int i = 0; i < items.Length; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();
            typeof(InventoryManager).GetMethod("BuildDefinitionLookup", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(inventory, null);
        }

        private void Hold(string itemId, int count)
        {
            data.items.Add(new InventoryItemState { itemId = itemId, count = count });
        }
    }
}
