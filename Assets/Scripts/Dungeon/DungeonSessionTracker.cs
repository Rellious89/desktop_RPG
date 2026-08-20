using System;
using Enemy;
using Field;
using Inventory;
using UnityEngine;

namespace Dungeon
{
    [DisallowMultipleComponent]
    public class DungeonSessionTracker : MonoBehaviour
    {
        [SerializeField] private FieldModeManager fieldModeManager;
        [SerializeField] private MonsterEncounterQueue encounterQueue;
        [SerializeField] private InventoryManager inventoryManager;

        private readonly DungeonSessionLedger ledger = new DungeonSessionLedger();

        private FieldModeManager subscribedFmm;
        private MonsterEncounterQueue subscribedQueue;
        private InventoryManager subscribedIm;
        private Func<double> realtimeSecondsProvider;
        private double activeSessionStartedAt;
        private bool hasActiveSessionStartTime;

        public event Action<DungeonSessionSnapshot> SessionCompleted;

        public bool HasActiveSession => ledger.HasActiveSession;

        public int PendingCompletedSessionCount => ledger.PendingCompletedSessionCount;

        public bool TryPeekNextCompletedSession(out DungeonSessionSnapshot snapshot)
            => ledger.TryPeekNextCompletedSession(out snapshot);

        public bool TryConsumeNextCompletedSession(out DungeonSessionSnapshot snapshot)
            => ledger.TryConsumeNextCompletedSession(out snapshot);

        private void OnEnable()
        {
            if (realtimeSecondsProvider == null)
                realtimeSecondsProvider = ReadRealtimeSeconds;

            ResolveReferences();
            Subscribe();
            ResyncWithActualState();
        }

        private void Start()
        {
            ResolveReferences();
            Subscribe();
            ResyncWithActualState();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void ResolveReferences()
        {
            if (fieldModeManager == null)
            {
                fieldModeManager = FindObjectOfType<FieldModeManager>(true);
                if (fieldModeManager == null)
                    Debug.LogError("[DungeonSessionTracker] FieldModeManager를 찾지 못했습니다 - " +
                                   "Inspector에서 연결하세요.", this);
            }
            if (encounterQueue == null)
            {
                encounterQueue = FindObjectOfType<MonsterEncounterQueue>(true);
                if (encounterQueue == null)
                    Debug.LogError("[DungeonSessionTracker] MonsterEncounterQueue를 찾지 못했습니다 - " +
                                   "Inspector에서 연결하세요.", this);
            }
            if (inventoryManager == null)
            {
                inventoryManager = FindObjectOfType<InventoryManager>(true);
                if (inventoryManager == null)
                    Debug.LogError("[DungeonSessionTracker] InventoryManager를 찾지 못했습니다 - " +
                                   "Inspector에서 연결하세요.", this);
            }
        }

        private void Subscribe()
        {
            Unsubscribe();

            if (fieldModeManager != null)
            {
                fieldModeManager.FieldModeChanged += HandleFieldModeChanged;
                subscribedFmm = fieldModeManager;
            }
            if (encounterQueue != null)
            {
                encounterQueue.MonsterDefeated += HandleMonsterDefeated;
                subscribedQueue = encounterQueue;
            }
            if (inventoryManager != null)
            {
                inventoryManager.RewardApplied += HandleRewardApplied;
                subscribedIm = inventoryManager;
            }
        }

        private void Unsubscribe()
        {
            if (subscribedFmm != null)
            {
                subscribedFmm.FieldModeChanged -= HandleFieldModeChanged;
                subscribedFmm = null;
            }
            if (subscribedQueue != null)
            {
                subscribedQueue.MonsterDefeated -= HandleMonsterDefeated;
                subscribedQueue = null;
            }
            if (subscribedIm != null)
            {
                subscribedIm.RewardApplied -= HandleRewardApplied;
                subscribedIm = null;
            }
        }

        private void ResyncWithActualState()
        {
            if (fieldModeManager == null) return;

            FieldMode mode = fieldModeManager.CurrentMode;
            DungeonDefinition dungeon = fieldModeManager.CurrentDungeon;

            if (mode == FieldMode.Dungeon)
            {
                if (dungeon == null || !dungeon.IsValid)
                {
                    if (ledger.HasActiveSession)
                    {
                        Debug.LogWarning(
                            "[DungeonSessionTracker] 재활성화 시 던전 모드이지만 유효한 던전이 없습니다 - " +
                            "활성 세션을 버립니다.", this);
                        AbandonActiveSession();
                    }
                    return;
                }

                if (ledger.HasActiveSession)
                {
                    if (string.Equals(ledger.ActiveDungeonId, dungeon.DungeonId,
                            StringComparison.Ordinal))
                        return;

                    Debug.LogWarning(
                        $"[DungeonSessionTracker] 재활성화 시 활성 던전 '{ledger.ActiveDungeonId}'이 " +
                        $"현재 던전 '{dungeon.DungeonId}'과 다릅니다 - 이전 세션을 버리고 새 세션을 " +
                        "시작합니다.", this);
                    AbandonActiveSession();
                }

                BeginSession(dungeon);
            }
            else if (mode == FieldMode.Town)
            {
                if (dungeon != null)
                {
                    Debug.LogWarning(
                        "[DungeonSessionTracker] 재활성화 시 마을인데 던전 참조가 있습니다 - " +
                        "비정상 상태이므로 활성 세션을 버립니다.", this);
                    if (ledger.HasActiveSession)
                        AbandonActiveSession();
                    return;
                }
                if (ledger.HasActiveSession)
                {
                    Debug.LogWarning(
                        "[DungeonSessionTracker] 재활성화 시 마을 상태이지만 활성 세션이 남아 있습니다 - " +
                        "비활성 동안 마을 복귀가 발생한 것으로 보고 세션을 버립니다.", this);
                    AbandonActiveSession();
                }
            }
            else
            {
                Debug.LogWarning(
                    $"[DungeonSessionTracker] 재활성화 시 지원하지 않는 모드 '{mode}' - " +
                    "활성 세션을 버립니다.", this);
                if (ledger.HasActiveSession)
                    AbandonActiveSession();
            }
        }

        private void HandleFieldModeChanged(FieldMode mode, DungeonDefinition dungeon)
        {
            if (mode == FieldMode.Dungeon)
            {
                if (dungeon == null || !dungeon.IsValid)
                {
                    if (ledger.HasActiveSession)
                    {
                        Debug.LogWarning(
                            "[DungeonSessionTracker] 던전 전환 이벤트에 유효한 던전이 없습니다 - " +
                            "활성 세션을 버립니다.", this);
                        AbandonActiveSession();
                    }
                    Debug.LogError(
                        "[DungeonSessionTracker] 유효하지 않은 던전으로 세션을 시작할 수 없습니다.",
                        this);
                    return;
                }

                if (ledger.HasActiveSession &&
                    !string.Equals(ledger.ActiveDungeonId, dungeon.DungeonId,
                        StringComparison.Ordinal))
                {
                    Debug.LogWarning(
                        $"[DungeonSessionTracker] 다른 던전 '{dungeon.DungeonId}'으로 전환합니다 - " +
                        $"활성 세션 '{ledger.ActiveDungeonId}'을 버리고 새 세션을 시작합니다.", this);
                    AbandonActiveSession();
                }

                BeginSession(dungeon);
            }
            else if (mode == FieldMode.Town)
            {
                if (dungeon != null)
                {
                    Debug.LogWarning(
                        "[DungeonSessionTracker] 마을 전환 이벤트에 던전이 포함되어 있습니다 - " +
                        "비정상 상태이므로 활성 세션을 버립니다.", this);
                    if (ledger.HasActiveSession)
                        AbandonActiveSession();
                    return;
                }
                CompleteActiveSession();
            }
            else
            {
                Debug.LogWarning(
                    $"[DungeonSessionTracker] 지원하지 않는 모드 '{mode}' 수신 - " +
                    "활성 세션을 버립니다.", this);
                if (ledger.HasActiveSession)
                    AbandonActiveSession();
            }
        }

        private void BeginSession(DungeonDefinition dungeon)
        {
            SessionStartResult result = ledger.TryStartSession(dungeon);
            switch (result)
            {
                case SessionStartResult.Started:
                    activeSessionStartedAt = GetRealtimeSeconds();
                    hasActiveSessionStartTime = true;
                    break;
                case SessionStartResult.DuplicateIgnored:
                    break;
                case SessionStartResult.SequenceExhausted:
                    Debug.LogError(
                        "[DungeonSessionTracker] 세션 시퀀스가 소진되어 새 세션을 시작할 수 없습니다.",
                        this);
                    break;
            }
        }

        private void CompleteActiveSession()
        {
            if (!ledger.HasActiveSession) return;

            double elapsedSeconds = hasActiveSessionStartTime
                ? Math.Max(0d, GetRealtimeSeconds() - activeSessionStartedAt)
                : 0d;

            if (ledger.TryCompleteSession(elapsedSeconds, out DungeonSessionSnapshot snapshot))
            {
                ClearActiveSessionTimer();
                SessionCompleted?.Invoke(snapshot);
            }
        }

        private void HandleMonsterDefeated(MonsterDefinition monster)
        {
            if (!IsRecordingAligned()) return;
            ledger.RecordDefeat(monster);
        }

        private void HandleRewardApplied(InventoryRewardApplyResult result)
        {
            if (!IsRecordingAligned()) return;
            if (result == null || result.IsEmpty) return;
            ledger.RecordReward(result);
        }

        private bool IsRecordingAligned()
        {
            if (!ledger.HasActiveSession) return false;
            if (fieldModeManager == null) return false;
            if (fieldModeManager.CurrentMode != FieldMode.Dungeon) return false;

            DungeonDefinition current = fieldModeManager.CurrentDungeon;
            if (current == null || !current.IsValid) return false;

            return string.Equals(
                ledger.ActiveDungeonId, current.DungeonId, StringComparison.Ordinal);
        }

        private void AbandonActiveSession()
        {
            ledger.TryAbandonSession();
            ClearActiveSessionTimer();
        }

        private void ClearActiveSessionTimer()
        {
            activeSessionStartedAt = 0d;
            hasActiveSessionStartTime = false;
        }

        private double GetRealtimeSeconds()
        {
            double value = realtimeSecondsProvider != null
                ? realtimeSecondsProvider()
                : ReadRealtimeSeconds();
            return double.IsNaN(value) || double.IsInfinity(value) ? 0d : value;
        }

        private static double ReadRealtimeSeconds() => Time.realtimeSinceStartupAsDouble;
    }
}
