using System;
using System.Collections.Generic;
using System.Reflection;
using Common;
using Inventory;
using NUnit.Framework;
using Quest;
using UnityEditor;
using UnityEngine;

namespace QuestEditorTests
{
    /// <summary>완료 확정은 실제 저장 경로 대신 메모리 문서와 저장 이음매만 사용한다.</summary>
    public sealed class CharacterStoryQuestRewardTransactionTests
    {
        private static readonly FieldInfo SaveDataField = typeof(SaveSystem).GetField("data",
            BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly FieldInfo SaveOverrideField = typeof(CharacterStoryQuestService).GetField("saveOverride",
            BindingFlags.NonPublic | BindingFlags.Static);

        private readonly List<UnityEngine.Object> created = new List<UnityEngine.Object>();
        private GameObject inventoryHost;
        private GameObject serviceHost;
        private InventoryManager inventory;
        private CharacterStoryQuestService service;
        private object originalSaveData;
        private object originalSaveOverride;
        private int saveCount;
        private int changedCount;
        private int rewardAppliedCount;

        [SetUp]
        public void SetUp()
        {
            Assert.NotNull(SaveDataField);
            Assert.NotNull(SaveOverrideField);
            originalSaveData = SaveDataField.GetValue(null);
            originalSaveOverride = SaveOverrideField.GetValue(null);
            SaveDataField.SetValue(null, new SaveData
            {
                currency = 10,
                characterStoryQuests = new List<CharacterStoryQuestSaveState>
                {
                    new CharacterStoryQuestSaveState
                    {
                        characterId = "CatKnight",
                        activeQuestId = "Q1",
                        readyToComplete = true,
                    },
                },
            });

            ItemDefinition item = NewItem("50001");
            CurrencyDefinition jewel = NewCurrency("jewel");
            CharacterStoryQuestDefinition quest = NewQuest(item, jewel);
            CharacterStoryQuestCatalog quests = NewCatalog<CharacterStoryQuestCatalog>("quests", quest);
            CharacterStoryQuestObjectiveCatalog objectives = NewCatalog<CharacterStoryQuestObjectiveCatalog>("objectives");

            inventoryHost = new GameObject("quest-reward-inventory-test");
            inventory = inventoryHost.AddComponent<InventoryManager>();
            var inventorySerialized = new SerializedObject(inventory);
            inventorySerialized.FindProperty("itemCatalog").arraySize = 1;
            inventorySerialized.FindProperty("itemCatalog").GetArrayElementAtIndex(0).objectReferenceValue = item;
            inventorySerialized.ApplyModifiedPropertiesWithoutUndo();
            Invoke(inventory, "BuildDefinitionLookup");

            serviceHost = new GameObject("quest-reward-service-test");
            service = serviceHost.AddComponent<CharacterStoryQuestService>();
            Set(service, "questCatalog", quests); Set(service, "objectiveCatalog", objectives);
            Set(service, "inventoryManager", inventory);

            saveCount = 0;
            changedCount = 0;
            rewardAppliedCount = 0;
            InventoryManager.InventoryChanged += CountChanged;
            inventory.RewardApplied += CountRewardApplied;
        }

        [TearDown]
        public void TearDown()
        {
            InventoryManager.InventoryChanged -= CountChanged;
            if (inventory != null) inventory.RewardApplied -= CountRewardApplied;
            if (serviceHost != null) UnityEngine.Object.DestroyImmediate(serviceHost);
            if (inventoryHost != null) UnityEngine.Object.DestroyImmediate(inventoryHost);
            foreach (UnityEngine.Object asset in created)
                if (asset != null) UnityEngine.Object.DestroyImmediate(asset);
            SaveOverrideField.SetValue(null, originalSaveOverride);
            SaveDataField.SetValue(null, originalSaveData);
        }

        [Test]
        public void Completion_SavesAndNotifiesOnce_AndCannotPayTwice()
        {
            SaveOverrideField.SetValue(null, new Func<bool>(() => { saveCount++; return true; }));

            Assert.IsTrue(service.TryConfirmComplete("CatKnight"));
            CharacterStoryQuestSaveState state = SaveSystem.Data.characterStoryQuests[0];
            Assert.AreEqual(string.Empty, state.activeQuestId);
            CollectionAssert.AreEqual(new[] { "Q1" }, state.completedQuestIds);
            Assert.AreEqual(210, SaveSystem.Data.currency);
            Assert.AreEqual(1, SaveSystem.Data.items.Count);
            Assert.AreEqual("50001", SaveSystem.Data.items[0].itemId);
            Assert.AreEqual(3, SaveSystem.Data.items[0].count);
            Assert.AreEqual(1, saveCount);
            Assert.AreEqual(1, changedCount);
            Assert.AreEqual(1, rewardAppliedCount);

            Assert.IsFalse(service.TryConfirmComplete("CatKnight"));
            Assert.AreEqual(1, saveCount);
            Assert.AreEqual(1, changedCount);
            Assert.AreEqual(1, rewardAppliedCount);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SaveFailureOrException_RollsBackQuestAndInventoryWithoutNotifications(bool throws)
        {
            InventoryItemState existing = new InventoryItemState { itemId = "before", count = 7 };
            List<InventoryItemState> originalItems = SaveSystem.Data.items;
            originalItems.Add(existing);
            SaveOverrideField.SetValue(null, new Func<bool>(() =>
            {
                saveCount++;
                if (throws) throw new InvalidOperationException("test save failure");
                return false;
            }));

            Assert.DoesNotThrow(() => Assert.IsFalse(service.TryConfirmComplete("CatKnight")));
            CharacterStoryQuestSaveState state = SaveSystem.Data.characterStoryQuests[0];
            Assert.AreEqual("Q1", state.activeQuestId);
            Assert.IsTrue(state.readyToComplete);
            Assert.IsEmpty(state.completedQuestIds);
            Assert.AreEqual(10, SaveSystem.Data.currency);
            Assert.AreSame(originalItems, SaveSystem.Data.items);
            Assert.AreSame(existing, SaveSystem.Data.items[0]);
            Assert.AreEqual(7, SaveSystem.Data.items[0].count);
            Assert.AreEqual(1, saveCount);
            Assert.AreEqual(0, changedCount);
            Assert.AreEqual(0, rewardAppliedCount);
        }

        private void CountChanged() => changedCount++;
        private void CountRewardApplied(InventoryRewardApplyResult _) => rewardAppliedCount++;

        private T NewCatalog<T>(string property, params UnityEngine.Object[] entries) where T : ScriptableObject
        {
            T catalog = ScriptableObject.CreateInstance<T>();
            created.Add(catalog);
            var serialized = new SerializedObject(catalog);
            SerializedProperty list = serialized.FindProperty(property);
            list.arraySize = entries.Length;
            for (int i = 0; i < entries.Length; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = entries[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return catalog;
        }

        private CharacterStoryQuestDefinition NewQuest(ItemDefinition item, CurrencyDefinition jewel)
        {
            var quest = ScriptableObject.CreateInstance<CharacterStoryQuestDefinition>();
            created.Add(quest);
            Set(quest, "questId", "Q1"); Set(quest, "characterId", "CatKnight"); Set(quest, "isFinal", true);
            Set(quest, "enabled", true);
            Set(quest, "rewards", new List<CharacterStoryQuestRewardDefinition>
            {
                NewReward(CharacterStoryQuestRewardType.Currency, jewel, null, 200),
                NewReward(CharacterStoryQuestRewardType.Item, null, item, 3),
            });
            return quest;
        }

        private CharacterStoryQuestRewardDefinition NewReward(CharacterStoryQuestRewardType type,
            CurrencyDefinition currency, ItemDefinition item, int amount)
        {
            var reward = new CharacterStoryQuestRewardDefinition();
            Set(reward, "rewardType", type); Set(reward, "currency", currency); Set(reward, "item", item);
            Set(reward, "amount", amount);
            return reward;
        }

        private ItemDefinition NewItem(string itemId)
        {
            var item = ScriptableObject.CreateInstance<ItemDefinition>();
            created.Add(item); Set(item, "itemId", itemId); return item;
        }

        private CurrencyDefinition NewCurrency(string currencyId)
        {
            var currency = ScriptableObject.CreateInstance<CurrencyDefinition>();
            created.Add(currency); Set(currency, "currencyId", currencyId); return currency;
        }

        private static void Invoke(object target, string name) => target.GetType().GetMethod(name,
            BindingFlags.NonPublic | BindingFlags.Instance).Invoke(target, null);
        private static void Set(object target, string name, object value) => target.GetType().GetField(name,
            BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);
    }
}
