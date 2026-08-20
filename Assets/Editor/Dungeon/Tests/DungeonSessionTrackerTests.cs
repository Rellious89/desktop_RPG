using System;
using System.Collections.Generic;
using System.Reflection;
using Dungeon;
using Enemy;
using Field;
using Inventory;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace DungeonEditor.Tests
{
    public sealed class DungeonSessionTrackerTests
    {
        private static readonly ConstructorInfo ResultCtor =
            typeof(InventoryRewardApplyResult).GetConstructor(
                BindingFlags.NonPublic | BindingFlags.Instance,
                null,
                new[] { typeof(int), typeof(InventoryRewardItemDelta[]) },
                null);

        private static readonly MethodInfo HandleFieldMode =
            typeof(DungeonSessionTracker).GetMethod(
                "HandleFieldModeChanged",
                BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly MethodInfo HandleDefeat =
            typeof(DungeonSessionTracker).GetMethod(
                "HandleMonsterDefeated",
                BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly MethodInfo HandleReward =
            typeof(DungeonSessionTracker).GetMethod(
                "HandleRewardApplied",
                BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly FieldInfo FmmField =
            typeof(DungeonSessionTracker).GetField(
                "fieldModeManager",
                BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly FieldInfo QueueField =
            typeof(DungeonSessionTracker).GetField(
                "encounterQueue",
                BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly FieldInfo ImField =
            typeof(DungeonSessionTracker).GetField(
                "inventoryManager",
                BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly FieldInfo RealtimeProviderField =
            typeof(DungeonSessionTracker).GetField(
                "realtimeSecondsProvider",
                BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly FieldInfo LastTransitionFrame =
            typeof(FieldModeManager).GetField(
                "lastTransitionFrame",
                BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly FieldInfo SubscribedFmmField =
            typeof(DungeonSessionTracker).GetField(
                "subscribedFmm",
                BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly FieldInfo SubscribedQueueField =
            typeof(DungeonSessionTracker).GetField(
                "subscribedQueue",
                BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly FieldInfo SubscribedImField =
            typeof(DungeonSessionTracker).GetField(
                "subscribedIm",
                BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly MethodInfo StartMethod =
            typeof(DungeonSessionTracker).GetMethod(
                "Start",
                BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly FieldInfo CurrentModeField =
            typeof(FieldModeManager).GetField(
                "<CurrentMode>k__BackingField",
                BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly FieldInfo CurrentDungeonField =
            typeof(FieldModeManager).GetField(
                "<CurrentDungeon>k__BackingField",
                BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly MethodInfo SubscribeMethod =
            typeof(DungeonSessionTracker).GetMethod(
                "Subscribe",
                BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly MethodInfo UnsubscribeMethod =
            typeof(DungeonSessionTracker).GetMethod(
                "Unsubscribe",
                BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly MethodInfo ResyncMethod =
            typeof(DungeonSessionTracker).GetMethod(
                "ResyncWithActualState",
                BindingFlags.NonPublic | BindingFlags.Instance);

        private DungeonSessionTracker tracker;
        private FieldModeManager fmm;
        private MonsterEncounterQueue queue;
        private InventoryManager im;
        private readonly List<UnityEngine.Object> created = new List<UnityEngine.Object>();

        [SetUp]
        public void SetUp()
        {
            Assert.IsNotNull(ResultCtor, "InventoryRewardApplyResult 생성자 리플렉션");
            Assert.IsNotNull(HandleFieldMode, "HandleFieldModeChanged 리플렉션");
            Assert.IsNotNull(HandleDefeat, "HandleMonsterDefeated 리플렉션");
            Assert.IsNotNull(HandleReward, "HandleRewardApplied 리플렉션");
            Assert.IsNotNull(CurrentModeField, "CurrentMode 백킹필드 리플렉션");
            Assert.IsNotNull(CurrentDungeonField, "CurrentDungeon 백킹필드 리플렉션");
            Assert.IsNotNull(RealtimeProviderField, "실시간 공급자 리플렉션");

            var fmmGo = new GameObject("TestFMM");
            fmmGo.SetActive(false);
            fmm = fmmGo.AddComponent<FieldModeManager>();
            fmmGo.SetActive(true);
            created.Add(fmmGo);

            var queueGo = new GameObject("TestQueue");
            queueGo.SetActive(false);
            queue = queueGo.AddComponent<MonsterEncounterQueue>();
            created.Add(queueGo);

            var imGo = new GameObject("TestIM");
            imGo.SetActive(false);
            im = imGo.AddComponent<InventoryManager>();
            created.Add(imGo);

            var trackerGo = new GameObject("TestTracker");
            trackerGo.SetActive(false);
            tracker = trackerGo.AddComponent<DungeonSessionTracker>();
            FmmField.SetValue(tracker, fmm);
            QueueField.SetValue(tracker, queue);
            ImField.SetValue(tracker, im);
            SubscribeMethod.Invoke(tracker, null);
            trackerGo.SetActive(true);
            created.Add(trackerGo);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (UnityEngine.Object obj in created)
                if (obj != null) UnityEngine.Object.DestroyImmediate(obj);
            created.Clear();

            typeof(InventoryManager).GetProperty("Instance",
                    BindingFlags.Public | BindingFlags.Static)
                ?.SetValue(null, null);
        }

        // ======== Authoritative start ========

        [Test]
        public void ApprovalRequestAlone_DoesNotStart()
        {
            Assert.IsFalse(tracker.HasActiveSession,
                "입장 요청 전에는 세션이 없다");

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("입장 요청에 던전이 없습니다"));
            bool accepted = fmm.TryEnterDungeon(null);
            Assert.IsFalse(accepted);
            Assert.IsFalse(tracker.HasActiveSession,
                "실패한 입장 요청은 세션을 시작하지 않는다");
        }

        [Test]
        public void ActualDungeonFieldMode_StartsSession()
        {
            DungeonDefinition d = MakeDungeon("forest_01");
            InvokeFieldMode(FieldMode.Dungeon, d);
            Assert.IsTrue(tracker.HasActiveSession);
        }

        [Test]
        public void SameDungeon_DuplicateIgnored()
        {
            DungeonDefinition d = MakeDungeon("forest_01");
            InvokeFieldMode(FieldMode.Dungeon, d);
            Assert.IsTrue(tracker.HasActiveSession);

            InvokeFieldMode(FieldMode.Dungeon, d);
            Assert.IsTrue(tracker.HasActiveSession,
                "같은 던전 중복 시작은 세션을 유지한다");
        }

        [Test]
        public void InvalidDungeon_NoStart()
        {
            DungeonDefinition d = MakeDungeon("");
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("유효하지 않은 던전"));
            InvokeFieldMode(FieldMode.Dungeon, d);
            Assert.IsFalse(tracker.HasActiveSession,
                "유효하지 않은 던전은 세션을 시작하지 않는다");
        }

        [Test]
        public void NullDungeon_NoStart()
        {
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("유효하지 않은 던전"));
            InvokeFieldMode(FieldMode.Dungeon, null);
            Assert.IsFalse(tracker.HasActiveSession,
                "null 던전은 세션을 시작하지 않는다");
        }

        // ======== Authoritative completion ========

        [Test]
        public void ActualTown_CompletesOnce()
        {
            DungeonDefinition d = MakeDungeon("forest_01");
            InvokeFieldMode(FieldMode.Dungeon, d);
            Assert.IsTrue(tracker.HasActiveSession);

            InvokeFieldMode(FieldMode.Town, null);
            Assert.IsFalse(tracker.HasActiveSession);
            Assert.AreEqual(1, tracker.PendingCompletedSessionCount,
                "마을 복귀 한 번에 완료 한 번");
        }

        [Test]
        public void TownWithNoActiveSession_NoSnapshot()
        {
            Assert.IsFalse(tracker.HasActiveSession);
            InvokeFieldMode(FieldMode.Town, null);
            Assert.AreEqual(0, tracker.PendingCompletedSessionCount,
                "활성 세션이 없으면 마을 복귀에 스냅샷이 생기지 않는다");
        }

        [Test]
        public void EmptySession_ValidComplete()
        {
            DungeonDefinition d = MakeDungeon("forest_01");
            InvokeFieldMode(FieldMode.Dungeon, d);
            InvokeFieldMode(FieldMode.Town, null);

            Assert.IsTrue(tracker.TryPeekNextCompletedSession(out DungeonSessionSnapshot snap));
            Assert.IsTrue(snap.IsEmpty);
            Assert.AreEqual("forest_01", snap.DungeonId);
        }

        [Test]
        public void ActualDungeonToTown_RecordsInjectedRealtimeElapsedSeconds()
        {
            double now = 100d;
            RealtimeProviderField.SetValue(tracker, new Func<double>(() => now));

            DungeonDefinition d = MakeDungeon("forest_01");
            InvokeFieldMode(FieldMode.Dungeon, d);
            now = 165d;
            InvokeFieldMode(FieldMode.Town, null);

            Assert.IsTrue(tracker.TryPeekNextCompletedSession(out DungeonSessionSnapshot snap));
            Assert.AreEqual(65d, snap.ElapsedSeconds, 0.0001d);
        }

        [Test]
        public void DuplicateDungeonSignal_DoesNotResetElapsedTimer()
        {
            double now = 10d;
            RealtimeProviderField.SetValue(tracker, new Func<double>(() => now));

            DungeonDefinition d = MakeDungeon("forest_01");
            InvokeFieldMode(FieldMode.Dungeon, d);
            now = 40d;
            InvokeFieldMode(FieldMode.Dungeon, d);
            now = 75d;
            InvokeFieldMode(FieldMode.Town, null);

            tracker.TryPeekNextCompletedSession(out DungeonSessionSnapshot snap);
            Assert.AreEqual(65d, snap.ElapsedSeconds, 0.0001d,
                "같은 던전 중복 시작 신호는 시작 시간을 갱신하지 않는다");
        }

        [Test]
        public void ElapsedTime_DoesNotDependOnTimeScale()
        {
            float originalTimeScale = Time.timeScale;
            try
            {
                double now = 200d;
                RealtimeProviderField.SetValue(tracker, new Func<double>(() => now));
                Time.timeScale = 0f;

                DungeonDefinition d = MakeDungeon("forest_01");
                InvokeFieldMode(FieldMode.Dungeon, d);
                now = 205d;
                InvokeFieldMode(FieldMode.Town, null);

                tracker.TryPeekNextCompletedSession(out DungeonSessionSnapshot snap);
                Assert.AreEqual(5d, snap.ElapsedSeconds, 0.0001d);
            }
            finally
            {
                Time.timeScale = originalTimeScale;
            }
        }

        // ======== Defeat alignment ========

        [Test]
        public void Defeat_WhenAligned_Records()
        {
            DungeonDefinition d = MakeDungeon("forest_01");
            InvokeFieldMode(FieldMode.Dungeon, d);
            MonsterDefinition m = MakeMonster("slime_01");

            InvokeDefeat(m);
            InvokeFieldMode(FieldMode.Town, null);

            tracker.TryPeekNextCompletedSession(out DungeonSessionSnapshot snap);
            Assert.AreEqual(1L, snap.DefeatedMonsterCount);
        }

        [Test]
        public void Defeat_BeforeSession_Ignored()
        {
            MonsterDefinition m = MakeMonster("slime_01");
            InvokeDefeat(m);

            DungeonDefinition d = MakeDungeon("forest_01");
            InvokeFieldMode(FieldMode.Dungeon, d);
            InvokeFieldMode(FieldMode.Town, null);

            tracker.TryPeekNextCompletedSession(out DungeonSessionSnapshot snap);
            Assert.AreEqual(0L, snap.DefeatedMonsterCount,
                "세션 전 처치는 기록되지 않는다");
        }

        [Test]
        public void Defeat_AfterComplete_Ignored()
        {
            DungeonDefinition d = MakeDungeon("forest_01");
            InvokeFieldMode(FieldMode.Dungeon, d);
            InvokeFieldMode(FieldMode.Town, null);

            MonsterDefinition m = MakeMonster("slime_01");
            InvokeDefeat(m);

            tracker.TryPeekNextCompletedSession(out DungeonSessionSnapshot snap);
            Assert.AreEqual(0L, snap.DefeatedMonsterCount,
                "완료 후 처치는 기록되지 않는다");
        }

        [Test]
        public void Defeat_NullMonster_Ignored()
        {
            DungeonDefinition d = MakeDungeon("forest_01");
            InvokeFieldMode(FieldMode.Dungeon, d);

            InvokeDefeat(null);
            InvokeFieldMode(FieldMode.Town, null);

            tracker.TryPeekNextCompletedSession(out DungeonSessionSnapshot snap);
            Assert.AreEqual(0L, snap.DefeatedMonsterCount);
        }

        [Test]
        public void Defeat_InvalidMonster_Ignored()
        {
            DungeonDefinition d = MakeDungeon("forest_01");
            InvokeFieldMode(FieldMode.Dungeon, d);

            MonsterDefinition m = MakeMonster("");
            InvokeDefeat(m);
            InvokeFieldMode(FieldMode.Town, null);

            tracker.TryPeekNextCompletedSession(out DungeonSessionSnapshot snap);
            Assert.AreEqual(0L, snap.DefeatedMonsterCount);
        }

        [Test]
        public void Defeat_MismatchDungeon_Ignored()
        {
            DungeonDefinition d = MakeDungeon("forest_01");
            InvokeFieldMode(FieldMode.Dungeon, d);

            SetFmmDungeon(MakeDungeon("cave_01"));

            MonsterDefinition m = MakeMonster("slime_01");
            InvokeDefeat(m);

            SetFmmDungeon(d);
            InvokeFieldMode(FieldMode.Town, null);

            tracker.TryPeekNextCompletedSession(out DungeonSessionSnapshot snap);
            Assert.AreEqual(0L, snap.DefeatedMonsterCount,
                "던전 ID 불일치 시 처치가 기록되지 않는다");
        }

        // ======== Reward alignment ========

        [Test]
        public void Reward_WhenAligned_Records()
        {
            DungeonDefinition d = MakeDungeon("forest_01");
            InvokeFieldMode(FieldMode.Dungeon, d);

            InvokeReward(MakeResult(100, null));
            InvokeFieldMode(FieldMode.Town, null);

            tracker.TryPeekNextCompletedSession(out DungeonSessionSnapshot snap);
            Assert.AreEqual(100L, snap.EarnedCurrency);
        }

        [Test]
        public void Reward_BeforeSession_Ignored()
        {
            InvokeReward(MakeResult(100, null));

            DungeonDefinition d = MakeDungeon("forest_01");
            InvokeFieldMode(FieldMode.Dungeon, d);
            InvokeFieldMode(FieldMode.Town, null);

            tracker.TryPeekNextCompletedSession(out DungeonSessionSnapshot snap);
            Assert.AreEqual(0L, snap.EarnedCurrency);
        }

        [Test]
        public void Reward_Null_Ignored()
        {
            DungeonDefinition d = MakeDungeon("forest_01");
            InvokeFieldMode(FieldMode.Dungeon, d);

            InvokeReward(null);
            InvokeFieldMode(FieldMode.Town, null);

            tracker.TryPeekNextCompletedSession(out DungeonSessionSnapshot snap);
            Assert.IsTrue(snap.IsEmpty);
        }

        [Test]
        public void Reward_Empty_Ignored()
        {
            DungeonDefinition d = MakeDungeon("forest_01");
            InvokeFieldMode(FieldMode.Dungeon, d);

            InvokeReward(InventoryRewardApplyResult.Empty);
            InvokeFieldMode(FieldMode.Town, null);

            tracker.TryPeekNextCompletedSession(out DungeonSessionSnapshot snap);
            Assert.IsTrue(snap.IsEmpty);
        }

        [Test]
        public void Reward_MismatchDungeon_Ignored()
        {
            DungeonDefinition d = MakeDungeon("forest_01");
            InvokeFieldMode(FieldMode.Dungeon, d);

            SetFmmDungeon(MakeDungeon("cave_01"));
            InvokeReward(MakeResult(999, null));

            SetFmmDungeon(d);
            InvokeFieldMode(FieldMode.Town, null);

            tracker.TryPeekNextCompletedSession(out DungeonSessionSnapshot snap);
            Assert.AreEqual(0L, snap.EarnedCurrency,
                "던전 ID 불일치 시 보상이 기록되지 않는다");
        }

        // ======== Integration ========

        [Test]
        public void ActualRewardAndKill_Integration()
        {
            DungeonDefinition d = MakeDungeon("forest_01");
            InvokeFieldMode(FieldMode.Dungeon, d);

            MonsterDefinition m = MakeMonster("slime_01");
            InvokeDefeat(m);
            InvokeDefeat(m);

            ItemDefinition item = MakeItem("potion_01");
            InvokeReward(MakeResult(50, new InventoryRewardItemDelta(item, "potion_01", 2)));
            InvokeReward(MakeResult(30, null));

            InvokeFieldMode(FieldMode.Town, null);

            tracker.TryPeekNextCompletedSession(out DungeonSessionSnapshot snap);
            Assert.AreEqual(2L, snap.DefeatedMonsterCount);
            Assert.AreEqual(80L, snap.EarnedCurrency);
            Assert.AreEqual(1, snap.EarnedItems.Count);
            Assert.AreEqual("potion_01", snap.EarnedItems[0].ItemId);
            Assert.AreEqual(2L, snap.EarnedItems[0].Count);
        }

        [Test]
        public void OrdinaryInventoryChanged_NotReached()
        {
            DungeonDefinition d = MakeDungeon("forest_01");
            InvokeFieldMode(FieldMode.Dungeon, d);

            FieldInfo changedField = typeof(InventoryManager).GetField(
                "InventoryChanged", BindingFlags.NonPublic | BindingFlags.Static);

            if (changedField != null)
            {
                var handler = (Action)changedField.GetValue(null);
                handler?.Invoke();
            }

            InvokeFieldMode(FieldMode.Town, null);

            tracker.TryPeekNextCompletedSession(out DungeonSessionSnapshot snap);
            Assert.IsTrue(snap.IsEmpty,
                "InventoryChanged 정적 이벤트는 세션 데이터에 영향을 주지 않는다");
        }

        // ======== SessionCompleted event ========

        [Test]
        public void SessionCompleted_ExactlyOnce()
        {
            int count = 0;
            DungeonSessionSnapshot received = null;
            tracker.SessionCompleted += snap => { count++; received = snap; };

            DungeonDefinition d = MakeDungeon("forest_01");
            InvokeFieldMode(FieldMode.Dungeon, d);
            InvokeReward(MakeResult(42, null));
            InvokeFieldMode(FieldMode.Town, null);

            Assert.AreEqual(1, count, "SessionCompleted는 정확히 한 번 발생해야 한다");
            Assert.IsNotNull(received);
            Assert.AreEqual(42L, received.EarnedCurrency);
        }

        [Test]
        public void SessionCompleted_NotFiredOnTownWithoutSession()
        {
            int count = 0;
            tracker.SessionCompleted += _ => count++;

            InvokeFieldMode(FieldMode.Town, null);
            Assert.AreEqual(0, count);
        }

        // ======== Queue proxy FIFO ========

        [Test]
        public void QueueProxy_FIFO()
        {
            DungeonDefinition a = MakeDungeon("a");
            DungeonDefinition b = MakeDungeon("b");

            InvokeFieldMode(FieldMode.Dungeon, a);
            InvokeReward(MakeResult(10, null));
            InvokeFieldMode(FieldMode.Town, null);

            InvokeFieldMode(FieldMode.Dungeon, b);
            InvokeReward(MakeResult(20, null));
            InvokeFieldMode(FieldMode.Town, null);

            Assert.AreEqual(2, tracker.PendingCompletedSessionCount);

            tracker.TryConsumeNextCompletedSession(out DungeonSessionSnapshot first);
            Assert.AreEqual("a", first.DungeonId);
            Assert.AreEqual(10L, first.EarnedCurrency);

            tracker.TryConsumeNextCompletedSession(out DungeonSessionSnapshot second);
            Assert.AreEqual("b", second.DungeonId);
            Assert.AreEqual(20L, second.EarnedCurrency);
        }

        [Test]
        public void QueueProxy_PeekNonConsuming()
        {
            DungeonDefinition d = MakeDungeon("forest_01");
            InvokeFieldMode(FieldMode.Dungeon, d);
            InvokeFieldMode(FieldMode.Town, null);

            tracker.TryPeekNextCompletedSession(out DungeonSessionSnapshot s1);
            tracker.TryPeekNextCompletedSession(out DungeonSessionSnapshot s2);
            Assert.AreSame(s1, s2);
            Assert.AreEqual(1, tracker.PendingCompletedSessionCount);
        }

        [Test]
        public void QueueProxy_ConsumeRemoves()
        {
            DungeonDefinition d = MakeDungeon("forest_01");
            InvokeFieldMode(FieldMode.Dungeon, d);
            InvokeFieldMode(FieldMode.Town, null);

            Assert.IsTrue(tracker.TryConsumeNextCompletedSession(out _));
            Assert.AreEqual(0, tracker.PendingCompletedSessionCount);
            Assert.IsFalse(tracker.TryConsumeNextCompletedSession(out _));
        }

        // ======== Enable/Disable symmetry ========

        [Test]
        public void EnableDisable_Symmetry()
        {
            DungeonDefinition d = MakeDungeon("forest_01");
            InvokeFieldMode(FieldMode.Dungeon, d);
            Assert.IsTrue(tracker.HasActiveSession);

            SimulateDisable();
            Assert.IsTrue(tracker.HasActiveSession,
                "비활성화는 세션을 완료하지 않는다");

            SimulateEnable();
            Assert.IsTrue(tracker.HasActiveSession,
                "같은 던전 재활성화는 세션을 유지한다");
        }

        [Test]
        public void Resync_SameDungeon_KeepsActive()
        {
            DungeonDefinition d = MakeDungeon("forest_01");
            InvokeFieldMode(FieldMode.Dungeon, d);
            Assert.IsTrue(tracker.HasActiveSession);

            SimulateDisable();
            SimulateEnable();

            Assert.IsTrue(tracker.HasActiveSession,
                "같은 던전으로 재활성화하면 활성 세션을 유지한다");
            Assert.AreEqual(0, tracker.PendingCompletedSessionCount,
                "위조 완료가 생기지 않는다");
        }

        [Test]
        public void Resync_Town_AbandonsActive()
        {
            DungeonDefinition d = MakeDungeon("forest_01");
            EnterDungeon(d);
            Assert.IsTrue(tracker.HasActiveSession);

            SimulateDisable();
            ReturnToTown();

            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("마을 상태이지만 활성 세션"));
            SimulateEnable();

            Assert.IsFalse(tracker.HasActiveSession,
                "마을 상태 재활성화는 활성 세션을 버린다");
            Assert.AreEqual(0, tracker.PendingCompletedSessionCount,
                "위조 완료 스냅샷이 생기지 않는다");
        }

        [Test]
        public void Resync_DifferentDungeon_AbandonsAndStarts()
        {
            DungeonDefinition a = MakeDungeon("a");
            EnterDungeon(a);
            Assert.IsTrue(tracker.HasActiveSession);

            SimulateDisable();
            ReturnToTown();
            DungeonDefinition b = MakeDungeon("b");
            EnterDungeon(b);
            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("다릅니다"));
            SimulateEnable();

            Assert.IsTrue(tracker.HasActiveSession,
                "다른 던전으로 재활성화하면 새 세션이 시작된다");
            Assert.AreEqual(0, tracker.PendingCompletedSessionCount,
                "이전 세션의 위조 완료가 없다");
        }

        // ======== Disable does not complete ========

        [Test]
        public void Disable_DoesNotComplete()
        {
            DungeonDefinition d = MakeDungeon("forest_01");
            InvokeFieldMode(FieldMode.Dungeon, d);
            InvokeReward(MakeResult(100, null));

            SimulateDisable();

            Assert.AreEqual(0, tracker.PendingCompletedSessionCount,
                "비활성화는 세션을 완료하지 않는다");
        }

        // ======== No persistentDataPath ========

        [Test]
        public void NoPersistentDataPath()
        {
            FieldInfo dataField = typeof(Common.SaveSystem).GetField(
                "data", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(dataField);

            object before = dataField.GetValue(null);

            DungeonDefinition d = MakeDungeon("forest_01");
            InvokeFieldMode(FieldMode.Dungeon, d);
            InvokeDefeat(MakeMonster("slime_01"));
            InvokeReward(MakeResult(100, null));
            InvokeFieldMode(FieldMode.Town, null);
            tracker.TryConsumeNextCompletedSession(out _);

            object after = dataField.GetValue(null);
            Assert.AreSame(before, after,
                "DungeonSessionTracker는 SaveSystem.data를 변경하지 않는다");
        }

        // ======== Different-dungeon event: abandon + start ========

        [Test]
        public void DifferentDungeon_Event_AbandonsOldAndStartsNew()
        {
            DungeonDefinition a = MakeDungeon("a");
            InvokeFieldMode(FieldMode.Dungeon, a);
            Assert.IsTrue(tracker.HasActiveSession);

            DungeonDefinition b = MakeDungeon("b");
            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("다른 던전"));
            InvokeFieldMode(FieldMode.Dungeon, b);

            Assert.IsTrue(tracker.HasActiveSession,
                "새 던전 세션이 시작되어야 한다");
            Assert.AreEqual(0, tracker.PendingCompletedSessionCount,
                "이전 세션은 완료가 아니라 버려져야 한다 - 스냅샷 없음");
        }

        [Test]
        public void DifferentDungeon_Event_NewSessionRecords()
        {
            DungeonDefinition a = MakeDungeon("a");
            InvokeFieldMode(FieldMode.Dungeon, a);
            InvokeReward(MakeResult(100, null));

            DungeonDefinition b = MakeDungeon("b");
            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("다른 던전"));
            InvokeFieldMode(FieldMode.Dungeon, b);

            InvokeReward(MakeResult(50, null));
            InvokeFieldMode(FieldMode.Town, null);

            Assert.AreEqual(1, tracker.PendingCompletedSessionCount);
            tracker.TryPeekNextCompletedSession(out DungeonSessionSnapshot snap);
            Assert.AreEqual("b", snap.DungeonId,
                "완료된 세션은 새 던전이어야 한다");
            Assert.AreEqual(50L, snap.EarnedCurrency,
                "이전 던전 보상(100)은 버려지고 새 던전 보상(50)만 남는다");
        }

        // ======== Fail-closed: invalid mode/dungeon pairs ========

        [Test]
        public void InvalidDungeon_Event_WithActiveSession_FailClosed()
        {
            DungeonDefinition d = MakeDungeon("forest_01");
            InvokeFieldMode(FieldMode.Dungeon, d);
            Assert.IsTrue(tracker.HasActiveSession);

            DungeonDefinition invalid = MakeDungeon("");
            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("유효한 던전이 없습니다"));
            LogAssert.Expect(LogType.Error,
                new System.Text.RegularExpressions.Regex("유효하지 않은 던전"));
            InvokeFieldMode(FieldMode.Dungeon, invalid);

            Assert.IsFalse(tracker.HasActiveSession,
                "유효하지 않은 던전 이벤트는 활성 세션을 버린다");
            Assert.AreEqual(0, tracker.PendingCompletedSessionCount,
                "버린 세션은 완료 스냅샷을 생성하지 않는다");
        }

        [Test]
        public void NullDungeon_Event_WithActiveSession_FailClosed()
        {
            DungeonDefinition d = MakeDungeon("forest_01");
            InvokeFieldMode(FieldMode.Dungeon, d);
            Assert.IsTrue(tracker.HasActiveSession);

            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("유효한 던전이 없습니다"));
            LogAssert.Expect(LogType.Error,
                new System.Text.RegularExpressions.Regex("유효하지 않은 던전"));
            InvokeFieldMode(FieldMode.Dungeon, null);

            Assert.IsFalse(tracker.HasActiveSession,
                "null 던전 이벤트는 활성 세션을 버린다");
            Assert.AreEqual(0, tracker.PendingCompletedSessionCount,
                "버린 세션은 완료 스냅샷을 생성하지 않는다");
        }

        [Test]
        public void Town_WithDungeon_AbandonsWithoutCompletion()
        {
            DungeonDefinition d = MakeDungeon("forest_01");
            InvokeFieldMode(FieldMode.Dungeon, d);
            InvokeReward(MakeResult(100, null));
            Assert.IsTrue(tracker.HasActiveSession);

            DungeonDefinition rogue = MakeDungeon("rogue_dungeon");
            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("마을 전환 이벤트에 던전이 포함"));
            InvokeFieldMode(FieldMode.Town, rogue);

            Assert.IsFalse(tracker.HasActiveSession,
                "Town+던전은 활성 세션을 버린다");
            Assert.AreEqual(0, tracker.PendingCompletedSessionCount,
                "Town+던전은 완료 스냅샷 없이 버린다");
        }

        [Test]
        public void Town_WithDungeon_NoActiveSession_NoEffect()
        {
            DungeonDefinition d = MakeDungeon("forest_01");
            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("마을 전환 이벤트에 던전이 포함"));
            InvokeFieldMode(FieldMode.Town, d);

            Assert.IsFalse(tracker.HasActiveSession);
            Assert.AreEqual(0, tracker.PendingCompletedSessionCount);
        }

        [Test]
        public void UnsupportedMode_AbandonsActiveSession()
        {
            DungeonDefinition d = MakeDungeon("forest_01");
            InvokeFieldMode(FieldMode.Dungeon, d);
            InvokeReward(MakeResult(100, null));
            Assert.IsTrue(tracker.HasActiveSession);

            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("지원하지 않는 모드"));
            InvokeFieldMode((FieldMode)999, null);

            Assert.IsFalse(tracker.HasActiveSession,
                "지원하지 않는 모드는 활성 세션을 버린다");
            Assert.AreEqual(0, tracker.PendingCompletedSessionCount,
                "지원하지 않는 모드는 완료 스냅샷 없이 버린다");
        }

        [Test]
        public void UnsupportedMode_NoActiveSession_NoEffect()
        {
            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("지원하지 않는 모드"));
            InvokeFieldMode((FieldMode)999, null);

            Assert.IsFalse(tracker.HasActiveSession);
            Assert.AreEqual(0, tracker.PendingCompletedSessionCount);
        }

        // ======== Resync: invalid dungeon in Dungeon mode ========

        [Test]
        public void Resync_DungeonMode_InvalidDungeon_FailClosed()
        {
            DungeonDefinition d = MakeDungeon("forest_01");
            EnterDungeon(d);
            Assert.IsTrue(tracker.HasActiveSession);

            SimulateDisable();

            DungeonDefinition invalid = MakeDungeon("");
            SetFmmState(FieldMode.Dungeon, invalid);

            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("유효한 던전이 없습니다"));
            SimulateEnable();

            Assert.IsFalse(tracker.HasActiveSession,
                "재동기화 시 유효하지 않은 던전이면 세션을 버린다");
            Assert.AreEqual(0, tracker.PendingCompletedSessionCount,
                "버린 세션은 스냅샷을 만들지 않는다");
        }

        // ======== Start retry ========

        [Test]
        public void Start_AlwaysRetries_EvenWhenPartiallySubscribed()
        {
            SimulateDisable();
            ImField.SetValue(tracker, null);

            SubscribeMethod.Invoke(tracker, null);

            Assert.IsNotNull(SubscribedFmmField.GetValue(tracker), "FMM은 구독됨");
            Assert.IsNotNull(SubscribedQueueField.GetValue(tracker), "Queue는 구독됨");
            Assert.IsNull(SubscribedImField.GetValue(tracker), "IM은 아직 미구독");

            ImField.SetValue(tracker, im);
            StartMethod.Invoke(tracker, null);

            Assert.IsNotNull(SubscribedFmmField.GetValue(tracker), "Start 후 FMM 구독됨");
            Assert.IsNotNull(SubscribedQueueField.GetValue(tracker), "Start 후 Queue 구독됨");
            Assert.IsNotNull(SubscribedImField.GetValue(tracker), "Start 후 IM도 구독됨");
        }

        [Test]
        public void Start_FromCleanState_SubscribesAll()
        {
            SimulateDisable();
            Assert.IsNull(SubscribedFmmField.GetValue(tracker));
            Assert.IsNull(SubscribedQueueField.GetValue(tracker));
            Assert.IsNull(SubscribedImField.GetValue(tracker));

            StartMethod.Invoke(tracker, null);

            Assert.IsNotNull(SubscribedFmmField.GetValue(tracker), "Start는 FMM 구독");
            Assert.IsNotNull(SubscribedQueueField.GetValue(tracker), "Start는 Queue 구독");
            Assert.IsNotNull(SubscribedImField.GetValue(tracker), "Start는 IM 구독");
        }

        // ======== Resync fail-close: Town+dungeon, unsupported mode ========

        [Test]
        public void Resync_Town_WithDungeon_AbandonsWithoutCompletion()
        {
            DungeonDefinition d = MakeDungeon("forest_01");
            EnterDungeon(d);
            Assert.IsTrue(tracker.HasActiveSession);

            SimulateDisable();
            SetFmmState(FieldMode.Town, MakeDungeon("rogue"));

            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("마을인데 던전 참조가 있습니다"));
            SimulateEnable();

            Assert.IsFalse(tracker.HasActiveSession,
                "재동기화 시 Town+던전이면 세션을 버린다");
            Assert.AreEqual(0, tracker.PendingCompletedSessionCount,
                "완료 스냅샷 없이 버린다");
        }

        [Test]
        public void Resync_UnsupportedMode_AbandonsActiveSession()
        {
            DungeonDefinition d = MakeDungeon("forest_01");
            EnterDungeon(d);
            Assert.IsTrue(tracker.HasActiveSession);

            SimulateDisable();
            SetFmmState((FieldMode)999, null);

            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("지원하지 않는 모드"));
            SimulateEnable();

            Assert.IsFalse(tracker.HasActiveSession,
                "재동기화 시 지원하지 않는 모드이면 세션을 버린다");
            Assert.AreEqual(0, tracker.PendingCompletedSessionCount,
                "완료 스냅샷 없이 버린다");
        }

        // ======== Old-to-new subscription replacement proof ========

        [Test]
        public void SubscriptionReplacement_AllRefs_DirectRebindWithoutUnsubscribe()
        {
            Assert.AreSame(fmm, SubscribedFmmField.GetValue(tracker),
                "초기 FMM 구독 확인");
            Assert.AreSame(queue, SubscribedQueueField.GetValue(tracker),
                "초기 Queue 구독 확인");
            Assert.AreSame(im, SubscribedImField.GetValue(tracker),
                "초기 IM 구독 확인");

            var newFmmGo = new GameObject("NewFMM");
            newFmmGo.SetActive(false);
            var newFmm = newFmmGo.AddComponent<FieldModeManager>();
            newFmmGo.SetActive(true);
            created.Add(newFmmGo);

            var newQueueGo = new GameObject("NewQueue");
            newQueueGo.SetActive(false);
            var newQueue = newQueueGo.AddComponent<MonsterEncounterQueue>();
            created.Add(newQueueGo);

            var newImGo = new GameObject("NewIM");
            newImGo.SetActive(false);
            var newIm = newImGo.AddComponent<InventoryManager>();
            created.Add(newImGo);

            FmmField.SetValue(tracker, newFmm);
            QueueField.SetValue(tracker, newQueue);
            ImField.SetValue(tracker, newIm);
            SubscribeMethod.Invoke(tracker, null);

            Assert.AreSame(newFmm, SubscribedFmmField.GetValue(tracker),
                "Subscribe 후 새 FMM에 구독됨");
            Assert.AreSame(newQueue, SubscribedQueueField.GetValue(tracker),
                "Subscribe 후 새 Queue에 구독됨");
            Assert.AreSame(newIm, SubscribedImField.GetValue(tracker),
                "Subscribe 후 새 IM에 구독됨");
        }

        [Test]
        public void SubscriptionReplacement_OldFMM_EventIgnored_NewFMM_EventWorks()
        {
            var newFmmGo = new GameObject("NewFMM2");
            newFmmGo.SetActive(false);
            var newFmm = newFmmGo.AddComponent<FieldModeManager>();
            newFmmGo.SetActive(true);
            created.Add(newFmmGo);

            FmmField.SetValue(tracker, newFmm);
            SubscribeMethod.Invoke(tracker, null);

            DungeonDefinition d1 = MakeDungeon("old_fmm_event");
            ResetFrameLock();
            fmm.TryEnterDungeon(d1);
            Assert.IsFalse(tracker.HasActiveSession,
                "이전 FMM의 이벤트는 트래커에 도달하지 않는다");

            DungeonDefinition d2 = MakeDungeon("new_fmm_event");
            LastTransitionFrame.SetValue(newFmm, -1);
            newFmm.TryEnterDungeon(d2);
            Assert.IsTrue(tracker.HasActiveSession,
                "새 FMM의 이벤트는 트래커에 도달한다");
        }

        // ======== Additive scene smoke ========

        [Test]
        public void Scene_ExactlyOneTracker_RefsAreExactSceneComponents()
        {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
                "Assets/Scenes/desktopScene_ReSize.unity",
                UnityEditor.SceneManagement.OpenSceneMode.Additive);
            try
            {
                var allTrackers = UnityEngine.Object.FindObjectsOfType<DungeonSessionTracker>();
                var sceneTrackers = System.Array.FindAll(allTrackers,
                    t => t.gameObject.scene == scene);
                Assert.AreEqual(1, sceneTrackers.Length,
                    "씬에 DungeonSessionTracker가 정확히 1개여야 한다");

                DungeonSessionTracker sceneTracker = sceneTrackers[0];
                var sceneFmm = (FieldModeManager)FmmField.GetValue(sceneTracker);
                var sceneQueue = (MonsterEncounterQueue)QueueField.GetValue(sceneTracker);
                var sceneIm = (InventoryManager)ImField.GetValue(sceneTracker);

                Assert.IsNotNull(sceneFmm, "fieldModeManager 참조가 연결되어야 한다");
                Assert.IsNotNull(sceneQueue, "encounterQueue 참조가 연결되어야 한다");
                Assert.IsNotNull(sceneIm, "inventoryManager 참조가 연결되어야 한다");

                Assert.AreSame(sceneTracker.gameObject, sceneFmm.gameObject,
                    "fieldModeManager는 트래커와 같은 GameObject에 있어야 한다");
                Assert.AreEqual(scene, sceneFmm.gameObject.scene,
                    "fieldModeManager는 같은 씬의 컴포넌트여야 한다");
                Assert.AreEqual(scene, sceneQueue.gameObject.scene,
                    "encounterQueue는 같은 씬의 컴포넌트여야 한다");
                Assert.AreEqual(scene, sceneIm.gameObject.scene,
                    "inventoryManager는 같은 씬의 컴포넌트여야 한다");

                Assert.AreSame(
                    sceneTracker.gameObject.GetComponent<FieldModeManager>(), sceneFmm,
                    "fieldModeManager는 FieldSystem의 정확한 FieldModeManager 인스턴스여야 한다");
            }
            finally
            {
                UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true);
            }
        }

        // ======== Subscription via real events ========

        [Test]
        public void RealFieldModeEvent_StartsAndCompletes()
        {
            DungeonDefinition d = MakeDungeon("forest_01");
            EnterDungeon(d);
            Assert.IsTrue(tracker.HasActiveSession,
                "FieldModeManager.TryEnterDungeon 이벤트로 세션이 시작된다");

            ReturnToTown();
            Assert.IsFalse(tracker.HasActiveSession);
            Assert.AreEqual(1, tracker.PendingCompletedSessionCount);
        }

        // ======== Helpers ========

        private void InvokeFieldMode(FieldMode mode, DungeonDefinition dungeon)
        {
            SetFmmState(mode, dungeon);
            HandleFieldMode.Invoke(tracker, new object[] { mode, dungeon });
        }

        private void InvokeDefeat(MonsterDefinition monster)
        {
            HandleDefeat.Invoke(tracker, new object[] { monster });
        }

        private void InvokeReward(InventoryRewardApplyResult result)
        {
            HandleReward.Invoke(tracker, new object[] { result });
        }

        private void SetFmmState(FieldMode mode, DungeonDefinition dungeon)
        {
            CurrentModeField.SetValue(fmm, mode);
            CurrentDungeonField.SetValue(fmm, dungeon);
        }

        private void SetFmmDungeon(DungeonDefinition dungeon)
        {
            CurrentDungeonField.SetValue(fmm, dungeon);
        }

        private void ResetFrameLock()
        {
            LastTransitionFrame.SetValue(fmm, -1);
        }

        private void SimulateDisable()
        {
            UnsubscribeMethod.Invoke(tracker, null);
        }

        private void SimulateEnable()
        {
            SubscribeMethod.Invoke(tracker, null);
            ResyncMethod.Invoke(tracker, null);
        }

        private void EnterDungeon(DungeonDefinition dungeon)
        {
            ResetFrameLock();
            bool ok = fmm.TryEnterDungeon(dungeon);
            Assert.IsTrue(ok, "TryEnterDungeon이 성공해야 한다");
        }

        private void ReturnToTown()
        {
            ResetFrameLock();
            bool ok = fmm.TryReturnToTown();
            Assert.IsTrue(ok, "TryReturnToTown이 성공해야 한다");
        }

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
    }
}
