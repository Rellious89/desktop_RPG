using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;
using Dungeon;
using Inventory;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DungeonEditor.Tests
{
    public sealed class DungeonSessionLedgerTests
    {
        private static readonly ConstructorInfo ResultCtor =
            typeof(InventoryRewardApplyResult).GetConstructor(
                BindingFlags.NonPublic | BindingFlags.Instance,
                null,
                new[] { typeof(int), typeof(InventoryRewardItemDelta[]) },
                null);

        private DungeonSessionLedger ledger;
        private readonly List<UnityEngine.Object> created = new List<UnityEngine.Object>();

        [SetUp]
        public void SetUp()
        {
            Assert.IsNotNull(ResultCtor,
                "InventoryRewardApplyResult internal 생성자를 리플렉션으로 찾아야 합니다");
            ledger = new DungeonSessionLedger();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (UnityEngine.Object obj in created)
                if (obj != null) UnityEngine.Object.DestroyImmediate(obj);
            created.Clear();
        }

        // ======== Start - invalid ========

        [Test]
        public void Start_NullDungeon_ReturnsInvalid()
        {
            Assert.AreEqual(SessionStartResult.InvalidDungeon,
                ledger.TryStartSession(null));
            Assert.IsFalse(ledger.HasActiveSession);
        }

        [Test]
        public void Start_InvalidDungeon_ReturnsInvalid()
        {
            DungeonDefinition d = MakeDungeon("");
            Assert.AreEqual(SessionStartResult.InvalidDungeon,
                ledger.TryStartSession(d));
            Assert.IsFalse(ledger.HasActiveSession);
        }

        // ======== Start - valid ========

        [Test]
        public void Start_ValidDungeon_ReturnsStarted()
        {
            DungeonDefinition d = MakeDungeon("forest_01");
            Assert.AreEqual(SessionStartResult.Started,
                ledger.TryStartSession(d));
            Assert.IsTrue(ledger.HasActiveSession);
            Assert.AreSame(d, ledger.ActiveDungeon);
            Assert.AreEqual("forest_01", ledger.ActiveDungeonId);
        }

        [Test]
        public void Participants_CopiesOccupiedFixedSlots_AndPreservesTheirOrder()
        {
            DungeonDefinition dungeon = MakeDungeon("forest_01");
            var partySlots = new[] { "A", string.Empty, "B", null, "C" };

            Assert.AreEqual(SessionStartResult.Started, ledger.TryStartSession(dungeon, partySlots));
            partySlots[0] = "changed-after-entry";

            Assert.IsTrue(ledger.TryCompleteSession(out DungeonSessionSnapshot snapshot));
            CollectionAssert.AreEqual(new[] { "A", "B", "C" }, snapshot.ParticipantCharacterIds);
        }

        [Test]
        public void Participants_DuplicateIds_AreExcludedWithOrdinalComparison()
        {
            DungeonDefinition dungeon = MakeDungeon("forest_01");
            Assert.AreEqual(SessionStartResult.Started,
                ledger.TryStartSession(dungeon, new[] { "A", "A", "a", "B", "B" }));

            ledger.TryCompleteSession(out DungeonSessionSnapshot snapshot);

            CollectionAssert.AreEqual(new[] { "A", "a", "B" }, snapshot.ParticipantCharacterIds);
        }

        [Test]
        public void Participants_DuplicateStart_DoesNotOverwriteActiveSnapshot()
        {
            DungeonDefinition dungeon = MakeDungeon("forest_01");
            Assert.AreEqual(SessionStartResult.Started, ledger.TryStartSession(dungeon, new[] { "A", "B" }));
            Assert.AreEqual(SessionStartResult.DuplicateIgnored, ledger.TryStartSession(dungeon, new[] { "C" }));

            ledger.TryCompleteSession(out DungeonSessionSnapshot snapshot);

            CollectionAssert.AreEqual(new[] { "A", "B" }, snapshot.ParticipantCharacterIds);
        }

        // ======== Sequence ========

        [Test]
        public void Sequence_AdvancesOnlyOnNewValidSession()
        {
            DungeonDefinition a = MakeDungeon("a");
            DungeonDefinition b = MakeDungeon("b");

            ledger.TryStartSession(a);
            ledger.TryCompleteSession(out DungeonSessionSnapshot snap1);
            Assert.AreEqual(1L, snap1.SessionSequence);

            ledger.TryStartSession(b);
            ledger.TryCompleteSession(out DungeonSessionSnapshot snap2);
            Assert.AreEqual(2L, snap2.SessionSequence);
        }

        [Test]
        public void Start_SequenceExhausted_AtLongMax()
        {
            SetLastSequence(ledger, long.MaxValue);

            DungeonDefinition d = MakeDungeon("forest_01");
            Assert.AreEqual(SessionStartResult.SequenceExhausted,
                ledger.TryStartSession(d));
            Assert.IsFalse(ledger.HasActiveSession);
        }

        // ======== Same-dungeon duplicate ========

        [Test]
        public void Start_SameDungeon_DuplicateIgnored_NoSequenceAdvance()
        {
            DungeonDefinition d = MakeDungeon("forest_01");
            Assert.AreEqual(SessionStartResult.Started,
                ledger.TryStartSession(d));
            Assert.AreEqual(SessionStartResult.DuplicateIgnored,
                ledger.TryStartSession(d));

            ledger.TryCompleteSession(out DungeonSessionSnapshot snap);
            Assert.AreEqual(1L, snap.SessionSequence,
                "중복 시작은 시퀀스를 진행하지 않는다");
        }

        [Test]
        public void Start_DifferentInstanceSameId_DuplicateIgnored()
        {
            DungeonDefinition d1 = MakeDungeon("forest_01");
            DungeonDefinition d2 = MakeDungeon("forest_01");
            ledger.TryStartSession(d1);
            Assert.AreEqual(SessionStartResult.DuplicateIgnored,
                ledger.TryStartSession(d2));
        }

        // ======== Different-dungeon conflict ========

        [Test]
        public void Start_DifferentActiveDungeon_ReturnsConflict()
        {
            DungeonDefinition a = MakeDungeon("forest_01");
            DungeonDefinition b = MakeDungeon("cave_01");
            ledger.TryStartSession(a);
            Assert.AreEqual(SessionStartResult.ConflictWithActiveDungeon,
                ledger.TryStartSession(b));
            Assert.AreSame(a, ledger.ActiveDungeon,
                "충돌 시 기존 세션이 유지되어야 한다");
        }

        [Test]
        public void Start_DifferentCaseDungeonId_IsConflictNotDuplicate()
        {
            DungeonDefinition upper = MakeDungeon("Forest_01");
            DungeonDefinition lower = MakeDungeon("forest_01");
            ledger.TryStartSession(upper);
            Assert.AreEqual(SessionStartResult.ConflictWithActiveDungeon,
                ledger.TryStartSession(lower),
                "대소문자가 다른 던전 ID는 중복이 아닌 충돌로 처리한다");
        }

        // ======== Abandon ========

        [Test]
        public void Abandon_ActiveSession_Succeeds()
        {
            DungeonDefinition d = MakeDungeon("forest_01");
            ledger.TryStartSession(d);
            Assert.IsTrue(ledger.TryAbandonSession());
            Assert.IsFalse(ledger.HasActiveSession);
            Assert.AreEqual(0, ledger.PendingCompletedSessionCount,
                "포기한 세션은 대기열에 들어가지 않는다");
        }

        [Test]
        public void Abandon_NoActiveSession_ReturnsFalse()
        {
            Assert.IsFalse(ledger.TryAbandonSession());
        }

        [Test]
        public void Abandon_ThenNewSession_SequenceStillAdvances()
        {
            DungeonDefinition a = MakeDungeon("a");
            DungeonDefinition b = MakeDungeon("b");

            ledger.TryStartSession(a);
            ledger.TryAbandonSession();

            ledger.TryStartSession(b);
            ledger.TryCompleteSession(out DungeonSessionSnapshot snap);
            Assert.AreEqual(2L, snap.SessionSequence,
                "포기해도 시퀀스는 이미 소비되었다");
        }

        // ======== Defeat recording ========

        [Test]
        public void RecordDefeat_ValidMonster_IncrementsKills()
        {
            DungeonDefinition d = MakeDungeon("forest_01");
            MonsterDefinition m = MakeMonster("slime_01");
            ledger.TryStartSession(d);
            ledger.RecordDefeat(m);
            ledger.RecordDefeat(m);
            ledger.TryCompleteSession(out DungeonSessionSnapshot snap);
            Assert.AreEqual(2L, snap.DefeatedMonsterCount);
        }

        [Test]
        public void RecordDefeat_NullMonster_Ignored()
        {
            DungeonDefinition d = MakeDungeon("forest_01");
            ledger.TryStartSession(d);
            ledger.RecordDefeat(null);
            ledger.TryCompleteSession(out DungeonSessionSnapshot snap);
            Assert.AreEqual(0L, snap.DefeatedMonsterCount);
        }

        [Test]
        public void RecordDefeat_InvalidMonster_Ignored()
        {
            DungeonDefinition d = MakeDungeon("forest_01");
            MonsterDefinition m = MakeMonster("");
            ledger.TryStartSession(d);
            ledger.RecordDefeat(m);
            ledger.TryCompleteSession(out DungeonSessionSnapshot snap);
            Assert.AreEqual(0L, snap.DefeatedMonsterCount);
        }

        [Test]
        public void RecordDefeat_NoActiveSession_Ignored()
        {
            MonsterDefinition m = MakeMonster("slime_01");
            ledger.RecordDefeat(m);
            Assert.IsFalse(ledger.HasActiveSession);
        }

        // ======== Empty completion ========

        [Test]
        public void Complete_EmptySession_ValidSnapshot()
        {
            DungeonDefinition d = MakeDungeon("forest_01");
            ledger.TryStartSession(d);
            Assert.IsTrue(ledger.TryCompleteSession(out DungeonSessionSnapshot snap));
            Assert.IsTrue(snap.IsEmpty);
            Assert.AreEqual(0L, snap.EarnedCurrency);
            Assert.AreEqual(0L, snap.DefeatedMonsterCount);
            Assert.AreEqual(0, snap.EarnedItems.Count);
            Assert.AreSame(d, snap.DungeonDefinition);
            Assert.AreEqual("forest_01", snap.DungeonId);
            Assert.IsFalse(ledger.HasActiveSession);
        }

        [Test]
        public void Complete_NoActiveSession_ReturnsFalse()
        {
            Assert.IsFalse(ledger.TryCompleteSession(out DungeonSessionSnapshot snap));
            Assert.IsNull(snap);
        }

        [Test]
        public void Complete_UsesElapsedSecondsProvidedByRuntimeTracker()
        {
            DungeonDefinition d = MakeDungeon("forest_01");
            ledger.TryStartSession(d);

            Assert.IsTrue(ledger.TryCompleteSession(65.75d, out DungeonSessionSnapshot snap));
            Assert.AreEqual(65.75d, snap.ElapsedSeconds, 0.0001d);
        }

        [TestCase(-1d)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        public void Complete_InvalidElapsedSeconds_NormalizesToZero(double elapsedSeconds)
        {
            DungeonDefinition d = MakeDungeon("forest_01");
            ledger.TryStartSession(d);

            Assert.IsTrue(ledger.TryCompleteSession(elapsedSeconds, out DungeonSessionSnapshot snap));
            Assert.AreEqual(0d, snap.ElapsedSeconds);
        }

        // ======== Currency aggregation ========

        [Test]
        public void RecordReward_Currency_Aggregates()
        {
            DungeonDefinition d = MakeDungeon("forest_01");
            ledger.TryStartSession(d);

            ledger.RecordReward(MakeResult(100, null));
            ledger.RecordReward(MakeResult(200, null));

            ledger.TryCompleteSession(out DungeonSessionSnapshot snap);
            Assert.AreEqual(300L, snap.EarnedCurrency);
        }

        [Test]
        public void RecordReward_Currency_SaturatesAtLongMax()
        {
            DungeonDefinition d = MakeDungeon("forest_01");
            ledger.TryStartSession(d);

            ledger.RecordReward(MakeResult(int.MaxValue, null));
            ledger.RecordReward(MakeResult(int.MaxValue, null));
            ledger.RecordReward(MakeResult(int.MaxValue, null));

            ledger.TryCompleteSession(out DungeonSessionSnapshot snap);
            Assert.IsTrue(snap.EarnedCurrency > 0L,
                "재화 합계가 오버플로우로 음수가 되면 안 된다");
            Assert.AreEqual((long)int.MaxValue * 3L, snap.EarnedCurrency);
        }

        [Test]
        public void RecordReward_ZeroCurrency_Ignored()
        {
            DungeonDefinition d = MakeDungeon("forest_01");
            ledger.TryStartSession(d);
            ledger.RecordReward(MakeResult(0, null));
            ledger.TryCompleteSession(out DungeonSessionSnapshot snap);
            Assert.AreEqual(0L, snap.EarnedCurrency);
        }

        // ======== Item aggregation and order ========

        [Test]
        public void RecordReward_Items_PreservesFirstAcquisitionOrder()
        {
            DungeonDefinition d = MakeDungeon("forest_01");
            ledger.TryStartSession(d);

            ItemDefinition itemB = MakeItem("item_b");
            ItemDefinition itemA = MakeItem("item_a");
            ledger.RecordReward(MakeResult(0,
                new InventoryRewardItemDelta(itemB, "item_b", 3)));
            ledger.RecordReward(MakeResult(0,
                new InventoryRewardItemDelta(itemA, "item_a", 2)));

            ledger.TryCompleteSession(out DungeonSessionSnapshot snap);
            Assert.AreEqual(2, snap.EarnedItems.Count);
            Assert.AreEqual("item_b", snap.EarnedItems[0].ItemId,
                "첫 획득 순서 유지: item_b가 먼저");
            Assert.AreEqual(3L, snap.EarnedItems[0].Count);
            Assert.AreEqual("item_a", snap.EarnedItems[1].ItemId);
            Assert.AreEqual(2L, snap.EarnedItems[1].Count);
        }

        [Test]
        public void RecordReward_SameItemId_AggregatesOneLine()
        {
            DungeonDefinition d = MakeDungeon("forest_01");
            ledger.TryStartSession(d);

            ItemDefinition item = MakeItem("item_a");
            ledger.RecordReward(MakeResult(0,
                new InventoryRewardItemDelta(item, "item_a", 3)));
            ledger.RecordReward(MakeResult(0,
                new InventoryRewardItemDelta(item, "item_a", 7)));

            ledger.TryCompleteSession(out DungeonSessionSnapshot snap);
            Assert.AreEqual(1, snap.EarnedItems.Count);
            Assert.AreEqual(10L, snap.EarnedItems[0].Count);
        }

        [Test]
        public void RecordReward_ItemCount_SaturatesAtLongMax()
        {
            DungeonDefinition d = MakeDungeon("forest_01");
            ledger.TryStartSession(d);

            ItemDefinition item = MakeItem("item_a");
            ledger.RecordReward(MakeResult(0,
                new InventoryRewardItemDelta(item, "item_a", 1)));

            SetActiveItemCount(ledger, 0, long.MaxValue - 5);
            ledger.RecordReward(MakeResult(0,
                new InventoryRewardItemDelta(item, "item_a", 10)));

            ledger.TryCompleteSession(out DungeonSessionSnapshot snap);
            Assert.AreEqual(long.MaxValue, snap.EarnedItems[0].Count,
                "아이템 수량은 long.MaxValue에서 포화해야 한다");
        }

        [Test]
        public void RecordReward_CanonicalDefinitionRetained()
        {
            DungeonDefinition d = MakeDungeon("forest_01");
            ledger.TryStartSession(d);

            ItemDefinition item = MakeItem("item_a");
            ledger.RecordReward(MakeResult(0,
                new InventoryRewardItemDelta(item, "item_a", 1)));

            ledger.TryCompleteSession(out DungeonSessionSnapshot snap);
            Assert.AreSame(item, snap.EarnedItems[0].ItemDefinition);
        }

        [Test]
        public void RecordReward_EmptyResult_Ignored()
        {
            DungeonDefinition d = MakeDungeon("forest_01");
            ledger.TryStartSession(d);
            ledger.RecordReward(InventoryRewardApplyResult.Empty);
            ledger.TryCompleteSession(out DungeonSessionSnapshot snap);
            Assert.IsTrue(snap.IsEmpty);
        }

        [Test]
        public void RecordReward_NullResult_Ignored()
        {
            DungeonDefinition d = MakeDungeon("forest_01");
            ledger.TryStartSession(d);
            ledger.RecordReward(null);
            ledger.TryCompleteSession(out DungeonSessionSnapshot snap);
            Assert.IsTrue(snap.IsEmpty);
        }

        [Test]
        public void RecordReward_NoActiveSession_Ignored()
        {
            ItemDefinition item = MakeItem("item_a");
            ledger.RecordReward(MakeResult(100,
                new InventoryRewardItemDelta(item, "item_a", 5)));
            Assert.IsFalse(ledger.HasActiveSession);
        }

        // ======== Ordinal ID comparison ========

        [Test]
        public void RecordReward_ItemId_CaseSensitive_OrdinalDistinct()
        {
            DungeonDefinition d = MakeDungeon("forest_01");
            ledger.TryStartSession(d);

            ItemDefinition upper = MakeItem("Item_A");
            ItemDefinition lower = MakeItem("item_a");
            ledger.RecordReward(MakeResult(0,
                new InventoryRewardItemDelta(upper, "Item_A", 1)));
            ledger.RecordReward(MakeResult(0,
                new InventoryRewardItemDelta(lower, "item_a", 1)));

            ledger.TryCompleteSession(out DungeonSessionSnapshot snap);
            Assert.AreEqual(2, snap.EarnedItems.Count,
                "대소문자가 다른 아이템 ID는 별개로 취급한다");
        }

        [Test]
        public void RecordReward_ItemId_WhitespaceSensitive_OrdinalDistinct()
        {
            DungeonDefinition d = MakeDungeon("forest_01");
            ledger.TryStartSession(d);

            ItemDefinition trimmed = MakeItem("item_a");
            ItemDefinition padded = MakeItem("item_a ");
            ledger.RecordReward(MakeResult(0,
                new InventoryRewardItemDelta(trimmed, "item_a", 1)));
            ledger.RecordReward(MakeResult(0,
                new InventoryRewardItemDelta(padded, "item_a ", 1)));

            ledger.TryCompleteSession(out DungeonSessionSnapshot snap);
            Assert.AreEqual(2, snap.EarnedItems.Count,
                "공백이 다른 아이템 ID는 별개로 취급한다");
        }

        // ======== Kill count overflow ========

        [Test]
        public void RecordDefeat_KillCount_SaturatesAtLongMax()
        {
            DungeonDefinition d = MakeDungeon("forest_01");
            MonsterDefinition m = MakeMonster("slime_01");
            ledger.TryStartSession(d);

            SetActiveKills(ledger, long.MaxValue - 1);
            ledger.RecordDefeat(m);
            ledger.RecordDefeat(m);

            ledger.TryCompleteSession(out DungeonSessionSnapshot snap);
            Assert.AreEqual(long.MaxValue, snap.DefeatedMonsterCount,
                "킬 수는 long.MaxValue에서 포화해야 한다");
        }

        [Test]
        public void RecordDefeat_DefeatedMonsterCount_HandlesLongValues()
        {
            DungeonDefinition d = MakeDungeon("forest_01");
            MonsterDefinition m = MakeMonster("slime_01");
            ledger.TryStartSession(d);

            SetActiveKills(ledger, (long)int.MaxValue + 100L);
            ledger.RecordDefeat(m);

            ledger.TryCompleteSession(out DungeonSessionSnapshot snap);
            Assert.AreEqual((long)int.MaxValue + 101L, snap.DefeatedMonsterCount,
                "DefeatedMonsterCount는 int 범위를 넘는 값을 처리해야 한다");
        }

        // ======== Before/after session boundary ========

        [Test]
        public void RecordDefeat_BeforeSession_Ignored()
        {
            MonsterDefinition m = MakeMonster("slime_01");
            ledger.RecordDefeat(m);

            DungeonDefinition d = MakeDungeon("forest_01");
            ledger.TryStartSession(d);
            ledger.TryCompleteSession(out DungeonSessionSnapshot snap);
            Assert.AreEqual(0L, snap.DefeatedMonsterCount);
        }

        [Test]
        public void RecordDefeat_AfterComplete_NotIncluded()
        {
            DungeonDefinition d = MakeDungeon("forest_01");
            MonsterDefinition m = MakeMonster("slime_01");
            ledger.TryStartSession(d);
            ledger.RecordDefeat(m);
            ledger.TryCompleteSession(out DungeonSessionSnapshot snap);

            ledger.RecordDefeat(m);
            Assert.AreEqual(1L, snap.DefeatedMonsterCount,
                "완료 후 기록은 스냅샷에 반영되지 않는다");
        }

        [Test]
        public void RecordReward_AfterComplete_NotIncludedInPreviousSnapshot()
        {
            DungeonDefinition d = MakeDungeon("forest_01");
            ledger.TryStartSession(d);
            ledger.RecordReward(MakeResult(100, null));
            ledger.TryCompleteSession(out DungeonSessionSnapshot snap);

            ledger.RecordReward(MakeResult(999, null));
            Assert.AreEqual(100L, snap.EarnedCurrency);
        }

        // ======== Multiple FIFO sessions ========

        [Test]
        public void MultipleCompleted_FIFOOrder()
        {
            DungeonDefinition a = MakeDungeon("a");
            DungeonDefinition b = MakeDungeon("b");

            ledger.TryStartSession(a);
            ledger.TryCompleteSession(out _);

            ledger.TryStartSession(b);
            ledger.TryCompleteSession(out _);

            Assert.AreEqual(2, ledger.PendingCompletedSessionCount);

            ledger.TryConsumeNextCompletedSession(out DungeonSessionSnapshot first);
            Assert.AreEqual("a", first.DungeonId, "FIFO: 첫 번째는 'a'");

            ledger.TryConsumeNextCompletedSession(out DungeonSessionSnapshot second);
            Assert.AreEqual("b", second.DungeonId, "FIFO: 두 번째는 'b'");
        }

        [Test]
        public void NewSession_DoesNotOverwritePending()
        {
            DungeonDefinition a = MakeDungeon("a");
            DungeonDefinition b = MakeDungeon("b");

            ledger.TryStartSession(a);
            ledger.RecordReward(MakeResult(100, null));
            ledger.TryCompleteSession(out _);

            ledger.TryStartSession(b);
            ledger.RecordReward(MakeResult(999, null));
            ledger.TryCompleteSession(out _);

            ledger.TryConsumeNextCompletedSession(out DungeonSessionSnapshot first);
            Assert.AreEqual(100L, first.EarnedCurrency,
                "이전 세션의 스냅샷이 새 세션에 의해 덮어써지면 안 된다");
        }

        // ======== Peek / Consume ========

        [Test]
        public void Peek_NonConsuming()
        {
            DungeonDefinition d = MakeDungeon("forest_01");
            ledger.TryStartSession(d);
            ledger.TryCompleteSession(out _);

            Assert.IsTrue(ledger.TryPeekNextCompletedSession(out DungeonSessionSnapshot s1));
            Assert.IsTrue(ledger.TryPeekNextCompletedSession(out DungeonSessionSnapshot s2));
            Assert.AreSame(s1, s2);
            Assert.AreEqual(1, ledger.PendingCompletedSessionCount);
        }

        [Test]
        public void Consume_RemovesFromQueue()
        {
            DungeonDefinition d = MakeDungeon("forest_01");
            ledger.TryStartSession(d);
            ledger.TryCompleteSession(out _);

            Assert.IsTrue(ledger.TryConsumeNextCompletedSession(out _));
            Assert.AreEqual(0, ledger.PendingCompletedSessionCount);
            Assert.IsFalse(ledger.TryConsumeNextCompletedSession(out _));
        }

        [Test]
        public void Consume_NoDoubleConsume()
        {
            DungeonDefinition d = MakeDungeon("forest_01");
            ledger.TryStartSession(d);
            ledger.TryCompleteSession(out _);

            ledger.TryConsumeNextCompletedSession(out _);
            Assert.IsFalse(ledger.TryConsumeNextCompletedSession(out DungeonSessionSnapshot s));
            Assert.IsNull(s);
        }

        [Test]
        public void Peek_EmptyQueue_ReturnsFalse()
        {
            Assert.IsFalse(ledger.TryPeekNextCompletedSession(out DungeonSessionSnapshot s));
            Assert.IsNull(s);
        }

        // ======== Immutability / deep copy ========

        [Test]
        public void Snapshot_EarnedItems_Immutable()
        {
            DungeonDefinition d = MakeDungeon("forest_01");
            ledger.TryStartSession(d);
            ItemDefinition item = MakeItem("item_a");
            ledger.RecordReward(MakeResult(0,
                new InventoryRewardItemDelta(item, "item_a", 5)));
            ledger.TryCompleteSession(out DungeonSessionSnapshot snap);

            Assert.Throws<NotSupportedException>(() =>
            {
                ((IList<DungeonSessionItemReward>)snap.EarnedItems).RemoveAt(0);
            });
        }

        [Test]
        public void Snapshot_UnaffectedByLaterSession()
        {
            DungeonDefinition a = MakeDungeon("a");
            DungeonDefinition b = MakeDungeon("b");

            ledger.TryStartSession(a);
            ledger.RecordReward(MakeResult(50, null));
            ledger.TryCompleteSession(out DungeonSessionSnapshot snapA);

            ledger.TryStartSession(b);
            ledger.RecordReward(MakeResult(9999, null));
            ledger.TryCompleteSession(out _);

            Assert.AreEqual(50L, snapA.EarnedCurrency,
                "이전 스냅샷은 이후 세션의 영향을 받지 않는다");
        }

        // ======== No SaveSystem side effect ========

        [Test]
        public void NoSaveSystemCalls()
        {
            FieldInfo dataField =
                typeof(Common.SaveSystem).GetField("data",
                    BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(dataField);

            object before = dataField.GetValue(null);

            DungeonDefinition d = MakeDungeon("forest_01");
            ledger.TryStartSession(d);
            ledger.RecordDefeat(MakeMonster("slime_01"));
            ItemDefinition item = MakeItem("item_a");
            ledger.RecordReward(MakeResult(100,
                new InventoryRewardItemDelta(item, "item_a", 5)));
            ledger.TryCompleteSession(out _);
            ledger.TryConsumeNextCompletedSession(out _);

            object after = dataField.GetValue(null);
            Assert.AreSame(before, after,
                "DungeonSessionLedger는 SaveSystem.data를 변경하지 않는다");
        }

        // ======== Currency overflow saturation ========

        [Test]
        public void RecordReward_Currency_SaturatesAtLongMax_ExtremeCase()
        {
            DungeonDefinition d = MakeDungeon("forest_01");
            ledger.TryStartSession(d);

            SetActiveCurrency(ledger, long.MaxValue - 10L);
            ledger.RecordReward(MakeResult(100, null));

            ledger.TryCompleteSession(out DungeonSessionSnapshot snap);
            Assert.AreEqual(long.MaxValue, snap.EarnedCurrency,
                "재화 합계는 long.MaxValue에서 포화해야 한다");
        }

        // ======== Long value contract ========

        [Test]
        public void RecordReward_ItemCount_HandlesLongValues()
        {
            DungeonDefinition d = MakeDungeon("forest_01");
            ledger.TryStartSession(d);

            ItemDefinition item = MakeItem("item_a");
            ledger.RecordReward(MakeResult(0,
                new InventoryRewardItemDelta(item, "item_a", 1)));

            SetActiveItemCount(ledger, 0, (long)int.MaxValue + 100L);
            ledger.RecordReward(MakeResult(0,
                new InventoryRewardItemDelta(item, "item_a", 50)));

            ledger.TryCompleteSession(out DungeonSessionSnapshot snap);
            Assert.AreEqual((long)int.MaxValue + 150L, snap.EarnedItems[0].Count,
                "아이템 Count는 int 범위를 넘는 값을 처리해야 한다");
        }

        // ======== Integration with Checkpoint A result types ========

        [Test]
        public void RecordReward_MultipleItemDeltasInSingleResult()
        {
            DungeonDefinition d = MakeDungeon("forest_01");
            ledger.TryStartSession(d);

            ItemDefinition itemA = MakeItem("item_a");
            ItemDefinition itemB = MakeItem("item_b");
            var result = MakeResultMultiItem(50,
                new InventoryRewardItemDelta(itemA, "item_a", 3),
                new InventoryRewardItemDelta(itemB, "item_b", 7));

            ledger.RecordReward(result);

            ledger.TryCompleteSession(out DungeonSessionSnapshot snap);
            Assert.AreEqual(50L, snap.EarnedCurrency);
            Assert.AreEqual(2, snap.EarnedItems.Count);
            Assert.AreEqual("item_a", snap.EarnedItems[0].ItemId);
            Assert.AreEqual(3L, snap.EarnedItems[0].Count);
            Assert.AreEqual("item_b", snap.EarnedItems[1].ItemId);
            Assert.AreEqual(7L, snap.EarnedItems[1].Count);
        }

        // ======== Helpers ========

        private DungeonDefinition MakeDungeon(string id)
        {
            var d = ScriptableObject.CreateInstance<DungeonDefinition>();
            d.hideFlags = HideFlags.HideAndDontSave;
            if (!string.IsNullOrEmpty(id))
            {
                var so = new SerializedObject(d);
                so.FindProperty("dungeonId").stringValue = id;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            created.Add(d);
            return d;
        }

        private MonsterDefinition MakeMonster(string id)
        {
            var m = ScriptableObject.CreateInstance<MonsterDefinition>();
            m.hideFlags = HideFlags.HideAndDontSave;
            if (!string.IsNullOrEmpty(id))
            {
                var so = new SerializedObject(m);
                so.FindProperty("monsterId").stringValue = id;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            created.Add(m);
            return m;
        }

        private ItemDefinition MakeItem(string id)
        {
            var item = ScriptableObject.CreateInstance<ItemDefinition>();
            item.hideFlags = HideFlags.HideAndDontSave;
            var so = new SerializedObject(item);
            so.FindProperty("itemId").stringValue = id;
            so.ApplyModifiedPropertiesWithoutUndo();
            created.Add(item);
            return item;
        }

        private static InventoryRewardApplyResult MakeResult(
            int currency, InventoryRewardItemDelta? singleItem)
        {
            var deltas = singleItem.HasValue
                ? new[] { singleItem.Value }
                : Array.Empty<InventoryRewardItemDelta>();

            if (currency == 0 && deltas.Length == 0)
                return InventoryRewardApplyResult.Empty;

            return (InventoryRewardApplyResult)ResultCtor.Invoke(
                new object[] { currency, deltas });
        }

        private static InventoryRewardApplyResult MakeResultMultiItem(
            int currency, params InventoryRewardItemDelta[] items)
        {
            if (currency == 0 && items.Length == 0)
                return InventoryRewardApplyResult.Empty;

            return (InventoryRewardApplyResult)ResultCtor.Invoke(
                new object[] { currency, items });
        }

        private static void SetActiveKills(DungeonSessionLedger l, long value)
        {
            FieldInfo field = typeof(DungeonSessionLedger).GetField(
                "activeKills", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field);
            field.SetValue(l, value);
        }

        private static void SetActiveCurrency(DungeonSessionLedger l, long value)
        {
            FieldInfo field = typeof(DungeonSessionLedger).GetField(
                "activeCurrency", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field);
            field.SetValue(l, value);
        }

        private static void SetLastSequence(DungeonSessionLedger l, long value)
        {
            FieldInfo field = typeof(DungeonSessionLedger).GetField(
                "lastSequence", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field);
            field.SetValue(l, value);
        }

        private static void SetActiveItemCount(DungeonSessionLedger l, int index, long count)
        {
            FieldInfo itemsField = typeof(DungeonSessionLedger).GetField(
                "activeItems", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(itemsField);

            Type accType = typeof(DungeonSessionLedger).GetNestedType(
                "ItemAccumulator", BindingFlags.NonPublic);
            Assert.IsNotNull(accType);

            System.Collections.IList list =
                (System.Collections.IList)itemsField.GetValue(l);
            object existing = list[index];

            FieldInfo defField = accType.GetField("ItemDefinition");
            FieldInfo idField = accType.GetField("ItemId");
            object def = defField.GetValue(existing);
            object itemId = idField.GetValue(existing);

            ConstructorInfo ctor =
                accType.GetConstructors(BindingFlags.Public | BindingFlags.Instance)[0];
            object newAcc = ctor.Invoke(new object[] { def, itemId, count });
            list[index] = newAcc;
        }
    }
}
