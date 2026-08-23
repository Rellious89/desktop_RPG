using System;
using System.Collections.Generic;
using Common;

namespace Recruitment
{
    /// <summary>건물에 연결된 현재 모집 주기의 파생 상태.</summary>
    public enum RecruitmentCyclePhase
    {
        /// <summary>건설 기록이 없거나 아직 완성 시각 전이다.</summary>
        Locked,

        /// <summary>건물은 완성됐고 창구도 유효하지만 아직 주기 기록이 없다.</summary>
        NotInitialized,

        /// <summary>주기는 있으나 READY 경계 시각 전이다.</summary>
        Waiting,

        /// <summary>READY 경계 시각과 같거나 지났다. 조회해도 다음 주기를 시작하지 않는다.</summary>
        Ready,

        /// <summary>저장 시각이나 RecruitmentAccess 연결을 읽을 수 없다.</summary>
        Unreadable,
    }

    /// <summary>한 건물의 모집 주기를 읽기만 한 결과.</summary>
    public readonly struct RecruitmentCycleStatus
    {
        internal RecruitmentCycleStatus(
            RecruitmentCyclePhase phase,
            TimeSpan remaining,
            RecruitmentCycleSaveState state,
            RecruitmentAccessResolution access)
        {
            Phase = phase;
            Remaining = remaining;
            State = state;
            Access = access;
        }

        public RecruitmentCyclePhase Phase { get; }

        /// <summary>READY까지 남은 시간. Waiting이 아니면 항상 0이다.</summary>
        public TimeSpan Remaining { get; }

        /// <summary>판정에 사용한 저장 기록. 기록이 없으면 null이다.</summary>
        public RecruitmentCycleSaveState State { get; }

        /// <summary>건물에서 조회한 RecruitmentAccess 연결 결과.</summary>
        public RecruitmentAccessResolution Access { get; }
    }

    /// <summary>최초 모집 주기 초기화 명령의 결과.</summary>
    public enum RecruitmentCycleInitializeCode
    {
        Initialized,
        Locked,
        AlreadyInitialized,
        Unreadable,
        NoSaveData,
        SaveFailed,
        Reentrant,
    }

    /// <summary>최초 모집 주기 초기화 결과와, 성공 시 저장된 항목.</summary>
    public readonly struct RecruitmentCycleInitializeResult
    {
        internal RecruitmentCycleInitializeResult(
            RecruitmentCycleInitializeCode code, RecruitmentCycleSaveState state)
        {
            Code = code;
            State = state;
        }

        public RecruitmentCycleInitializeCode Code { get; }
        public RecruitmentCycleSaveState State { get; }
        public bool Success => Code == RecruitmentCycleInitializeCode.Initialized;
    }

    /// <summary>
    /// 건물 완성과 RecruitmentAccess를 모집 방문 주기에 연결하는 순수 C# 서비스. UI, 씬 수명주기,
    /// Unity 시간에 의존하지 않으며, 조회는 저장 문서와 파일을 전혀 바꾸지 않는다.
    ///
    /// 최초 주기는 명시적인 <see cref="TryInitialize"/> 호출로만 생성된다. 시작 시각은 호출 시각이
    /// 아니라 건설 기록의 completeAtUtc이고, READY가 된 뒤에도 조회만으로 기록을 지우거나 다음 주기를
    /// 만들지 않는다.
    /// </summary>
    public sealed class RecruitmentCycleService
    {
        private readonly Func<SaveData> dataProvider;
        private readonly Func<bool> saveAction;
        private readonly Func<DateTime> utcNowProvider;
        private readonly RecruitmentAccessCatalog accessCatalog;
        private readonly RecruitmentTypeCatalog typeCatalog;

        private bool initializing;

        public RecruitmentCycleService(
            Func<SaveData> dataProvider,
            Func<bool> saveAction,
            Func<DateTime> utcNowProvider,
            RecruitmentAccessCatalog accessCatalog,
            RecruitmentTypeCatalog typeCatalog)
        {
            this.dataProvider = dataProvider ?? throw new ArgumentNullException(nameof(dataProvider));
            this.saveAction = saveAction ?? throw new ArgumentNullException(nameof(saveAction));
            this.utcNowProvider = utcNowProvider ?? throw new ArgumentNullException(nameof(utcNowProvider));
            this.accessCatalog = accessCatalog;
            this.typeCatalog = typeCatalog;
        }

        /// <summary>
        /// 현재 상태와 남은 시간을 파생한다. 목록이 null이어도 만들지 않으며 저장을 호출하지 않는다.
        /// 건물이 완성되기 전에는 Access 연결보다 잠금 상태가 우선한다.
        /// </summary>
        public RecruitmentCycleStatus GetStatus(string buildingId)
        {
            SaveData data = dataProvider();
            if (data == null)
            {
                return Status(RecruitmentCyclePhase.Unreadable);
            }

            BuildingConstructionSaveState construction = FindConstruction(data, buildingId);
            if (construction == null)
            {
                return Status(RecruitmentCyclePhase.Locked);
            }

            if (!SaveData.TryParseTimestamp(construction.completeAtUtc, out DateTime completeAtUtc))
            {
                return Status(RecruitmentCyclePhase.Unreadable);
            }

            DateTime nowUtc = ToUtc(utcNowProvider());
            if (nowUtc < completeAtUtc)
            {
                return Status(RecruitmentCyclePhase.Locked);
            }

            RecruitmentAccessResolution access =
                RecruitmentAccessResolver.ResolveForBuilding(buildingId, accessCatalog, typeCatalog);
            if (!access.IsResolved || access.Access == null ||
                string.IsNullOrEmpty(access.Access.RecruitmentAccessId))
            {
                return Status(RecruitmentCyclePhase.Unreadable, access: access);
            }

            RecruitmentCycleSaveState state =
                FindCycle(data, access.Access.RecruitmentAccessId);
            if (state == null)
            {
                return Status(RecruitmentCyclePhase.NotInitialized, access: access);
            }

            if (!TryReadCycle(state, out DateTime startedAtUtc, out DateTime readyAtUtc) ||
                readyAtUtc < startedAtUtc)
            {
                return Status(RecruitmentCyclePhase.Unreadable, state, access);
            }

            return nowUtc < readyAtUtc
                ? Status(RecruitmentCyclePhase.Waiting, state, access, readyAtUtc - nowUtc)
                : Status(RecruitmentCyclePhase.Ready, state, access);
        }

        /// <summary>
        /// 완성된 건물에 최초 주기 한 건을 생성하고 저장을 정확히 한 번 호출한다. 이미 같은 Access Id의
        /// 기록이 있으면 값이 손상됐더라도 덮어쓰지 않고 저장하지 않는다. 저장 실패 시 목록 모양과
        /// 저장 메타데이터를 호출 전 그대로 복구한다.
        /// </summary>
        public RecruitmentCycleInitializeResult TryInitialize(string buildingId)
        {
            if (initializing)
            {
                return Result(RecruitmentCycleInitializeCode.Reentrant);
            }

            initializing = true;
            try
            {
                return InitializeInternal(buildingId);
            }
            finally
            {
                initializing = false;
            }
        }

        private RecruitmentCycleInitializeResult InitializeInternal(string buildingId)
        {
            SaveData data = dataProvider();
            if (data == null) return Result(RecruitmentCycleInitializeCode.NoSaveData);

            BuildingConstructionSaveState construction = FindConstruction(data, buildingId);
            if (construction == null) return Result(RecruitmentCycleInitializeCode.Locked);

            if (!SaveData.TryParseTimestamp(construction.completeAtUtc, out DateTime completeAtUtc))
            {
                return Result(RecruitmentCycleInitializeCode.Unreadable);
            }

            if (ToUtc(utcNowProvider()) < completeAtUtc)
            {
                return Result(RecruitmentCycleInitializeCode.Locked);
            }

            RecruitmentAccessResolution access =
                RecruitmentAccessResolver.ResolveForBuilding(buildingId, accessCatalog, typeCatalog);
            if (!access.IsResolved || access.Access == null ||
                string.IsNullOrEmpty(access.Access.RecruitmentAccessId))
            {
                return Result(RecruitmentCycleInitializeCode.Unreadable);
            }

            string accessId = access.Access.RecruitmentAccessId;
            RecruitmentCycleSaveState existing = FindCycle(data, accessId);
            if (existing != null)
            {
                return Result(RecruitmentCycleInitializeCode.AlreadyInitialized, existing);
            }

            var state = new RecruitmentCycleSaveState
            {
                recruitmentAccessId = accessId,
                startedAtUtc = SaveData.FormatTimestamp(completeAtUtc),
                readyAtUtc = SaveData.FormatTimestamp(
                    AddSeconds(completeAtUtc, access.Access.ArrivalIntervalSeconds)),
            };

            List<RecruitmentCycleSaveState> originalCycles = data.recruitmentCycles;
            if (data.recruitmentCycles == null)
            {
                data.recruitmentCycles = new List<RecruitmentCycleSaveState>();
            }
            data.recruitmentCycles.Add(state);

            SaveMetadataSnapshot metadata = SaveMetadataSnapshot.Capture(data);
            bool saved;
            try
            {
                saved = saveAction();
            }
            catch
            {
                Rollback(data, originalCycles, state, metadata);
                throw;
            }

            if (!saved)
            {
                Rollback(data, originalCycles, state, metadata);
                return Result(RecruitmentCycleInitializeCode.SaveFailed);
            }

            return Result(RecruitmentCycleInitializeCode.Initialized, state);
        }

        private static BuildingConstructionSaveState FindConstruction(SaveData data, string buildingId)
        {
            if (string.IsNullOrEmpty(buildingId)) return null;
            List<BuildingConstructionSaveState> states = data.buildingConstructions;
            if (states == null) return null;

            for (int i = 0; i < states.Count; i++)
            {
                BuildingConstructionSaveState state = states[i];
                if (state != null && string.Equals(state.buildingId, buildingId, StringComparison.Ordinal))
                {
                    return state;
                }
            }
            return null;
        }

        private static RecruitmentCycleSaveState FindCycle(SaveData data, string accessId)
        {
            List<RecruitmentCycleSaveState> states = data.recruitmentCycles;
            if (states == null) return null;

            for (int i = 0; i < states.Count; i++)
            {
                RecruitmentCycleSaveState state = states[i];
                if (state != null && string.Equals(
                        state.recruitmentAccessId, accessId, StringComparison.Ordinal))
                {
                    return state;
                }
            }
            return null;
        }

        private static bool TryReadCycle(
            RecruitmentCycleSaveState state, out DateTime startedAtUtc, out DateTime readyAtUtc)
        {
            bool startedReadable = SaveData.TryParseTimestamp(state.startedAtUtc, out startedAtUtc);
            bool readyReadable = SaveData.TryParseTimestamp(state.readyAtUtc, out readyAtUtc);
            return startedReadable && readyReadable;
        }

        private static RecruitmentCycleStatus Status(
            RecruitmentCyclePhase phase,
            RecruitmentCycleSaveState state = null,
            RecruitmentAccessResolution? access = null,
            TimeSpan remaining = default)
        {
            return new RecruitmentCycleStatus(
                phase,
                remaining,
                state,
                access ?? RecruitmentAccessResolution.Failed(RecruitmentAccessOutcome.InvalidSource));
        }

        private static RecruitmentCycleInitializeResult Result(
            RecruitmentCycleInitializeCode code, RecruitmentCycleSaveState state = null)
        {
            return new RecruitmentCycleInitializeResult(code, state);
        }

        private static void Rollback(
            SaveData data,
            List<RecruitmentCycleSaveState> originalCycles,
            RecruitmentCycleSaveState added,
            SaveMetadataSnapshot metadata)
        {
            if (originalCycles == null)
            {
                data.recruitmentCycles = null;
            }
            else
            {
                originalCycles.Remove(added);
                data.recruitmentCycles = originalCycles;
            }
            SaveData.RestoreMetadata(data, metadata);
        }

        private static DateTime ToUtc(DateTime value)
        {
            if (value.Kind == DateTimeKind.Local) return value.ToUniversalTime();
            if (value.Kind == DateTimeKind.Unspecified) return DateTime.SpecifyKind(value, DateTimeKind.Utc);
            return value;
        }

        private static DateTime AddSeconds(DateTime startedAtUtc, int seconds)
        {
            if (seconds <= 0) return startedAtUtc;
            double remaining = (DateTime.MaxValue - startedAtUtc).TotalSeconds;
            return seconds >= remaining ? DateTime.MaxValue : startedAtUtc.AddSeconds(seconds);
        }
    }
}
