using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Inventory;

namespace Dungeon
{
    /// <summary>
    /// 던전 세션 한 회의 아이템 보상 기록. <b>실제로 인벤토리에 적용된 양</b>만 담으며, 요청량과
    /// 다를 수 있다(포화 등). 같은 ItemId가 여러 번 기록되면 하나의 항목으로 합산되고,
    /// <b>첫 실제 획득 순서</b>를 유지한다.
    /// </summary>
    public sealed class DungeonSessionItemReward
    {
        public DungeonSessionItemReward(ItemDefinition itemDefinition, string itemId, long count)
        {
            ItemDefinition = itemDefinition;
            ItemId = itemId ?? string.Empty;
            Count = count;
        }

        public ItemDefinition ItemDefinition { get; }
        public string ItemId { get; }
        public long Count { get; }
    }

    /// <summary>
    /// 완료된 던전 세션의 불변 스냅샷. 생성 이후 내부 값이 바뀌지 않으며, 이후 세션이
    /// 이 스냅샷에 영향을 주지 않는다.
    /// </summary>
    public sealed class DungeonSessionSnapshot
    {
        private readonly ReadOnlyCollection<DungeonSessionItemReward> earnedItems;

        internal DungeonSessionSnapshot(
            DungeonDefinition dungeon,
            string dungeonId,
            long sessionSequence,
            long earnedCurrency,
            long defeatedMonsterCount,
            double elapsedSeconds,
            DungeonSessionItemReward[] items)
        {
            DungeonDefinition = dungeon;
            DungeonId = dungeonId ?? string.Empty;
            SessionSequence = sessionSequence;
            EarnedCurrency = earnedCurrency;
            DefeatedMonsterCount = defeatedMonsterCount;
            ElapsedSeconds = NormalizeElapsedSeconds(elapsedSeconds);
            earnedItems = Array.AsReadOnly(items ?? Array.Empty<DungeonSessionItemReward>());
        }

        public DungeonDefinition DungeonDefinition { get; }
        public string DungeonId { get; }
        public long SessionSequence { get; }
        public long EarnedCurrency { get; }
        public long DefeatedMonsterCount { get; }
        public double ElapsedSeconds { get; }
        public ReadOnlyCollection<DungeonSessionItemReward> EarnedItems => earnedItems;
        public bool IsEmpty => EarnedCurrency == 0L && DefeatedMonsterCount == 0L && earnedItems.Count == 0;

        private static double NormalizeElapsedSeconds(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0d) return 0d;
            return value;
        }
    }

    /// <summary>
    /// <see cref="DungeonSessionLedger.TryStartSession"/>의 결과.
    /// </summary>
    public enum SessionStartResult
    {
        /// <summary>새 세션이 시작되었다.</summary>
        Started,

        /// <summary>같은 던전의 세션이 이미 활성 상태라 무시했다(시퀀스 불변).</summary>
        DuplicateIgnored,

        /// <summary>다른 던전의 세션이 이미 활성 상태라 시작할 수 없다.</summary>
        ConflictWithActiveDungeon,

        /// <summary>던전 정의가 null이거나 유효하지 않다.</summary>
        InvalidDungeon,

        /// <summary>세션 시퀀스가 long.MaxValue에 도달하여 더 이상 세션을 시작할 수 없다.</summary>
        SequenceExhausted,
    }

    /// <summary>
    /// 던전 세션의 순수 모델 원장. MonoBehaviour 생명주기, SaveSystem, 이벤트 구독에
    /// 의존하지 않는다.
    ///
    /// <b>활성 세션은 최대 하나다.</b> 새 세션을 시작하면 이전 활성 세션이 있으면 충돌이고,
    /// 완료하면 불변 스냅샷이 대기열에 들어가고 활성은 비워진다.
    ///
    /// <b>완료 대기열은 FIFO다.</b> 소비(TryConsumeNextCompletedSession)는 가장 오래된 것을
    /// 하나 꺼내고, 새 세션이 이미 대기 중인 스냅샷을 덮어쓰지 않는다.
    /// </summary>
    public sealed class DungeonSessionLedger
    {
        private DungeonDefinition activeDungeon;
        private string activeDungeonId;
        private long activeCurrency;
        private long activeKills;
        private readonly List<ItemAccumulator> activeItems = new List<ItemAccumulator>();
        private readonly Dictionary<string, int> activeItemIndex =
            new Dictionary<string, int>(StringComparer.Ordinal);

        private readonly Queue<DungeonSessionSnapshot> completed = new Queue<DungeonSessionSnapshot>();
        private long lastSequence;
        private long activeSequence;

        public bool HasActiveSession => activeDungeon != null;

        public DungeonDefinition ActiveDungeon => activeDungeon;

        public string ActiveDungeonId => activeDungeonId ?? string.Empty;

        public int PendingCompletedSessionCount => completed.Count;

        // ---- 세션 생명주기 ----

        /// <summary>
        /// 새 세션을 시작한다. 유효한 <see cref="DungeonDefinition"/>이 필요하다.
        /// </summary>
        public SessionStartResult TryStartSession(DungeonDefinition dungeon)
        {
            if (dungeon == null || !dungeon.IsValid)
                return SessionStartResult.InvalidDungeon;

            if (activeDungeon != null)
            {
                if (activeDungeon == dungeon || activeDungeonId == dungeon.DungeonId)
                    return SessionStartResult.DuplicateIgnored;

                return SessionStartResult.ConflictWithActiveDungeon;
            }

            if (lastSequence == long.MaxValue)
                return SessionStartResult.SequenceExhausted;

            lastSequence++;
            activeSequence = lastSequence;

            activeDungeon = dungeon;
            activeDungeonId = dungeon.DungeonId;
            activeCurrency = 0L;
            activeKills = 0L;
            activeItems.Clear();
            activeItemIndex.Clear();

            return SessionStartResult.Started;
        }

        /// <summary>
        /// 활성 세션을 완료하고 불변 스냅샷을 대기열에 넣는다. 빈 세션도 유효한 완료다.
        /// 활성 세션이 없으면 false.
        /// </summary>
        public bool TryCompleteSession(out DungeonSessionSnapshot snapshot)
            => TryCompleteSession(0d, out snapshot);

        /// <summary>
        /// 활성 세션을 완료하고 런타임 Tracker가 측정한 경과 시간을 함께 담는다.
        /// 원장은 Unity 시간 API를 읽지 않으며 전달받은 값만 불변 스냅샷으로 복사한다.
        /// </summary>
        public bool TryCompleteSession(double elapsedSeconds, out DungeonSessionSnapshot snapshot)
        {
            if (activeDungeon == null)
            {
                snapshot = null;
                return false;
            }

            var items = new DungeonSessionItemReward[activeItems.Count];
            for (int i = 0; i < activeItems.Count; i++)
            {
                ItemAccumulator acc = activeItems[i];
                items[i] = new DungeonSessionItemReward(acc.ItemDefinition, acc.ItemId, acc.Count);
            }

            snapshot = new DungeonSessionSnapshot(
                activeDungeon, activeDungeonId, activeSequence,
                activeCurrency, activeKills, elapsedSeconds, items);

            completed.Enqueue(snapshot);
            ClearActive();

            return true;
        }

        /// <summary>
        /// 활성 세션을 스냅샷 없이 버린다. 활성 세션이 없으면 false.
        /// </summary>
        public bool TryAbandonSession()
        {
            if (activeDungeon == null) return false;
            ClearActive();
            return true;
        }

        // ---- 기록 ----

        /// <summary>
        /// 몬스터 처치를 기록한다. 유효한 <see cref="MonsterDefinition"/>만 받으며,
        /// 활성 세션이 없으면 무시한다.
        /// </summary>
        public void RecordDefeat(MonsterDefinition monster)
        {
            if (activeDungeon == null) return;
            if (monster == null || !monster.IsValid) return;

            if (activeKills < long.MaxValue) activeKills++;
        }

        /// <summary>
        /// <see cref="InventoryRewardApplyResult"/>의 실제 양수 재화와 아이템을 기록한다.
        /// 활성 세션이 없거나 결과가 비었으면 무시한다.
        /// </summary>
        public void RecordReward(InventoryRewardApplyResult result)
        {
            if (activeDungeon == null) return;
            if (result == null || result.IsEmpty) return;

            if (result.ActualCurrencyDelta > 0)
            {
                long sum = activeCurrency + result.ActualCurrencyDelta;
                activeCurrency = sum < activeCurrency ? long.MaxValue : sum;
            }

            for (int i = 0; i < result.ItemDeltas.Count; i++)
            {
                InventoryRewardItemDelta delta = result.ItemDeltas[i];
                if (delta.ActualCount <= 0) continue;

                string id = delta.ItemId;
                if (string.IsNullOrEmpty(id)) continue;

                if (activeItemIndex.TryGetValue(id, out int idx))
                {
                    ItemAccumulator existing = activeItems[idx];
                    long sum = existing.Count + delta.ActualCount;
                    long clamped = sum < existing.Count ? long.MaxValue : sum;
                    activeItems[idx] = new ItemAccumulator(
                        existing.ItemDefinition, existing.ItemId, clamped);
                }
                else
                {
                    activeItemIndex[id] = activeItems.Count;
                    activeItems.Add(new ItemAccumulator(
                        delta.Definition, id, delta.ActualCount));
                }
            }
        }

        // ---- 대기열 조회/소비 ----

        /// <summary>가장 오래된 완료 스냅샷을 꺼내지 않고 본다. 없으면 false.</summary>
        public bool TryPeekNextCompletedSession(out DungeonSessionSnapshot snapshot)
        {
            if (completed.Count == 0)
            {
                snapshot = null;
                return false;
            }
            snapshot = completed.Peek();
            return true;
        }

        /// <summary>가장 오래된 완료 스냅샷을 꺼낸다. 한 번 꺼내면 다시 나오지 않는다.</summary>
        public bool TryConsumeNextCompletedSession(out DungeonSessionSnapshot snapshot)
        {
            if (completed.Count == 0)
            {
                snapshot = null;
                return false;
            }
            snapshot = completed.Dequeue();
            return true;
        }

        // ---- 내부 ----

        private void ClearActive()
        {
            activeDungeon = null;
            activeDungeonId = null;
            activeCurrency = 0L;
            activeKills = 0L;
            activeItems.Clear();
            activeItemIndex.Clear();
        }

        private readonly struct ItemAccumulator
        {
            public readonly ItemDefinition ItemDefinition;
            public readonly string ItemId;
            public readonly long Count;

            public ItemAccumulator(ItemDefinition itemDefinition, string itemId, long count)
            {
                ItemDefinition = itemDefinition;
                ItemId = itemId;
                Count = count;
            }
        }
    }
}
