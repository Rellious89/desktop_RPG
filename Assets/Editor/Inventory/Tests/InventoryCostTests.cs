using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using Common;
using Inventory;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace InventoryEditor.Tests
{
    /// <summary>
    /// 9.1 비용 지불 토대 - <see cref="InventoryManager.EvaluateCost"/>(판정 전용)와
    /// <see cref="InventoryManager.TrySpendCost"/>(원자적 지불)가 지켜야 하는 성질을 고정한다.
    ///
    /// 저장은 <see cref="InventoryRewardApplyTests"/>와 같은 방식으로 비공개 saveOverride를 갈아 끼워
    /// <b>파일을 전혀 건드리지 않고</b> 횟수만 센다 - 실제 경로는 사람이 플레이한 저장 파일과 같은
    /// Application.persistentDataPath를 가리키므로 시험이 그 경로를 실행해서는 안 된다.
    /// </summary>
    public sealed class InventoryCostTests
    {
        private static readonly FieldInfo SaveDataField =
            typeof(SaveSystem).GetField("data", BindingFlags.NonPublic | BindingFlags.Static);

        private static readonly FieldInfo SaveOverrideField =
            typeof(InventoryManager).GetField("saveOverride", BindingFlags.NonPublic | BindingFlags.Static);

        private static readonly FieldInfo EntryCacheDirtyField =
            typeof(InventoryManager).GetField("entryCacheDirty", BindingFlags.NonPublic | BindingFlags.Instance);

        private GameObject host;
        private InventoryManager inventory;
        private object originalSaveData;
        private int changedCount;
        private int saveCount;
        private int rewardAppliedCount;

        [SetUp]
        public void SetUp()
        {
            Assert.IsNotNull(SaveDataField);
            Assert.IsNotNull(SaveOverrideField);
            Assert.IsNotNull(EntryCacheDirtyField);

            originalSaveData = SaveDataField.GetValue(null);
            SaveDataField.SetValue(null, new SaveData());

            saveCount = 0;
            SaveOverrideField.SetValue(null, new Func<bool>(() => { saveCount++; return true; }));

            host = new GameObject("InventoryCostTests");
            inventory = host.AddComponent<InventoryManager>();
            Invoke(inventory, "Awake");

            changedCount = 0;
            rewardAppliedCount = 0;
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
        private void OnRewardApplied(InventoryRewardApplyResult r) => rewardAppliedCount++;

        // ---- 9.1 성공 - 재화만 ----

        [Test]
        public void Spend_CurrencyOnly_DeductsAndSavesOnce()
        {
            SaveSystem.Data.currency = 100;

            InventoryCostResult result = inventory.TrySpendCost(new InventoryCostRequest(30));

            Assert.IsTrue(result.Success);
            Assert.IsTrue(result.IsPayable);
            Assert.AreEqual(InventoryCostFailureReason.None, result.Reason);
            Assert.AreEqual(string.Empty, result.ItemId);
            Assert.AreEqual(70, SaveSystem.Data.currency);
            Assert.AreEqual(1, saveCount, "성공한 지불은 정확히 한 번만 저장해야 합니다.");
            Assert.AreEqual(1, changedCount, "성공한 지불은 InventoryChanged를 한 번만 보내야 합니다.");
            Assert.AreEqual(0, rewardAppliedCount, "비용 지불은 RewardApplied를 발생시키지 않습니다.");
        }

        [Test]
        public void Spend_CurrencyExactBalance_Succeeds()
        {
            SaveSystem.Data.currency = 30;

            InventoryCostResult result = inventory.TrySpendCost(new InventoryCostRequest(30));

            Assert.IsTrue(result.Success);
            Assert.AreEqual(0, SaveSystem.Data.currency);
            Assert.AreEqual(1, saveCount);
        }

        // ---- 9.1 성공 - 아이템 한 종 ----

        [Test]
        public void Spend_SingleItem_DeductsCount()
        {
            ItemDefinition item = RegisterItem("item_a");
            Hold("item_a", 5);

            InventoryCostResult result = inventory.TrySpendCost(InventoryCostRequest.ForItem(item, 2));

            Assert.IsTrue(result.Success);
            Assert.AreEqual(3, inventory.GetItemCount("item_a"));
            Assert.AreEqual(1, SaveSystem.Data.items.Count);
            Assert.AreEqual(1, saveCount);
            Assert.AreEqual(1, changedCount);
            Assert.AreEqual(0, rewardAppliedCount);
        }

        // ---- 9.1 성공 - 아이템 여러 종 ----

        [Test]
        public void Spend_MultipleItems_DeductsAllInOneSave()
        {
            List<ItemDefinition> items = RegisterItems("item_a", "item_b");
            Hold("item_a", 5);
            Hold("item_b", 3);

            InventoryCostResult result = inventory.TrySpendCost(InventoryCostRequest.Of(
                0,
                InventoryItemCost.Of(items[0], 2),
                InventoryItemCost.Of(items[1], 1)));

            Assert.IsTrue(result.Success);
            Assert.AreEqual(3, inventory.GetItemCount("item_a"));
            Assert.AreEqual(2, inventory.GetItemCount("item_b"));
            Assert.AreEqual(1, saveCount, "아이템이 여러 종이어도 저장은 한 번입니다.");
            Assert.AreEqual(1, changedCount);
        }

        // ---- 9.1 성공 - 재화 + 아이템 ----

        [Test]
        public void Spend_CurrencyAndItems_DeductsTogether()
        {
            List<ItemDefinition> items = RegisterItems("item_a", "item_b");
            SaveSystem.Data.currency = 100;
            Hold("item_a", 5);
            Hold("item_b", 3);

            InventoryCostResult result = inventory.TrySpendCost(InventoryCostRequest.Of(
                40,
                InventoryItemCost.Of(items[0], 1),
                InventoryItemCost.Of(items[1], 3)));

            Assert.IsTrue(result.Success);
            Assert.AreEqual(60, SaveSystem.Data.currency);
            Assert.AreEqual(4, inventory.GetItemCount("item_a"));
            Assert.AreEqual(0, inventory.GetItemCount("item_b"));
            Assert.AreEqual(1, saveCount);
            Assert.AreEqual(1, changedCount);
            Assert.AreEqual(0, rewardAppliedCount);
        }

        // ---- 9.1 정의 지정 / Id 지정 ----

        [Test]
        public void Spend_ByItemId_MatchesSameSaveEntryAsDefinition()
        {
            RegisterItem("item_a");
            Hold("item_a", 4);

            InventoryCostResult result = inventory.TrySpendCost(InventoryCostRequest.ForItem("item_a", 3));

            Assert.IsTrue(result.Success);
            Assert.AreEqual(1, inventory.GetItemCount("item_a"));
            Assert.AreEqual(1, SaveSystem.Data.items.Count, "Id로 지정해도 저장 항목이 새로 생기지 않습니다.");
        }

        [Test]
        public void Cost_ById_PreservesItemIdExactly()
        {
            InventoryItemCost cost = InventoryItemCost.ById("  item_a  ", 1);

            Assert.AreEqual("  item_a  ", cost.ItemId, "Id 문자열을 다듬으면 저장 키와 다른 값을 가리킵니다.");
            Assert.IsNull(cost.Definition);
        }

        [Test]
        public void Cost_ByDefinition_UsesDefinitionItemId()
        {
            ItemDefinition item = RegisterItem("item_a");
            InventoryItemCost cost = InventoryItemCost.Of(item, 2);

            Assert.AreEqual("item_a", cost.ItemId);
            Assert.AreSame(item, cost.Definition);
            Assert.AreEqual(2, cost.Count);
        }

        // ---- 9.1 중복 합산 ----

        [Test]
        public void Spend_DuplicateItemCosts_AggregateIntoOneRequirement()
        {
            ItemDefinition item = RegisterItem("item_a");
            Hold("item_a", 5);

            InventoryCostResult result = inventory.TrySpendCost(InventoryCostRequest.Of(
                0,
                InventoryItemCost.Of(item, 2),
                InventoryItemCost.Of(item, 3)));

            Assert.IsTrue(result.Success);
            Assert.AreEqual(0, inventory.GetItemCount("item_a"));
            Assert.AreEqual(0, SaveSystem.Data.items.Count, "합산 결과가 정확히 보유량이면 항목이 사라집니다.");
            Assert.AreEqual(1, saveCount);
        }

        [Test]
        public void Spend_DuplicateAcrossDefinitionAndId_Aggregate()
        {
            ItemDefinition item = RegisterItem("item_a");
            Hold("item_a", 5);

            InventoryCostResult result = inventory.TrySpendCost(InventoryCostRequest.Of(
                0,
                InventoryItemCost.Of(item, 2),
                InventoryItemCost.ById("item_a", 1)));

            Assert.IsTrue(result.Success);
            Assert.AreEqual(2, inventory.GetItemCount("item_a"));
        }

        [Test]
        public void Evaluate_DuplicateCosts_ReportSummedRequirement()
        {
            ItemDefinition item = RegisterItem("item_a");
            Hold("item_a", 4);

            InventoryCostResult result = inventory.EvaluateCost(InventoryCostRequest.Of(
                0,
                InventoryItemCost.Of(item, 2),
                InventoryItemCost.Of(item, 3)));

            Assert.IsFalse(result.Success);
            Assert.AreEqual(InventoryCostFailureReason.InsufficientItem, result.Reason);
            Assert.AreEqual("item_a", result.ItemId);
            Assert.AreEqual(5, result.RequiredAmount, "중복 칸은 합산해서 판정해야 합니다(2 + 3).");
            Assert.AreEqual(4, result.CurrentAmount);
        }

        // ---- 9.1 비용 없음 / 0 비용 ----

        [Test]
        public void Spend_EmptyRequest_SucceedsWithoutSaveOrNotify()
        {
            SaveSystem.Data.currency = 10;

            InventoryCostResult result = inventory.TrySpendCost(InventoryCostRequest.Empty);

            Assert.IsTrue(result.Success);
            Assert.AreEqual(InventoryCostFailureReason.None, result.Reason);
            Assert.AreEqual(10, SaveSystem.Data.currency);
            Assert.AreEqual(0, saveCount, "낼 것이 없으면 저장하지 않습니다.");
            Assert.AreEqual(0, changedCount);
            Assert.AreEqual(0, rewardAppliedCount);
        }

        [Test]
        public void Spend_ZeroCosts_AreIgnored()
        {
            ItemDefinition item = RegisterItem("item_a");
            Hold("item_a", 2);

            InventoryCostResult result = inventory.TrySpendCost(InventoryCostRequest.Of(
                0, InventoryItemCost.Of(item, 0)));

            Assert.IsTrue(result.Success);
            Assert.AreEqual(2, inventory.GetItemCount("item_a"));
            Assert.AreEqual(0, saveCount);
            Assert.AreEqual(0, changedCount);
        }

        [Test]
        public void Spend_ZeroCountForUnregisteredId_IsIgnoredNotUnknown()
        {
            InventoryCostResult result = inventory.TrySpendCost(
                InventoryCostRequest.Of(0, InventoryItemCost.ById("item_missing", 0)));

            Assert.IsTrue(result.Success, "수량 0인 칸은 요구가 아니므로 등록 여부를 따지지 않습니다.");
            Assert.AreEqual(InventoryCostFailureReason.None, result.Reason);
            Assert.AreEqual(0, saveCount);
        }

        // 수량 0인 칸은 Id 검사보다 <b>앞에서</b> 걸러진다 - 빈 Id / null Id / 공백 Id 어느 쪽도
        // 거절 사유가 되지 않는다. 낼 것이 없는 칸의 Id는 아무것도 가리키지 않기 때문이다.
        [TestCase("", TestName = "Spend_ZeroCountWithEmptyId_IsIgnored")]
        [TestCase((string)null, TestName = "Spend_ZeroCountWithNullId_IsIgnored")]
        [TestCase("   ", TestName = "Spend_ZeroCountWithWhitespaceId_IsIgnored")]
        public void Spend_ZeroCountWithBlankId_IsIgnored(string blankItemId)
        {
            SaveSystem.Data.currency = 10;

            InventoryCostResult result = inventory.TrySpendCost(
                InventoryCostRequest.Of(0, InventoryItemCost.ById(blankItemId, 0)));

            Assert.IsTrue(result.Success, "수량 0인 칸은 Id를 보기도 전에 무시됩니다.");
            Assert.AreEqual(InventoryCostFailureReason.None, result.Reason);
            Assert.AreEqual(10, SaveSystem.Data.currency);
            Assert.AreEqual(0, SaveSystem.Data.items.Count);
            Assert.AreEqual(0, saveCount, "무시된 칸만 있으면 저장하지 않습니다.");
            Assert.AreEqual(0, changedCount);
            Assert.AreEqual(0, rewardAppliedCount);
        }

        [Test]
        public void Evaluate_ZeroCountWithNullDefinition_IsIgnored()
        {
            InventoryCostResult result = inventory.EvaluateCost(
                InventoryCostRequest.Of(0, InventoryItemCost.Of(null, 0)));

            Assert.IsTrue(result.Success, "정의가 null이어도 수량 0이면 칸 자체가 무시됩니다.");
            Assert.AreEqual(InventoryCostFailureReason.None, result.Reason);
        }

        [Test]
        public void Spend_ZeroCountBlankIdDoesNotBlockOtherCosts()
        {
            ItemDefinition item = RegisterItem("item_a");
            SaveSystem.Data.currency = 40;
            Hold("item_a", 3);

            InventoryCostResult result = inventory.TrySpendCost(InventoryCostRequest.Of(
                10,
                InventoryItemCost.ById(string.Empty, 0),
                InventoryItemCost.ById("item_missing", 0),
                InventoryItemCost.Of(item, 2)));

            Assert.IsTrue(result.Success, "무시되는 칸이 섞여도 나머지 비용은 정상적으로 지불됩니다.");
            Assert.AreEqual(30, SaveSystem.Data.currency);
            Assert.AreEqual(1, inventory.GetItemCount("item_a"));
            Assert.AreEqual(1, saveCount);
            Assert.AreEqual(1, changedCount);
        }

        // ---- 9.1 재화 부족 - 전부 그대로 ----

        [Test]
        public void Spend_InsufficientCurrency_IsFullNoOp()
        {
            ItemDefinition item = RegisterItem("item_a");
            SaveSystem.Data.currency = 50;
            Hold("item_a", 5);

            InventoryCostResult result = inventory.TrySpendCost(InventoryCostRequest.Of(
                100, InventoryItemCost.Of(item, 2)));

            Assert.IsFalse(result.Success);
            Assert.AreEqual(InventoryCostFailureReason.InsufficientCurrency, result.Reason);
            Assert.AreEqual(string.Empty, result.ItemId);
            Assert.AreEqual(100, result.RequiredAmount);
            Assert.AreEqual(50, result.CurrentAmount);
            Assert.AreEqual(50, SaveSystem.Data.currency, "실패한 지불은 재화를 건드리지 않습니다.");
            Assert.AreEqual(5, inventory.GetItemCount("item_a"), "실패한 지불은 아이템도 건드리지 않습니다.");
            Assert.AreEqual(1, SaveSystem.Data.items.Count);
            Assert.AreEqual(0, saveCount);
            Assert.AreEqual(0, changedCount);
            Assert.AreEqual(0, rewardAppliedCount);
        }

        // ---- 9.1 아이템 하나 부족 - 재화와 다른 아이템도 그대로 ----

        [Test]
        public void Spend_OneInsufficientItem_LeavesCurrencyAndOtherItemsUnchanged()
        {
            List<ItemDefinition> items = RegisterItems("item_a", "item_b");
            SaveSystem.Data.currency = 100;
            Hold("item_a", 5);
            Hold("item_b", 1);

            InventoryCostResult result = inventory.TrySpendCost(InventoryCostRequest.Of(
                10,
                InventoryItemCost.Of(items[0], 2),
                InventoryItemCost.Of(items[1], 3)));

            Assert.IsFalse(result.Success);
            Assert.AreEqual(InventoryCostFailureReason.InsufficientItem, result.Reason);
            Assert.AreEqual("item_b", result.ItemId);
            Assert.AreEqual(3, result.RequiredAmount);
            Assert.AreEqual(1, result.CurrentAmount);
            Assert.AreEqual(100, SaveSystem.Data.currency);
            Assert.AreEqual(5, inventory.GetItemCount("item_a"));
            Assert.AreEqual(1, inventory.GetItemCount("item_b"));
            Assert.AreEqual(2, SaveSystem.Data.items.Count);
            Assert.AreEqual(0, saveCount);
            Assert.AreEqual(0, changedCount);
            Assert.AreEqual(0, rewardAppliedCount);
        }

        [Test]
        public void Spend_ItemNotHeldAtAll_ReportsInsufficientItem()
        {
            ItemDefinition item = RegisterItem("item_a");

            InventoryCostResult result = inventory.TrySpendCost(InventoryCostRequest.ForItem(item, 1));

            Assert.IsFalse(result.Success);
            Assert.AreEqual(InventoryCostFailureReason.InsufficientItem, result.Reason);
            Assert.AreEqual(0, result.CurrentAmount);
            Assert.AreEqual(0, SaveSystem.Data.items.Count, "실패 판정이 저장 항목을 만들면 안 됩니다.");
            Assert.AreEqual(0, saveCount);
        }

        // ---- 9.1 잘못된 요청 거절 ----

        [Test]
        public void Evaluate_NegativeCurrency_IsInvalidRequest()
        {
            InventoryCostResult result = inventory.EvaluateCost(new InventoryCostRequest(-1));

            Assert.IsFalse(result.Success);
            Assert.AreEqual(InventoryCostFailureReason.InvalidRequest, result.Reason);
        }

        [Test]
        public void Evaluate_NegativeItemCount_IsInvalidRequest()
        {
            ItemDefinition item = RegisterItem("item_a");
            Hold("item_a", 5);

            InventoryCostResult result = inventory.EvaluateCost(
                InventoryCostRequest.Of(0, InventoryItemCost.Of(item, -1)));

            Assert.IsFalse(result.Success);
            Assert.AreEqual(InventoryCostFailureReason.InvalidRequest, result.Reason);
            Assert.AreEqual("item_a", result.ItemId);
        }

        [Test]
        public void Evaluate_EmptyItemId_IsInvalidRequest()
        {
            InventoryCostResult result = inventory.EvaluateCost(
                InventoryCostRequest.Of(0, InventoryItemCost.ById(string.Empty, 1)));

            Assert.IsFalse(result.Success);
            Assert.AreEqual(InventoryCostFailureReason.InvalidRequest, result.Reason);
        }

        [Test]
        public void Evaluate_NullItemId_IsInvalidRequest()
        {
            InventoryCostResult result = inventory.EvaluateCost(
                InventoryCostRequest.Of(0, InventoryItemCost.ById(null, 1)));

            Assert.IsFalse(result.Success);
            Assert.AreEqual(InventoryCostFailureReason.InvalidRequest, result.Reason);
            Assert.AreEqual(string.Empty, result.ItemId, "결과의 ItemId는 절대 null이 아닙니다.");
        }

        [Test]
        public void Evaluate_WhitespaceItemId_IsInvalidRequest()
        {
            InventoryCostResult result = inventory.EvaluateCost(
                InventoryCostRequest.Of(0, InventoryItemCost.ById("   ", 1)));

            Assert.IsFalse(result.Success, "수량이 1 이상이면 빈 Id는 여전히 호출부의 실수입니다.");
            Assert.AreEqual(InventoryCostFailureReason.InvalidRequest, result.Reason);
        }

        [Test]
        public void Evaluate_NullDefinition_IsInvalidRequest()
        {
            InventoryCostResult result = inventory.EvaluateCost(
                InventoryCostRequest.Of(0, InventoryItemCost.Of(null, 1)));

            Assert.IsFalse(result.Success);
            Assert.AreEqual(InventoryCostFailureReason.InvalidRequest, result.Reason);
        }

        // 음수 수량은 Id를 무시하는 규칙보다 <b>앞에서</b> 걸린다 - 0으로만 무시할 뿐,
        // "Id가 비었으니 그냥 넘어가자"로 음수가 조용히 통과할 자리는 없다.
        [TestCase("", TestName = "Evaluate_NegativeCountWithEmptyId_IsInvalidRequest")]
        [TestCase((string)null, TestName = "Evaluate_NegativeCountWithNullId_IsInvalidRequest")]
        public void Evaluate_NegativeCountWithBlankId_IsInvalidRequest(string blankItemId)
        {
            InventoryCostResult result = inventory.EvaluateCost(
                InventoryCostRequest.Of(0, InventoryItemCost.ById(blankItemId, -1)));

            Assert.IsFalse(result.Success, "음수 수량은 Id가 비어 있어도 거절됩니다.");
            Assert.AreEqual(InventoryCostFailureReason.InvalidRequest, result.Reason);
            Assert.AreEqual(string.Empty, result.ItemId, "결과의 ItemId는 절대 null이 아닙니다.");
        }

        [Test]
        public void Spend_NegativeCountWithZeroCountSibling_IsFullNoOp()
        {
            ItemDefinition item = RegisterItem("item_a");
            SaveSystem.Data.currency = 40;
            Hold("item_a", 3);

            InventoryCostResult result = inventory.TrySpendCost(InventoryCostRequest.Of(
                10,
                InventoryItemCost.ById(string.Empty, 0),
                InventoryItemCost.Of(item, -1)));

            Assert.IsFalse(result.Success);
            Assert.AreEqual(InventoryCostFailureReason.InvalidRequest, result.Reason);
            Assert.AreEqual(40, SaveSystem.Data.currency);
            Assert.AreEqual(3, inventory.GetItemCount("item_a"));
            Assert.AreEqual(0, saveCount);
            Assert.AreEqual(0, changedCount);
        }

        [Test]
        public void Evaluate_NullRequest_IsInvalidRequest()
        {
            InventoryCostResult result = inventory.EvaluateCost(null);

            Assert.IsFalse(result.Success);
            Assert.AreEqual(InventoryCostFailureReason.InvalidRequest, result.Reason);
        }

        [Test]
        public void Evaluate_UnregisteredItemId_IsUnknownItem()
        {
            RegisterItem("item_a");
            Hold("item_missing", 10);

            InventoryCostResult result = inventory.EvaluateCost(
                InventoryCostRequest.ForItem("item_missing", 2));

            Assert.IsFalse(result.Success);
            Assert.AreEqual(InventoryCostFailureReason.UnknownItem, result.Reason,
                "저장 값이 있어도 카탈로그에 없으면 보유 부족이 아니라 미등록입니다.");
            Assert.AreEqual("item_missing", result.ItemId);
            Assert.AreEqual(2, result.RequiredAmount);
        }

        [Test]
        public void Evaluate_UnknownItem_IsCheckedBeforeCurrency()
        {
            SaveSystem.Data.currency = 0;

            InventoryCostResult result = inventory.EvaluateCost(
                InventoryCostRequest.Of(500, InventoryItemCost.ById("item_missing", 1)));

            Assert.AreEqual(InventoryCostFailureReason.UnknownItem, result.Reason,
                "잔액 부족이 요청 오류를 가리면 원인을 찾기 어렵습니다.");
        }

        [Test]
        public void Evaluate_DuplicateSumOverflow_IsInvalidRequest()
        {
            RegisterItem("item_a");

            InventoryCostResult result = inventory.EvaluateCost(InventoryCostRequest.Of(
                0,
                InventoryItemCost.ById("item_a", int.MaxValue),
                InventoryItemCost.ById("item_a", 1)));

            Assert.IsFalse(result.Success);
            Assert.AreEqual(InventoryCostFailureReason.InvalidRequest, result.Reason,
                "중복 합산이 int를 넘으면 음수로 뒤집혀 '싼 비용'으로 통과할 수 있습니다.");
            Assert.AreEqual("item_a", result.ItemId);
        }

        [Test]
        public void Spend_InvalidRequest_IsFullNoOp()
        {
            ItemDefinition item = RegisterItem("item_a");
            SaveSystem.Data.currency = 100;
            Hold("item_a", 5);

            InventoryCostResult result = inventory.TrySpendCost(InventoryCostRequest.Of(
                10, InventoryItemCost.Of(item, -1)));

            Assert.IsFalse(result.Success);
            Assert.AreEqual(InventoryCostFailureReason.InvalidRequest, result.Reason);
            Assert.AreEqual(100, SaveSystem.Data.currency);
            Assert.AreEqual(5, inventory.GetItemCount("item_a"));
            Assert.AreEqual(0, saveCount);
            Assert.AreEqual(0, changedCount);
            Assert.AreEqual(0, rewardAppliedCount);
        }

        [Test]
        public void Spend_UnknownItem_IsFullNoOp()
        {
            SaveSystem.Data.currency = 100;

            InventoryCostResult result = inventory.TrySpendCost(InventoryCostRequest.Of(
                10, InventoryItemCost.ById("item_missing", 1)));

            Assert.IsFalse(result.Success);
            Assert.AreEqual(InventoryCostFailureReason.UnknownItem, result.Reason);
            Assert.AreEqual(100, SaveSystem.Data.currency);
            Assert.AreEqual(0, saveCount);
            Assert.AreEqual(0, changedCount);
        }

        // ---- 저장 실패 - 되돌리기 ----
        //
        // 저장에 실패했는데 성공을 돌려주면 <b>값은 빠졌고 파일에는 남지 않은</b> 상태로 호출부가
        // "샀다"고 믿는다. 그 판단으로 무언가를 지급하면 앱을 다시 켰을 때 낸 것만 되살아나고 받은
        // 것은 사라진다. 그래서 이 경로는 전부 되돌리고 실패를 알린다.

        [Test]
        public void Spend_SaveFails_RollsBackCurrencyAndReportsSaveFailed()
        {
            SaveSystem.Data.currency = 100;
            FailSaves();

            LogAssert.Expect(LogType.Error, new Regex("저장하지 못해 되돌렸습니다"));
            InventoryCostResult result = inventory.TrySpendCost(new InventoryCostRequest(30));

            Assert.IsFalse(result.Success, "기록에 실패했으면 낸 것도 없다");
            Assert.AreEqual(InventoryCostFailureReason.SaveFailed, result.Reason);
            Assert.AreEqual(30, result.RequiredAmount, "내려던 금액이 그대로 남는다");
            Assert.AreEqual(100, result.CurrentAmount, "되돌린 뒤의 잔액을 알려 준다");
            Assert.AreEqual(100, SaveSystem.Data.currency, "재화가 되돌아와야 합니다.");
            Assert.AreEqual(1, saveCount, "저장은 정확히 한 번만 시도합니다.");
            Assert.AreEqual(0, changedCount, "바뀐 것이 없으므로 InventoryChanged도 없습니다.");
            Assert.AreEqual(0, rewardAppliedCount, "비용 지불은 RewardApplied를 발생시키지 않습니다.");
        }

        [Test]
        public void Spend_SaveFails_RestoresExactItemListOrderAndCounts()
        {
            RegisterItems("item_a", "item_b", "item_c");
            SaveSystem.Data.currency = 100;
            Hold("item_a", 1);
            Hold("item_b", 5);   // 정확히 0이 되어 <b>지워질</b> 항목
            Hold("item_c", 7);

            InventoryItemState first = SaveSystem.Data.items[0];
            InventoryItemState depleted = SaveSystem.Data.items[1];
            InventoryItemState last = SaveSystem.Data.items[2];

            FailSaves();
            LogAssert.Expect(LogType.Error, new Regex("저장하지 못해 되돌렸습니다"));

            InventoryCostResult result = inventory.TrySpendCost(InventoryCostRequest.Of(
                30, InventoryItemCost.ById("item_b", 5), InventoryItemCost.ById("item_c", 2)));

            Assert.AreEqual(InventoryCostFailureReason.SaveFailed, result.Reason);
            Assert.AreEqual(100, SaveSystem.Data.currency);

            Assert.AreEqual(3, SaveSystem.Data.items.Count,
                "지워졌던 항목이 되살아나야 합니다(맨 뒤에 새로 붙는 것이 아닙니다).");
            Assert.AreEqual("item_a", SaveSystem.Data.items[0].itemId);
            Assert.AreEqual("item_b", SaveSystem.Data.items[1].itemId,
                "되돌리기가 획득 순서를 바꾸면 실패한 지불 하나가 인벤토리 배치를 영구히 바꿉니다.");
            Assert.AreEqual("item_c", SaveSystem.Data.items[2].itemId);
            Assert.AreEqual(1, SaveSystem.Data.items[0].count);
            Assert.AreEqual(5, SaveSystem.Data.items[1].count);
            Assert.AreEqual(7, SaveSystem.Data.items[2].count);

            Assert.AreSame(first, SaveSystem.Data.items[0], "항목 객체까지 그대로여야 합니다.");
            Assert.AreSame(depleted, SaveSystem.Data.items[1],
                "지워졌던 항목도 <b>새로 만들지 않고</b> 원래 객체가 제자리로 돌아옵니다.");
            Assert.AreSame(last, SaveSystem.Data.items[2]);

            Assert.AreEqual(1, saveCount);
            Assert.AreEqual(0, changedCount);
            Assert.AreEqual(0, rewardAppliedCount);
        }

        [Test]
        public void Spend_SaveFails_KeepsNullAndDuplicateEntriesInPlace()
        {
            // 손상된 저장 파일(같은 Id 두 줄 + null 칸)에서도 되돌리기는 <b>모양을 그대로</b> 되살린다.
            RegisterItems("item_a");
            SaveSystem.Data.currency = 10;
            Hold("item_a", 2);
            SaveSystem.Data.items.Add(null);
            Hold("item_a", 3);

            InventoryItemState firstRow = SaveSystem.Data.items[0];
            InventoryItemState secondRow = SaveSystem.Data.items[2];

            FailSaves();
            LogAssert.Expect(LogType.Error, new Regex("저장하지 못해 되돌렸습니다"));

            InventoryCostResult result = inventory.TrySpendCost(InventoryCostRequest.ForItem("item_a", 2));

            Assert.AreEqual(InventoryCostFailureReason.SaveFailed, result.Reason);
            Assert.AreEqual(3, SaveSystem.Data.items.Count);
            Assert.AreSame(firstRow, SaveSystem.Data.items[0]);
            Assert.IsNull(SaveSystem.Data.items[1], "null 칸도 자리를 그대로 지킵니다.");
            Assert.AreSame(secondRow, SaveSystem.Data.items[2]);
            Assert.AreEqual(2, SaveSystem.Data.items[0].count, "판정이 본 그 줄의 수량이 되돌아옵니다.");
            Assert.AreEqual(3, SaveSystem.Data.items[2].count);
            Assert.AreEqual(0, changedCount);
        }

        [Test]
        public void Spend_SaveFails_ThenSucceeds_LeavesNoTrace()
        {
            RegisterItems("item_a");
            SaveSystem.Data.currency = 100;
            Hold("item_a", 2);

            FailSaves();
            LogAssert.Expect(LogType.Error, new Regex("저장하지 못해 되돌렸습니다"));
            Assert.IsFalse(inventory.TrySpendCost(InventoryCostRequest.Of(
                30, InventoryItemCost.ById("item_a", 2))).Success);

            SucceedSaves();
            InventoryCostResult second = inventory.TrySpendCost(InventoryCostRequest.Of(
                30, InventoryItemCost.ById("item_a", 2)));

            Assert.IsTrue(second.Success, "되돌린 뒤에는 처음과 같은 상태여야 합니다.");
            Assert.AreEqual(70, SaveSystem.Data.currency, "두 번 빠지면 안 됩니다.");
            Assert.AreEqual(0, SaveSystem.Data.items.Count);
            Assert.AreEqual(2, saveCount, "실패 1회 + 성공 1회");
            Assert.AreEqual(1, changedCount, "성공한 한 번만 알립니다.");
        }

        [Test]
        public void Spend_NoCost_SaveFailureCannotHappen()
        {
            // 낼 것이 없으면 저장 자체를 하지 않으므로 저장 실패로 실패할 일도 없다(기존 동작 그대로).
            FailSaves();

            InventoryCostResult result = inventory.TrySpendCost(InventoryCostRequest.Empty);

            Assert.IsTrue(result.Success);
            Assert.AreEqual(0, saveCount);
            Assert.AreEqual(0, changedCount);
        }

        // ---- 9.1 저장 항목 정리 / 순서 유지 ----

        [Test]
        public void Spend_ExactDepletion_RemovesSaveEntry()
        {
            ItemDefinition item = RegisterItem("item_a");
            Hold("item_a", 2);

            InventoryCostResult result = inventory.TrySpendCost(InventoryCostRequest.ForItem(item, 2));

            Assert.IsTrue(result.Success);
            Assert.AreEqual(0, SaveSystem.Data.items.Count, "수량 0짜리 유령 항목을 남기지 않습니다.");
            Assert.AreEqual(0, inventory.GetItemCount("item_a"));
        }

        [Test]
        public void Spend_ExactDepletion_RemovesOnlyThatEntryAndKeepsOrder()
        {
            RegisterItems("item_a", "item_b", "item_c");
            Hold("item_a", 1);
            Hold("item_b", 5);
            Hold("item_c", 7);

            InventoryItemState first = SaveSystem.Data.items[0];
            InventoryItemState third = SaveSystem.Data.items[2];

            InventoryCostResult result = inventory.TrySpendCost(InventoryCostRequest.ForItem("item_b", 5));

            Assert.IsTrue(result.Success);
            Assert.AreEqual(2, SaveSystem.Data.items.Count);
            Assert.AreEqual("item_a", SaveSystem.Data.items[0].itemId);
            Assert.AreEqual("item_c", SaveSystem.Data.items[1].itemId);
            Assert.AreSame(first, SaveSystem.Data.items[0], "남은 항목의 객체가 바뀌면 안 됩니다.");
            Assert.AreSame(third, SaveSystem.Data.items[1]);
            Assert.AreEqual(1, first.count);
            Assert.AreEqual(7, third.count);
        }

        [Test]
        public void Spend_PartialDepletion_PreservesListIndexAndOrder()
        {
            RegisterItems("item_a", "item_b", "item_c");
            Hold("item_a", 1);
            Hold("item_b", 5);
            Hold("item_c", 7);

            InventoryCostResult result = inventory.TrySpendCost(InventoryCostRequest.ForItem("item_b", 2));

            Assert.IsTrue(result.Success);
            Assert.AreEqual(3, SaveSystem.Data.items.Count);
            Assert.AreEqual("item_a", SaveSystem.Data.items[0].itemId);
            Assert.AreEqual("item_b", SaveSystem.Data.items[1].itemId);
            Assert.AreEqual("item_c", SaveSystem.Data.items[2].itemId);
            Assert.AreEqual(1, SaveSystem.Data.items[0].count);
            Assert.AreEqual(3, SaveSystem.Data.items[1].count);
            Assert.AreEqual(7, SaveSystem.Data.items[2].count);
        }

        [Test]
        public void Spend_MixedDepletion_RemovesOnlyZeroedEntries()
        {
            RegisterItems("item_a", "item_b", "item_c");
            Hold("item_a", 2);
            Hold("item_b", 2);
            Hold("item_c", 2);

            InventoryCostResult result = inventory.TrySpendCost(InventoryCostRequest.Of(
                0,
                InventoryItemCost.ById("item_a", 2),
                InventoryItemCost.ById("item_c", 1)));

            Assert.IsTrue(result.Success);
            Assert.AreEqual(2, SaveSystem.Data.items.Count);
            Assert.AreEqual("item_b", SaveSystem.Data.items[0].itemId);
            Assert.AreEqual(2, SaveSystem.Data.items[0].count);
            Assert.AreEqual("item_c", SaveSystem.Data.items[1].itemId);
            Assert.AreEqual(1, SaveSystem.Data.items[1].count);
            Assert.AreEqual(1, saveCount);
            Assert.AreEqual(1, changedCount);
        }

        [Test]
        public void Spend_RefreshesDisplayCache()
        {
            RegisterItem("item_a");
            Hold("item_a", 3);
            Assert.AreEqual(3, inventory.Items[0].Count);

            inventory.TrySpendCost(InventoryCostRequest.ForItem("item_a", 1));

            Assert.AreEqual(2, inventory.Items[0].Count, "지불 뒤에는 표시 캐시가 다시 만들어져야 합니다.");
        }

        // ---- 9.1 판정은 아무것도 바꾸지 않는다 ----

        [Test]
        public void Evaluate_Success_DoesNotMutateAnything()
        {
            ItemDefinition item = RegisterItem("item_a");
            SaveSystem.Data.currency = 100;
            Hold("item_a", 5);

            InventoryCostResult result = inventory.EvaluateCost(InventoryCostRequest.Of(
                40, InventoryItemCost.Of(item, 2)));

            Assert.IsTrue(result.Success);
            Assert.AreEqual(100, SaveSystem.Data.currency);
            Assert.AreEqual(5, inventory.GetItemCount("item_a"));
            Assert.AreEqual(1, SaveSystem.Data.items.Count);
            Assert.AreEqual(0, saveCount);
            Assert.AreEqual(0, changedCount);
            Assert.AreEqual(0, rewardAppliedCount);
        }

        [Test]
        public void Evaluate_DoesNotCreateSaveEntryForMissingItem()
        {
            RegisterItem("item_a");

            inventory.EvaluateCost(InventoryCostRequest.ForItem("item_a", 3));

            Assert.AreEqual(0, SaveSystem.Data.items.Count);
        }

        [Test]
        public void Evaluate_DoesNotDirtyDisplayCache()
        {
            RegisterItem("item_a");
            Hold("item_a", 5);

            // Items를 한 번 읽어 캐시를 깨끗한 상태로 만든 뒤 판정만 한다.
            _ = inventory.Items;
            Assert.IsFalse((bool)EntryCacheDirtyField.GetValue(inventory));

            inventory.EvaluateCost(InventoryCostRequest.ForItem("item_a", 1));

            Assert.IsFalse((bool)EntryCacheDirtyField.GetValue(inventory),
                "판정만으로 표시 캐시를 더럽히면 안 됩니다.");
        }

        [Test]
        public void Evaluate_RepeatedCalls_ReturnSameAnswer()
        {
            ItemDefinition item = RegisterItem("item_a");
            Hold("item_a", 1);

            var request = InventoryCostRequest.Of(0, InventoryItemCost.Of(item, 2));

            InventoryCostResult first = inventory.EvaluateCost(request);
            InventoryCostResult second = inventory.EvaluateCost(request);

            Assert.AreEqual(first.Success, second.Success);
            Assert.AreEqual(first.Reason, second.Reason);
            Assert.AreEqual(first.ItemId, second.ItemId);
            Assert.AreEqual(first.RequiredAmount, second.RequiredAmount);
            Assert.AreEqual(first.CurrentAmount, second.CurrentAmount);
            Assert.AreEqual(0, saveCount);
        }

        // ---- 9.1 요청 객체는 불변 ----

        [Test]
        public void Request_CopiesItemCostList()
        {
            ItemDefinition item = RegisterItem("item_a");
            var costs = new List<InventoryItemCost> { InventoryItemCost.Of(item, 1) };
            var request = new InventoryCostRequest(0, costs);

            costs.Add(InventoryItemCost.Of(item, 99));

            Assert.AreEqual(1, request.ItemCosts.Count, "요청을 만든 뒤 원본 목록을 바꿔도 요청은 그대로여야 합니다.");
        }

        [Test]
        public void Request_EmptyHasNoCosts()
        {
            Assert.AreEqual(0, InventoryCostRequest.Empty.Currency);
            Assert.IsNotNull(InventoryCostRequest.Empty.ItemCosts);
            Assert.AreEqual(0, InventoryCostRequest.Empty.ItemCosts.Count);
        }

        // ---- 9.1 RewardApplied는 어떤 경우에도 발생하지 않는다 ----

        [Test]
        public void CostOperations_NeverRaiseRewardApplied()
        {
            ItemDefinition item = RegisterItem("item_a");
            SaveSystem.Data.currency = 100;
            Hold("item_a", 5);

            inventory.EvaluateCost(InventoryCostRequest.Of(10, InventoryItemCost.Of(item, 1)));
            inventory.TrySpendCost(InventoryCostRequest.Of(10, InventoryItemCost.Of(item, 1)));
            inventory.TrySpendCost(new InventoryCostRequest(int.MaxValue));
            inventory.TrySpendCost(InventoryCostRequest.ForItem("item_missing", 1));
            inventory.TrySpendCost(InventoryCostRequest.Empty);

            Assert.AreEqual(0, rewardAppliedCount);
        }

        // ---- 9.1 기존 재화 경로 회귀 ----

        [Test]
        public void TrySpendCurrency_Sufficient_StillDeductsAndSavesOnce()
        {
            SaveSystem.Data.currency = 100;

            Assert.IsTrue(inventory.TrySpendCurrency(30));
            Assert.AreEqual(70, SaveSystem.Data.currency);
            Assert.AreEqual(1, saveCount);
            Assert.AreEqual(1, changedCount);
        }

        [Test]
        public void TrySpendCurrency_Insufficient_StillChangesNothing()
        {
            SaveSystem.Data.currency = 10;

            Assert.IsFalse(inventory.TrySpendCurrency(30));
            Assert.AreEqual(10, SaveSystem.Data.currency);
            Assert.AreEqual(0, saveCount);
            Assert.AreEqual(0, changedCount);
        }

        [Test]
        public void TrySpendCurrency_Zero_SucceedsWithoutSave()
        {
            SaveSystem.Data.currency = 10;

            Assert.IsTrue(inventory.TrySpendCurrency(0));
            Assert.AreEqual(10, SaveSystem.Data.currency);
            Assert.AreEqual(0, saveCount);
        }

        [Test]
        public void TrySpendCurrency_Negative_StillLogsErrorAndFails()
        {
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("음수 금액"));
            SaveSystem.Data.currency = 10;

            Assert.IsFalse(inventory.TrySpendCurrency(-5));
            Assert.AreEqual(10, SaveSystem.Data.currency);
            Assert.AreEqual(0, saveCount);
        }

        [Test]
        public void TrySpendCurrencyWithoutSave_StillDeductsWithoutSavingOrNotifying()
        {
            SaveSystem.Data.currency = 100;

            Assert.IsTrue(inventory.TrySpendCurrencyWithoutSave(40));
            Assert.AreEqual(60, SaveSystem.Data.currency);
            Assert.AreEqual(0, saveCount, "회복소 트랜잭션 경로는 저장하지 않습니다.");
            Assert.AreEqual(0, changedCount);

            inventory.RefundCurrencyWithoutSave(40);
            Assert.AreEqual(100, SaveSystem.Data.currency);
            Assert.AreEqual(0, saveCount);
            Assert.AreEqual(0, changedCount);
        }

        [Test]
        public void NotifyChangedAfterExternalSave_StillNotifiesWithoutSaving()
        {
            inventory.NotifyChangedAfterExternalSave();

            Assert.AreEqual(0, saveCount);
            Assert.AreEqual(1, changedCount);
        }

        // ---- 9.1 저장 형식 번호는 그대로 ----

        [Test]
        public void SaveVersion_RemainsV6()
        {
            Assert.AreEqual(6, SaveData.CurrentSaveVersion,
                "거래 영수증은 SaveData 구조나 현재 v6 저장 형식을 바꾸지 않습니다.");
            Assert.AreEqual(6, new SaveData().saveVersion);
        }

        // ---- Helpers ----

        private void Hold(string itemId, int count)
        {
            SaveSystem.Data.items.Add(new InventoryItemState { itemId = itemId, count = count });
        }

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

        /// <summary>다음 저장부터 실패시킨다(파일은 여전히 건드리지 않는다).</summary>
        private void FailSaves()
        {
            SaveOverrideField.SetValue(null, new Func<bool>(() => { saveCount++; return false; }));
        }

        /// <summary>다시 성공하도록 되돌린다.</summary>
        private void SucceedSaves()
        {
            SaveOverrideField.SetValue(null, new Func<bool>(() => { saveCount++; return true; }));
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
