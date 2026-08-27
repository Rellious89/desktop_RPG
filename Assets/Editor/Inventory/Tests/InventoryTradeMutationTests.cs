using System;
using System.Collections.Generic;
using System.Reflection;
using Common;
using Inventory;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace InventoryEditor.Tests
{
    /// <summary>12B 거래 메모리 변경 경로는 저장 파일 대신 SaveSystem의 메모리 문서와 saveOverride만 쓴다.</summary>
    public sealed class InventoryTradeMutationTests
    {
        private static readonly FieldInfo SaveDataField =
            typeof(SaveSystem).GetField("data", BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly FieldInfo SaveOverrideField =
            typeof(InventoryManager).GetField("saveOverride", BindingFlags.NonPublic | BindingFlags.Static);

        private GameObject host;
        private InventoryManager inventory;
        private object originalSaveData;
        private int saveCount;
        private int changedCount;
        private int rewardAppliedCount;

        [SetUp]
        public void SetUp()
        {
            Assert.IsNotNull(SaveDataField);
            Assert.IsNotNull(SaveOverrideField);
            originalSaveData = SaveDataField.GetValue(null);
            SaveDataField.SetValue(null, new SaveData());
            saveCount = 0;
            changedCount = 0;
            rewardAppliedCount = 0;
            SaveOverrideField.SetValue(null, new Func<bool>(() => { saveCount++; return true; }));

            host = new GameObject("InventoryTradeMutationTests");
            inventory = host.AddComponent<InventoryManager>();
            Invoke(inventory, "Awake");
            InventoryManager.InventoryChanged += CountChanged;
            inventory.RewardApplied += CountRewardApplied;
        }

        [TearDown]
        public void TearDown()
        {
            InventoryManager.InventoryChanged -= CountChanged;
            if (inventory != null) inventory.RewardApplied -= CountRewardApplied;
            if (host != null) UnityEngine.Object.DestroyImmediate(host);
            SaveOverrideField.SetValue(null, null);
            SaveDataField.SetValue(null, originalSaveData);
        }

        [Test]
        public void Purchase_AppliesCurrencyAndItemWithoutSaveOrNotifications()
        {
            ItemDefinition item = RegisterItem("potion");
            SaveSystem.Data.currency = 100;

            InventoryTradeMutationResult result = inventory.TryApplyTradeWithoutSave(
                item, 2, -30, out InventoryTradeMutationReceipt receipt);

            Assert.AreEqual(InventoryTradeMutationCode.Success, result.Code);
            Assert.AreEqual("potion", result.ItemId);
            Assert.AreEqual(2, result.RequestedItemDelta);
            Assert.AreEqual(-30, result.RequestedCurrencyDelta);
            Assert.AreEqual(100, result.CurrencyBefore);
            Assert.AreEqual(70, result.CurrencyAfter);
            Assert.AreEqual(0, result.ItemCountBefore);
            Assert.AreEqual(2, result.ItemCountAfter);
            Assert.IsTrue(receipt.Changed);
            Assert.AreEqual(70, SaveSystem.Data.currency);
            Assert.AreEqual(2, inventory.GetItemCount("potion"));
            Assert.AreEqual(0, saveCount);
            Assert.AreEqual(0, changedCount);
            Assert.AreEqual(0, rewardAppliedCount);
        }

        [Test]
        public void Sale_ExactDepletionRemovesOnlySoldEntryAndPreservesOtherOrder()
        {
            ItemDefinition item = RegisterItem("potion");
            SaveSystem.Data.items.Add(new InventoryItemState { itemId = "before", count = 4 });
            SaveSystem.Data.items.Add(new InventoryItemState { itemId = "potion", count = 2 });
            SaveSystem.Data.items.Add(new InventoryItemState { itemId = "after", count = 9 });

            InventoryTradeMutationResult result = inventory.TryApplyTradeWithoutSave(
                item, -2, 30, out _);

            Assert.IsTrue(result.Success);
            Assert.AreEqual(30, SaveSystem.Data.currency);
            CollectionAssert.AreEqual(new[] { "before", "after" }, ItemIds());
            CollectionAssert.AreEqual(new[] { 4, 9 }, ItemCounts());
            Assert.AreEqual(0, saveCount);
            Assert.AreEqual(0, changedCount);
        }

        [Test]
        public void InsufficientCurrency_LeavesBothSidesUnchanged()
        {
            ItemDefinition item = RegisterItem("potion");
            SaveSystem.Data.currency = 29;

            InventoryTradeMutationResult result = inventory.TryApplyTradeWithoutSave(item, 1, -30, out var receipt);

            Assert.AreEqual(InventoryTradeMutationCode.InsufficientCurrency, result.Code);
            Assert.IsFalse(receipt.Changed);
            Assert.AreEqual(29, SaveSystem.Data.currency);
            Assert.AreEqual(0, SaveSystem.Data.items.Count);
            AssertNoSideEffects();
        }

        [Test]
        public void InsufficientItem_LeavesBothSidesUnchanged()
        {
            ItemDefinition item = RegisterItem("potion");
            SaveSystem.Data.currency = 10;
            Hold("potion", 1);

            InventoryTradeMutationResult result = inventory.TryApplyTradeWithoutSave(item, -2, 30, out var receipt);

            Assert.AreEqual(InventoryTradeMutationCode.InsufficientItem, result.Code);
            Assert.IsFalse(receipt.Changed);
            Assert.AreEqual(10, SaveSystem.Data.currency);
            Assert.AreEqual(1, SaveSystem.Data.items[0].count);
            AssertNoSideEffects();
        }

        [Test]
        public void UnknownItem_LeavesBothSidesUnchanged()
        {
            ItemDefinition unregistered = NewItem("missing");
            SaveSystem.Data.currency = 50;

            InventoryTradeMutationResult result = inventory.TryApplyTradeWithoutSave(unregistered, 1, -10, out var receipt);

            Assert.AreEqual(InventoryTradeMutationCode.UnknownItem, result.Code);
            Assert.IsFalse(receipt.Changed);
            Assert.AreEqual(50, SaveSystem.Data.currency);
            Assert.AreEqual(0, SaveSystem.Data.items.Count);
            AssertNoSideEffects();
            UnityEngine.Object.DestroyImmediate(unregistered);
        }

        [Test]
        public void CurrencyOverflow_RejectsTheEntireSale()
        {
            ItemDefinition item = RegisterItem("potion");
            SaveSystem.Data.currency = int.MaxValue;
            Hold("potion", 1);

            InventoryTradeMutationResult result = inventory.TryApplyTradeWithoutSave(item, -1, 1, out var receipt);

            Assert.AreEqual(InventoryTradeMutationCode.CurrencyOverflow, result.Code);
            Assert.IsFalse(receipt.Changed);
            Assert.AreEqual(int.MaxValue, SaveSystem.Data.currency);
            Assert.AreEqual(1, inventory.GetItemCount("potion"));
            AssertNoSideEffects();
        }

        [Test]
        public void ItemOverflow_RejectsTheEntirePurchase()
        {
            ItemDefinition item = RegisterItem("potion");
            SaveSystem.Data.currency = 50;
            Hold("potion", int.MaxValue);

            InventoryTradeMutationResult result = inventory.TryApplyTradeWithoutSave(item, 1, -10, out var receipt);

            Assert.AreEqual(InventoryTradeMutationCode.ItemOverflow, result.Code);
            Assert.IsFalse(receipt.Changed);
            Assert.AreEqual(50, SaveSystem.Data.currency);
            Assert.AreEqual(int.MaxValue, inventory.GetItemCount("potion"));
            AssertNoSideEffects();
        }

        [Test]
        public void Rollback_RestoresCurrencyItemObjectsOrderCountsAndNullSlots_Idempotently()
        {
            ItemDefinition item = RegisterItem("potion");
            var before = new InventoryItemState { itemId = "before", count = 4 };
            var sold = new InventoryItemState { itemId = "potion", count = 2 };
            var after = new InventoryItemState { itemId = "after", count = 9 };
            SaveSystem.Data.currency = 7;
            SaveSystem.Data.items.Add(before);
            SaveSystem.Data.items.Add(null);
            SaveSystem.Data.items.Add(sold);
            SaveSystem.Data.items.Add(after);

            InventoryTradeMutationResult result = inventory.TryApplyTradeWithoutSave(item, -2, 30, out var receipt);
            Assert.IsTrue(result.Success);
            Assert.AreEqual(37, SaveSystem.Data.currency);
            CollectionAssert.AreEqual(new[] { "before", null, "after" }, ItemIds());

            inventory.RollbackTradeWithoutSave(receipt);
            Assert.AreEqual(7, SaveSystem.Data.currency);
            Assert.AreEqual(4, SaveSystem.Data.items.Count);
            Assert.AreSame(before, SaveSystem.Data.items[0]);
            Assert.IsNull(SaveSystem.Data.items[1]);
            Assert.AreSame(sold, SaveSystem.Data.items[2]);
            Assert.AreSame(after, SaveSystem.Data.items[3]);
            CollectionAssert.AreEqual(new[] { 4, 0, 2, 9 }, ItemCounts());

            inventory.RollbackTradeWithoutSave(receipt);
            Assert.AreEqual(7, SaveSystem.Data.currency);
            CollectionAssert.AreEqual(new[] { "before", null, "potion", "after" }, ItemIds());
            CollectionAssert.AreEqual(new[] { 4, 0, 2, 9 }, ItemCounts());
            AssertNoSideEffects();
        }

        [Test]
        public void ExplicitExternalSuccessNotification_FiresInventoryChangedOnceOnly()
        {
            ItemDefinition item = RegisterItem("potion");
            inventory.TryApplyTradeWithoutSave(item, 1, 0, out _);

            AssertNoSideEffects();
            inventory.NotifyChangedAfterExternalSave();
            Assert.AreEqual(1, changedCount);
            Assert.AreEqual(0, rewardAppliedCount);
            Assert.AreEqual(0, saveCount);
        }

        [Test]
        public void NoChangeAndNoSaveData_ReturnEmptyReceiptsWithoutMutation()
        {
            ItemDefinition item = RegisterItem("potion");
            InventoryTradeMutationResult noChange = inventory.TryApplyTradeWithoutSave(item, 0, 0, out var noChangeReceipt);
            Assert.AreEqual(InventoryTradeMutationCode.NoChange, noChange.Code);
            Assert.IsFalse(noChangeReceipt.Changed);

            SaveDataField.SetValue(null, null);
            InventoryTradeMutationResult noData = inventory.TryApplyTradeWithoutSave(item, 1, -1, out var noDataReceipt);
            Assert.AreEqual(InventoryTradeMutationCode.NoSaveData, noData.Code);
            Assert.IsFalse(noDataReceipt.Changed);
            SaveDataField.SetValue(null, new SaveData());
            AssertNoSideEffects();
        }

        private void AssertNoSideEffects()
        {
            Assert.AreEqual(0, saveCount);
            Assert.AreEqual(0, changedCount);
            Assert.AreEqual(0, rewardAppliedCount);
        }

        private ItemDefinition RegisterItem(string itemId)
        {
            ItemDefinition item = NewItem(itemId);
            var manager = new SerializedObject(inventory);
            SerializedProperty catalog = manager.FindProperty("itemCatalog");
            catalog.arraySize = 1;
            catalog.GetArrayElementAtIndex(0).objectReferenceValue = item;
            manager.ApplyModifiedPropertiesWithoutUndo();
            Invoke(inventory, "BuildDefinitionLookup");
            return item;
        }

        private static ItemDefinition NewItem(string itemId)
        {
            var item = ScriptableObject.CreateInstance<ItemDefinition>();
            item.hideFlags = HideFlags.HideAndDontSave;
            var serialized = new SerializedObject(item);
            serialized.FindProperty("itemId").stringValue = itemId;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return item;
        }

        private static object Invoke(object target, string name)
        {
            MethodInfo method = target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(method, name);
            return method.Invoke(target, null);
        }

        private static void Hold(string itemId, int count)
        {
            SaveSystem.Data.items.Add(new InventoryItemState { itemId = itemId, count = count });
        }

        private static string[] ItemIds()
        {
            var values = new string[SaveSystem.Data.items.Count];
            for (int i = 0; i < values.Length; i++) values[i] = SaveSystem.Data.items[i]?.itemId;
            return values;
        }

        private static int[] ItemCounts()
        {
            var values = new int[SaveSystem.Data.items.Count];
            for (int i = 0; i < values.Length; i++) values[i] = SaveSystem.Data.items[i]?.count ?? 0;
            return values;
        }

        private void CountChanged() => changedCount++;
        private void CountRewardApplied(InventoryRewardApplyResult _) => rewardAppliedCount++;
    }
}
