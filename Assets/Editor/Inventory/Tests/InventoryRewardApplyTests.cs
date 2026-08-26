using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;
using Common;
using Inventory;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace InventoryEditor.Tests
{
    public sealed class InventoryRewardApplyTests
    {
        private static readonly FieldInfo SaveDataField =
            typeof(SaveSystem).GetField("data", BindingFlags.NonPublic | BindingFlags.Static);

        private static readonly FieldInfo SaveOverrideField =
            typeof(InventoryManager).GetField("saveOverride", BindingFlags.NonPublic | BindingFlags.Static);

        private GameObject host;
        private InventoryManager inventory;
        private object originalSaveData;
        private int changedCount;
        private int saveCount;
        private int rewardAppliedCount;
        private InventoryRewardApplyResult lastRewardResult;

        [SetUp]
        public void SetUp()
        {
            Assert.IsNotNull(SaveDataField);
            Assert.IsNotNull(SaveOverrideField);

            originalSaveData = SaveDataField.GetValue(null);
            SaveDataField.SetValue(null, new SaveData());

            saveCount = 0;
            SaveOverrideField.SetValue(null, new Func<bool>(() => { saveCount++; return true; }));

            host = new GameObject("InventoryRewardApplyTests");
            inventory = host.AddComponent<InventoryManager>();
            Invoke(inventory, "Awake");

            changedCount = 0;
            rewardAppliedCount = 0;
            lastRewardResult = null;
            InventoryManager.InventoryChanged += CountChanged;
            inventory.RewardApplied += OnRewardApplied;
        }

        [TearDown]
        public void TearDown()
        {
            InventoryManager.InventoryChanged -= CountChanged;
            if (inventory != null) inventory.RewardApplied -= OnRewardApplied;

            if (host != null) UnityEngine.Object.DestroyImmediate(host);
            inventory = null;

            SaveOverrideField.SetValue(null, null);
            SaveDataField.SetValue(null, originalSaveData);
        }

        private void CountChanged() => changedCount++;
        private void OnRewardApplied(InventoryRewardApplyResult r) { rewardAppliedCount++; lastRewardResult = r; }

        // ---- 6.1 Currency normal ----

        [Test]
        public void Currency_Normal_ReturnsActualDelta()
        {
            SaveSystem.Data.currency = 100;
            InventoryRewardApplyResult result = inventory.ApplyRewards(50, null);

            Assert.AreEqual(50, result.ActualCurrencyDelta);
            Assert.AreEqual(150, SaveSystem.Data.currency);
            Assert.IsTrue(result.Changed);
        }

        // ---- 6.1 Currency saturation ----

        [Test]
        public void Currency_Saturation_ReturnsOnlyActualIncrease()
        {
            SaveSystem.Data.currency = int.MaxValue - 10;
            InventoryRewardApplyResult result = inventory.ApplyRewards(100, null);

            Assert.AreEqual(10, result.ActualCurrencyDelta);
            Assert.AreEqual(int.MaxValue, SaveSystem.Data.currency);
        }

        // ---- 6.1 Currency already max ----

        [Test]
        public void Currency_AlreadyMax_ReturnsZeroDelta()
        {
            SaveSystem.Data.currency = int.MaxValue;
            InventoryRewardApplyResult result = inventory.ApplyRewards(100, null);

            Assert.AreEqual(0, result.ActualCurrencyDelta);
            Assert.IsTrue(result.IsEmpty);
            Assert.AreEqual(0, saveCount);
            Assert.AreEqual(0, changedCount);
            Assert.AreEqual(0, rewardAppliedCount);
        }

        // ---- 6.1 Item normal ----

        [Test]
        public void Item_Normal_ReturnsActualDelta()
        {
            ItemDefinition item = RegisterItem("item_a");
            var stacks = new List<InventoryManager.RewardItemStack>
            {
                new InventoryManager.RewardItemStack(item, 3),
            };

            InventoryRewardApplyResult result = inventory.ApplyRewards(0, stacks);

            Assert.AreEqual(1, result.ItemDeltas.Count);
            Assert.AreSame(item, result.ItemDeltas[0].Definition);
            Assert.AreEqual("item_a", result.ItemDeltas[0].ItemId);
            Assert.AreEqual(3, result.ItemDeltas[0].ActualCount);
        }

        // ---- 6.1 Item duplicates by exact ItemId aggregate ----

        [Test]
        public void Item_DuplicateIds_AggregateIntoOneResultLine()
        {
            ItemDefinition item = RegisterItem("item_a");
            var stacks = new List<InventoryManager.RewardItemStack>
            {
                new InventoryManager.RewardItemStack(item, 2),
                new InventoryManager.RewardItemStack(item, 3),
            };

            InventoryRewardApplyResult result = inventory.ApplyRewards(0, stacks);

            Assert.AreEqual(1, result.ItemDeltas.Count);
            Assert.AreEqual(5, result.ItemDeltas[0].ActualCount);
        }

        // ---- 6.1 Item saturation ----

        [Test]
        public void Item_Saturation_ReturnsOnlyActualIncrease()
        {
            ItemDefinition item = RegisterItem("item_a");
            SaveSystem.Data.items.Add(new InventoryItemState { itemId = "item_a", count = int.MaxValue - 5 });

            var stacks = new List<InventoryManager.RewardItemStack>
            {
                new InventoryManager.RewardItemStack(item, 100),
            };

            InventoryRewardApplyResult result = inventory.ApplyRewards(0, stacks);

            Assert.AreEqual(1, result.ItemDeltas.Count);
            Assert.AreEqual(5, result.ItemDeltas[0].ActualCount);
            Assert.AreEqual(int.MaxValue, SaveSystem.Data.items[0].count);
        }

        [Test]
        public void Item_AlreadyAtMax_NoChange()
        {
            ItemDefinition item = RegisterItem("item_a");
            SaveSystem.Data.items.Add(new InventoryItemState { itemId = "item_a", count = int.MaxValue });

            var stacks = new List<InventoryManager.RewardItemStack>
            {
                new InventoryManager.RewardItemStack(item, 10),
            };

            InventoryRewardApplyResult result = inventory.ApplyRewards(0, stacks);

            Assert.IsTrue(result.IsEmpty);
            Assert.AreEqual(0, saveCount);
        }

        // ---- 6.1 Unknown item excluded ----

        [Test]
        public void Item_Unknown_ExcludedFromResult()
        {
            var unknown = ScriptableObject.CreateInstance<ItemDefinition>();
            unknown.hideFlags = HideFlags.HideAndDontSave;
            var so = new SerializedObject(unknown);
            so.FindProperty("itemId").stringValue = "unknown_item";
            so.ApplyModifiedPropertiesWithoutUndo();

            var stacks = new List<InventoryManager.RewardItemStack>
            {
                new InventoryManager.RewardItemStack(unknown, 5),
            };

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("Item Catalog"));

            InventoryRewardApplyResult result = inventory.ApplyRewards(0, stacks);

            Assert.IsTrue(result.IsEmpty);
            Assert.AreEqual(0, saveCount);

            UnityEngine.Object.DestroyImmediate(unknown);
        }

        // ---- 6.1 Null item excluded ----

        [Test]
        public void Item_Null_ExcludedFromResult()
        {
            var stacks = new List<InventoryManager.RewardItemStack>
            {
                new InventoryManager.RewardItemStack(null, 5),
            };

            InventoryRewardApplyResult result = inventory.ApplyRewards(0, stacks);

            Assert.IsTrue(result.IsEmpty);
            Assert.AreEqual(0, saveCount);
        }

        // ---- 6.1 Nonpositive count excluded ----

        [Test]
        public void Item_NonpositiveCount_ExcludedFromResult()
        {
            ItemDefinition item = RegisterItem("item_a");

            var stacks = new List<InventoryManager.RewardItemStack>
            {
                new InventoryManager.RewardItemStack(item, 0),
                new InventoryManager.RewardItemStack(item, -1),
            };

            InventoryRewardApplyResult result = inventory.ApplyRewards(0, stacks);

            Assert.IsTrue(result.IsEmpty);
            Assert.AreEqual(0, saveCount);
        }

        // ---- 6.1 Empty => save/InventoryChanged/RewardApplied zero ----

        [Test]
        public void Empty_NoSaveNoChangedNoRewardApplied()
        {
            InventoryRewardApplyResult result = inventory.ApplyRewards(0, null);

            Assert.IsTrue(result.IsEmpty);
            Assert.AreEqual(0, saveCount);
            Assert.AreEqual(0, changedCount);
            Assert.AreEqual(0, rewardAppliedCount);
        }

        // ---- 6.1 Changed => each one ----

        [Test]
        public void Changed_SaveOnce_ChangedOnce_RewardAppliedOnce()
        {
            ItemDefinition item = RegisterItem("item_a");
            var stacks = new List<InventoryManager.RewardItemStack>
            {
                new InventoryManager.RewardItemStack(item, 2),
            };

            InventoryRewardApplyResult result = inventory.ApplyRewards(100, stacks);

            Assert.IsTrue(result.Changed);
            Assert.AreEqual(1, saveCount);
            Assert.AreEqual(1, changedCount);
            Assert.AreEqual(1, rewardAppliedCount);
            Assert.AreSame(result, lastRewardResult);
        }

        // ---- 6.1 Batch save once ----

        [Test]
        public void Batch_MultipleItems_SavesOnce()
        {
            List<ItemDefinition> items = RegisterItems("item_a", "item_b");
            var stacks = new List<InventoryManager.RewardItemStack>
            {
                new InventoryManager.RewardItemStack(items[0], 1),
                new InventoryManager.RewardItemStack(items[1], 2),
            };

            InventoryRewardApplyResult result = inventory.ApplyRewards(50, stacks);

            Assert.AreEqual(1, saveCount);
            Assert.AreEqual(1, changedCount);
            Assert.AreEqual(50, result.ActualCurrencyDelta);
            Assert.AreEqual(2, result.ItemDeltas.Count);
        }

        // ---- 6.1 Returned delta equals final inventory difference ----

        [Test]
        public void ReturnedDelta_EqualsFinalInventoryDifference()
        {
            ItemDefinition item = RegisterItem("item_a");
            SaveSystem.Data.currency = 100;
            SaveSystem.Data.items.Add(new InventoryItemState { itemId = "item_a", count = 10 });

            int currencyBefore = SaveSystem.Data.currency;
            int itemBefore = SaveSystem.Data.items[0].count;

            var stacks = new List<InventoryManager.RewardItemStack>
            {
                new InventoryManager.RewardItemStack(item, 7),
            };

            InventoryRewardApplyResult result = inventory.ApplyRewards(200, stacks);

            Assert.AreEqual(SaveSystem.Data.currency - currencyBefore, result.ActualCurrencyDelta);
            Assert.AreEqual(SaveSystem.Data.items[0].count - itemBefore, result.ItemDeltas[0].ActualCount);
        }

        // ---- 6.1 Result/list immutable ----

        [Test]
        public void Result_ItemDeltas_IsImmutable()
        {
            ItemDefinition item = RegisterItem("item_a");
            var stacks = new List<InventoryManager.RewardItemStack>
            {
                new InventoryManager.RewardItemStack(item, 3),
            };

            InventoryRewardApplyResult result = inventory.ApplyRewards(10, stacks);

            ReadOnlyCollection<InventoryRewardItemDelta> deltas = result.ItemDeltas;
            Assert.IsNotNull(deltas);

            Assert.Throws<NotSupportedException>(() =>
            {
                ((IList<InventoryRewardItemDelta>)deltas).RemoveAt(0);
            });
        }

        [Test]
        public void EmptyResult_IsSingleton()
        {
            InventoryRewardApplyResult empty1 = inventory.ApplyRewards(0, null);
            InventoryRewardApplyResult empty2 = inventory.ApplyRewards(0, null);

            Assert.AreSame(InventoryRewardApplyResult.Empty, empty1);
            Assert.AreSame(empty1, empty2);
        }

        // ---- 6.1 RewardApplied not fired by AddCurrency/AddItem/debug/spend/refund ----

        [Test]
        public void AddCurrency_DoesNotFireRewardApplied()
        {
            inventory.AddCurrency(100);
            Assert.AreEqual(0, rewardAppliedCount);
        }

        [Test]
        public void AddItem_DoesNotFireRewardApplied()
        {
            ItemDefinition item = RegisterItem("item_a");
            inventory.AddItem(item, 1);
            Assert.AreEqual(0, rewardAppliedCount);
        }

        [Test]
        public void TrySpendCurrency_DoesNotFireRewardApplied()
        {
            SaveSystem.Data.currency = 1000;
            inventory.TrySpendCurrency(100);
            Assert.AreEqual(0, rewardAppliedCount);
        }

        [Test]
        public void DebugAddCurrency_DoesNotFireRewardApplied()
        {
            inventory.DebugAddCurrency();
            Assert.AreEqual(0, rewardAppliedCount);
        }

        // ---- 6.1 ApplyReward (single-item overload) ----

        [Test]
        public void ApplyReward_SingleItem_ReturnsResult()
        {
            ItemDefinition item = RegisterItem("item_a");

            InventoryRewardApplyResult result = inventory.ApplyReward(50, item, 3);

            Assert.AreEqual(50, result.ActualCurrencyDelta);
            Assert.AreEqual(1, result.ItemDeltas.Count);
            Assert.AreEqual(3, result.ItemDeltas[0].ActualCount);
            Assert.AreEqual(1, saveCount);
            Assert.AreEqual(1, rewardAppliedCount);
        }

        [Test]
        public void ApplyReward_NullItem_CurrencyOnly()
        {
            InventoryRewardApplyResult result = inventory.ApplyReward(100, null);

            Assert.AreEqual(100, result.ActualCurrencyDelta);
            Assert.AreEqual(0, result.ItemDeltas.Count);
            Assert.IsTrue(result.Changed);
        }

        // ---- 6.1 Canonical definition from definitionsById ----

        [Test]
        public void Result_UsesCanonicalDefinition()
        {
            ItemDefinition registered = RegisterItem("item_a");

            var stacks = new List<InventoryManager.RewardItemStack>
            {
                new InventoryManager.RewardItemStack(registered, 1),
            };

            InventoryRewardApplyResult result = inventory.ApplyRewards(0, stacks);

            Assert.AreSame(registered, result.ItemDeltas[0].Definition);
        }

        // ---- 6.3 Renamed script GUID and scene references ----

        [Test]
        public void RenamedScript_GuidPreserved()
        {
            string metaPath = "Assets/Scripts/Inventory/DefeatRewardDistributor.cs.meta";
            string meta = System.IO.File.ReadAllText(
                System.IO.Path.Combine(Application.dataPath, "..", metaPath));
            Assert.IsTrue(meta.Contains("ec5ac0a2b315419e88cda22433753fe4"),
                "DefeatRewardDistributor.cs.meta must preserve the original GUID.");
        }

        [Test]
        public void RenamedScript_OldFileDoesNotExist()
        {
            string oldPath = System.IO.Path.Combine(
                Application.dataPath, "Scripts", "Inventory", "TestDefeatRewardDistributor.cs");
            Assert.IsFalse(System.IO.File.Exists(oldPath),
                "Old TestDefeatRewardDistributor.cs must not remain.");

            string oldMetaPath = oldPath + ".meta";
            Assert.IsFalse(System.IO.File.Exists(oldMetaPath),
                "Old TestDefeatRewardDistributor.cs.meta must not remain.");
        }

        [Test]
        public void RenamedScript_SceneReferencesStillPointToGuid()
        {
            string projectRoot = System.IO.Path.Combine(Application.dataPath, "..");

            foreach (string scene in new[] { "Assets/Scenes/desktopScene_ReSize.unity", "Assets/Scenes/desktopScene.unity" })
            {
                string content = System.IO.File.ReadAllText(
                    System.IO.Path.Combine(projectRoot, scene));
                Assert.IsTrue(content.Contains("ec5ac0a2b315419e88cda22433753fe4"),
                    $"{scene} must still reference GUID ec5ac0a2b315419e88cda22433753fe4.");
            }
        }

        [Test]
        public void RenamedScript_NoOldClassNameInCode()
        {
            string projectRoot = System.IO.Path.Combine(Application.dataPath, "..");
            string distributorPath = System.IO.Path.Combine(
                projectRoot, "Assets", "Scripts", "Inventory", "DefeatRewardDistributor.cs");
            string content = System.IO.File.ReadAllText(distributorPath);
            Assert.IsFalse(content.Contains("class TestDefeatRewardDistributor"),
                "Old class name must not remain in DefeatRewardDistributor.cs.");
        }

        // ---- 6.1 Toast: actual values from result ----

        [Test]
        public void Toast_EmptyResult_NoToast()
        {
            SaveSystem.Data.currency = int.MaxValue;
            InventoryRewardApplyResult result = inventory.ApplyRewards(100, null);
            Assert.IsTrue(result.IsEmpty);
        }

        // ---- 6A.1 Request order regression ----

        [Test]
        public void RequestOrder_BA_ResultIsBA_NotSavedOrder()
        {
            List<ItemDefinition> items = RegisterItems("item_a", "item_b");
            SaveSystem.Data.items.Add(new InventoryItemState { itemId = "item_a", count = 1 });
            SaveSystem.Data.items.Add(new InventoryItemState { itemId = "item_b", count = 1 });

            var stacks = new List<InventoryManager.RewardItemStack>
            {
                new InventoryManager.RewardItemStack(items[1], 5),
                new InventoryManager.RewardItemStack(items[0], 3),
            };

            InventoryRewardApplyResult result = inventory.ApplyRewards(0, stacks);

            Assert.AreEqual(2, result.ItemDeltas.Count);
            Assert.AreEqual("item_b", result.ItemDeltas[0].ItemId,
                "첫 번째 결과는 요청 순서대로 item_b여야 합니다");
            Assert.AreEqual(5, result.ItemDeltas[0].ActualCount);
            Assert.AreEqual("item_a", result.ItemDeltas[1].ItemId,
                "두 번째 결과는 요청 순서대로 item_a여야 합니다");
            Assert.AreEqual(3, result.ItemDeltas[1].ActualCount);
        }

        // ---- 6A.2 Toast formatter integration ----

        [Test]
        public void Toast_SaturatedResult_ShowsActualDelta_NotRequestedAmount()
        {
            ItemDefinition item = RegisterItem("item_a");
            SaveSystem.Data.items.Add(new InventoryItemState { itemId = "item_a", count = int.MaxValue - 5 });
            SaveSystem.Data.currency = int.MaxValue - 10;

            var stacks = new List<InventoryManager.RewardItemStack>
            {
                new InventoryManager.RewardItemStack(item, 1000),
            };
            InventoryRewardApplyResult result = inventory.ApplyRewards(9999, stacks);

            Assert.AreEqual(10, result.ActualCurrencyDelta);
            Assert.AreEqual(5, result.ItemDeltas[0].ActualCount);

            var distHost = new GameObject("TestDistributor");
            var distributor = distHost.AddComponent<DefeatRewardDistributor>();

            try
            {
                string message = (string)Invoke(distributor, "BuildToastMessage", result);

                Assert.IsTrue(message.Contains("+10"),
                    "재화는 실제 증가분(+10)이어야 합니다");
                Assert.IsTrue(message.Contains("x5"),
                    "아이템은 실제 증가분(x5)이어야 합니다");
                Assert.IsFalse(message.Contains("9999"),
                    "요청 재화량(9999)이 토스트에 나타나면 안 됩니다");
                Assert.IsFalse(message.Contains("1000"),
                    "요청 아이템량(1000)이 토스트에 나타나면 안 됩니다");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(distHost);
            }
        }

        [Test]
        public void Toast_EmptyResult_ReturnsEmptyMessage()
        {
            var distHost = new GameObject("TestDistributor");
            var distributor = distHost.AddComponent<DefeatRewardDistributor>();

            try
            {
                string message = (string)Invoke(distributor, "BuildToastMessage",
                    InventoryRewardApplyResult.Empty);
                Assert.IsTrue(string.IsNullOrEmpty(message),
                    "빈 결과의 토스트는 빈 문자열이어야 합니다");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(distHost);
            }
        }

        // ---- 6A.3 Event ordering ----

        [Test]
        public void EventOrder_Save_Then_InventoryChanged_Then_RewardApplied()
        {
            var order = new List<string>();

            SaveOverrideField.SetValue(null, new Func<bool>(() => { order.Add("save"); return true; }));

            Action changedHandler = () => order.Add("changed");
            Action<InventoryRewardApplyResult> rewardHandler = r => order.Add("reward");
            InventoryManager.InventoryChanged += changedHandler;
            inventory.RewardApplied += rewardHandler;

            try
            {
                ItemDefinition item = RegisterItem("item_a");
                var stacks = new List<InventoryManager.RewardItemStack>
                {
                    new InventoryManager.RewardItemStack(item, 1),
                };

                inventory.ApplyRewards(10, stacks);

                Assert.AreEqual(3, order.Count, "save·changed·reward 세 이벤트가 발생해야 합니다");
                Assert.AreEqual("save", order[0]);
                Assert.AreEqual("changed", order[1]);
                Assert.AreEqual("reward", order[2]);
            }
            finally
            {
                InventoryManager.InventoryChanged -= changedHandler;
                inventory.RewardApplied -= rewardHandler;
            }
        }

        // ---- 6A.Luna Corrupt-negative overflow guard ----

        [Test]
        public void Currency_NegativeStoredValue_LargeReward_NeverWraps()
        {
            SaveSystem.Data.currency = -100;
            InventoryRewardApplyResult result = inventory.ApplyRewards(int.MaxValue, null);

            Assert.IsTrue(result.Changed);
            Assert.AreEqual(int.MaxValue, result.ActualCurrencyDelta,
                "부정 잔액에서 큰 보상의 실제 증분이 양수여야 합니다 (int 오버플로우 금지)");
        }

        [Test]
        public void Currency_NegativeStoredValue_ResultClampedToIntMax()
        {
            SaveSystem.Data.currency = int.MinValue;
            InventoryRewardApplyResult result = inventory.ApplyRewards(int.MaxValue, null);

            Assert.AreEqual(0, SaveSystem.Data.currency,
                "int.MinValue + int.MaxValue = -1이므로 0으로 잘린다");
            Assert.AreEqual(int.MaxValue, result.ActualCurrencyDelta,
                "실제 증분(0 - int.MinValue)이 int.MaxValue를 넘으면 int.MaxValue로 잘라야 합니다");
        }

        [Test]
        public void Item_NegativeStoredCount_LargeReward_NeverWraps()
        {
            ItemDefinition item = RegisterItem("item_a");
            SaveSystem.Data.items.Add(new InventoryItemState { itemId = "item_a", count = -500 });

            var stacks = new List<InventoryManager.RewardItemStack>
            {
                new InventoryManager.RewardItemStack(item, int.MaxValue),
            };

            InventoryRewardApplyResult result = inventory.ApplyRewards(0, stacks);

            Assert.AreEqual(1, result.ItemDeltas.Count);
            Assert.AreEqual(int.MaxValue, result.ItemDeltas[0].ActualCount,
                "부정 수량에서 큰 보상의 실제 증분이 양수여야 합니다 (int 오버플로우 금지)");
        }

        [Test]
        public void Item_NegativeStoredCount_MultipleRewards_ResultDeltaClampedToIntMax()
        {
            ItemDefinition item = RegisterItem("item_a");
            SaveSystem.Data.items.Add(new InventoryItemState { itemId = "item_a", count = int.MinValue });

            var stacks = new List<InventoryManager.RewardItemStack>
            {
                new InventoryManager.RewardItemStack(item, int.MaxValue),
                new InventoryManager.RewardItemStack(item, int.MaxValue),
            };

            InventoryRewardApplyResult result = inventory.ApplyRewards(0, stacks);

            Assert.AreEqual(1, result.ItemDeltas.Count);
            Assert.AreEqual(int.MaxValue, result.ItemDeltas[0].ActualCount,
                "두 번 누적 후 실제 증분(after - int.MinValue)이 int.MaxValue를 넘으면 int.MaxValue로 잘라야 합니다");
        }

        [Test]
        public void NegativeStoredValues_CurrencyAndItem_Together_ResultBounded()
        {
            SaveSystem.Data.currency = -1000;

            ItemDefinition item = RegisterItem("item_a");
            SaveSystem.Data.items.Add(new InventoryItemState { itemId = "item_a", count = -200 });

            var stacks = new List<InventoryManager.RewardItemStack>
            {
                new InventoryManager.RewardItemStack(item, 500),
            };

            InventoryRewardApplyResult result = inventory.ApplyRewards(5000, stacks);

            Assert.IsTrue(result.Changed);
            Assert.AreEqual(5000, result.ActualCurrencyDelta);
            Assert.AreEqual(4000, SaveSystem.Data.currency);
            Assert.AreEqual(1, result.ItemDeltas.Count);
            Assert.AreEqual(500, result.ItemDeltas[0].ActualCount);
        }

        [Test]
        public void DefeatMutation_WithoutSave_RollsBackOrder_AndNotifiesOnceAfterSuccess()
        {
            List<ItemDefinition> definitions = RegisterItems("item_a", "item_b");
            SaveSystem.Data.currency = 7;
            SaveSystem.Data.items.Add(new InventoryItemState { itemId = "item_a", count = 2 });
            SaveSystem.Data.items.Add(new InventoryItemState { itemId = "item_b", count = 3 });

            var receipt = inventory.ApplyDefeatRewardsWithoutSave(5,
                new[] { new InventoryManager.RewardItemStack(definitions[0], 4) });

            Assert.AreEqual(0, saveCount); Assert.AreEqual(0, changedCount); Assert.AreEqual(0, rewardAppliedCount);
            Assert.AreEqual(7, receipt.CurrencyBefore); Assert.AreEqual(6, SaveSystem.Data.items[0].count);
            inventory.RollbackDefeatRewards(receipt);
            Assert.AreEqual(7, SaveSystem.Data.currency);
            CollectionAssert.AreEqual(new[] { "item_a", "item_b" }, new[] { SaveSystem.Data.items[0].itemId, SaveSystem.Data.items[1].itemId });
            CollectionAssert.AreEqual(new[] { 2, 3 }, new[] { SaveSystem.Data.items[0].count, SaveSystem.Data.items[1].count });

            receipt = inventory.ApplyDefeatRewardsWithoutSave(5,
                new[] { new InventoryManager.RewardItemStack(definitions[0], 4) });
            inventory.NotifyDefeatRewardsAfterExternalSave(receipt);
            Assert.AreEqual(1, changedCount); Assert.AreEqual(1, rewardAppliedCount);
            Assert.AreEqual(5, lastRewardResult.ActualCurrencyDelta); Assert.AreEqual(4, lastRewardResult.ItemDeltas[0].ActualCount);
        }

        // ---- Helpers ----

        private ItemDefinition RegisterItem(string itemId)
        {
            return RegisterItems(itemId)[0];
        }

        private List<ItemDefinition> RegisterItems(params string[] itemIds)
        {
            var items = new List<ItemDefinition>();
            var serializedManager = new SerializedObject(inventory);
            SerializedProperty list = serializedManager.FindProperty("itemCatalog");
            list.arraySize = itemIds.Length;

            for (int i = 0; i < itemIds.Length; i++)
            {
                var item = ScriptableObject.CreateInstance<ItemDefinition>();
                item.hideFlags = HideFlags.HideAndDontSave;
                var so = new SerializedObject(item);
                so.FindProperty("itemId").stringValue = itemIds[i];
                so.ApplyModifiedPropertiesWithoutUndo();
                list.GetArrayElementAtIndex(i).objectReferenceValue = item;
                items.Add(item);
            }

            serializedManager.ApplyModifiedPropertiesWithoutUndo();
            Invoke(inventory, "BuildDefinitionLookup");
            return items;
        }

        private static object Invoke(object target, string method, params object[] args)
        {
            MethodInfo info = target.GetType().GetMethod(
                method, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(info, $"{target.GetType().Name}.{method}를 찾지 못했습니다.");
            return info.Invoke(target, args);
        }
    }
}
