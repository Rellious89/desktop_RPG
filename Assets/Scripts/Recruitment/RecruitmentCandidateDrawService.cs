using System;
using System.Collections.Generic;
using Common;

namespace Recruitment
{
    /// <summary>READY 모집에서 후보 하나를 저장할 때의 결과 갈래.</summary>
    public enum RecruitmentCandidateDrawCode
    {
        Selected,
        Locked,
        NotReady,
        Unreadable,
        PendingCandidateExists,
        NoEligibleCandidate,
        NoSaveData,
        SaveFailed,
        Reentrant,
    }

    /// <summary>
    /// 후보 추첨 저장 트랜잭션의 불변 결과. 후보와 Selection은 <see cref="Selected"/> 성공 때만 밖으로
    /// 내보내므로, 저장에 실패한 후보가 화면에 성공으로 보일 수 없다.
    /// </summary>
    public readonly struct RecruitmentCandidateDrawResult
    {
        internal RecruitmentCandidateDrawResult(
            RecruitmentCandidateDrawCode code,
            string characterId,
            RecruitmentSelection? selection,
            RecruitmentCycleSaveState state)
        {
            Code = code;
            CharacterId = characterId;
            Selection = selection;
            State = state;
        }

        public RecruitmentCandidateDrawCode Code { get; }
        public string CharacterId { get; }
        public RecruitmentSelection? Selection { get; }
        public RecruitmentCycleSaveState State { get; }
        public bool Success => Code == RecruitmentCandidateDrawCode.Selected;
    }

    /// <summary>
    /// READY 상태의 모집에서 후보를 하나 고르고, 후보 보존과 다음 방문 주기 시작을 <b>한 번의 저장</b>으로
    /// 확정하는 순수 서비스. 후보 실제 등록·돌려보내기·UI는 알지 않으며, 가중치/중복 규칙은 반드시
    /// <see cref="RecruitmentCandidateSelector"/> 한 곳을 사용한다.
    /// </summary>
    public sealed class RecruitmentCandidateDrawService
    {
        private readonly Func<SaveData> dataProvider;
        private readonly Func<bool> saveAction;
        private readonly Func<DateTime> utcNowProvider;
        private readonly RecruitmentCycleService cycleService;
        private readonly RecruitmentAccessCatalog accessCatalog;
        private readonly RecruitmentTypeCatalog typeCatalog;
        private readonly RecruitmentPoolCatalog poolCatalog;
        private readonly CharacterAcquisitionCatalog acquisitionCatalog;
        private readonly CharacterUnlockConditionCatalog unlockConditionCatalog;
        private readonly IRecruitmentRandom random;

        private bool drawing;

        public RecruitmentCandidateDrawService(
            Func<SaveData> dataProvider,
            Func<bool> saveAction,
            Func<DateTime> utcNowProvider,
            RecruitmentCycleService cycleService,
            RecruitmentAccessCatalog accessCatalog,
            RecruitmentTypeCatalog typeCatalog,
            RecruitmentPoolCatalog poolCatalog,
            CharacterAcquisitionCatalog acquisitionCatalog,
            IRecruitmentRandom random,
            CharacterUnlockConditionCatalog unlockConditionCatalog = null)
        {
            this.dataProvider = dataProvider ?? throw new ArgumentNullException(nameof(dataProvider));
            this.saveAction = saveAction ?? throw new ArgumentNullException(nameof(saveAction));
            this.utcNowProvider = utcNowProvider ?? throw new ArgumentNullException(nameof(utcNowProvider));
            this.cycleService = cycleService ?? throw new ArgumentNullException(nameof(cycleService));
            this.accessCatalog = accessCatalog;
            this.typeCatalog = typeCatalog;
            this.poolCatalog = poolCatalog;
            this.acquisitionCatalog = acquisitionCatalog;
            this.random = random;
            this.unlockConditionCatalog = unlockConditionCatalog;
        }

        /// <summary>
        /// 건물의 현재 주기를 다시 읽고, READY일 때만 후보와 다음 주기를 함께 저장한다. READY가 아니거나
        /// 기존 후보/유효 후보 없음인 모든 경로는 난수·저장·시각 변경 없이 끝난다.
        /// </summary>
        public RecruitmentCandidateDrawResult TryDraw(string buildingId)
        {
            if (drawing) return Result(RecruitmentCandidateDrawCode.Reentrant);

            drawing = true;
            try
            {
                return DrawInternal(buildingId);
            }
            finally
            {
                drawing = false;
            }
        }

        private RecruitmentCandidateDrawResult DrawInternal(string buildingId)
        {
            // 주기 서비스의 답이 READY 여부에 대한 권위다. 직접 시각을 다시 파싱하거나 상태를 추측하지
            // 않으므로, 9.3B의 Locked/Unreadable 경계와 정확히 같은 기준을 쓴다.
            RecruitmentCycleStatus status = cycleService.GetStatus(buildingId);
            SaveData data = dataProvider();
            if (data == null) return Result(RecruitmentCandidateDrawCode.NoSaveData);

            switch (status.Phase)
            {
                case RecruitmentCyclePhase.Locked:
                    return Result(RecruitmentCandidateDrawCode.Locked);
                case RecruitmentCyclePhase.NotInitialized:
                case RecruitmentCyclePhase.Waiting:
                    return Result(RecruitmentCandidateDrawCode.NotReady);
                case RecruitmentCyclePhase.Unreadable:
                    return Result(RecruitmentCandidateDrawCode.Unreadable);
                case RecruitmentCyclePhase.Ready:
                    break;
                default:
                    return Result(RecruitmentCandidateDrawCode.Unreadable);
            }

            RecruitmentCycleSaveState state = status.State;
            if (state == null) return Result(RecruitmentCandidateDrawCode.Unreadable);

            // 이 서비스가 받은 표도 다시 연결한다. cycleService와 다른 표 구성이 섞이면 다른 창구의
            // 간격으로 주기를 시작하는 대신 손상된 연결로 처리한다.
            RecruitmentAccessResolution access =
                RecruitmentAccessResolver.ResolveForBuilding(buildingId, accessCatalog, typeCatalog);
            if (!access.IsResolved || access.Access == null ||
                !string.Equals(status.Access.Access?.RecruitmentAccessId,
                    access.Access.RecruitmentAccessId, StringComparison.Ordinal))
            {
                return Result(RecruitmentCandidateDrawCode.Unreadable);
            }

            if (!string.IsNullOrEmpty(state.pendingCharacterId))
            {
                return Result(RecruitmentCandidateDrawCode.PendingCandidateExists);
            }

            var unlocks = new RecruitmentUnlockService(dataProvider, saveAction, acquisitionCatalog, unlockConditionCatalog);
            if (!unlocks.TryPersistCurrentUnlocks()) return Result(RecruitmentCandidateDrawCode.SaveFailed);

            RecruitmentSelection selection = RecruitmentCandidateSelector.Select(
                access.RecruitmentTypeId,
                poolCatalog,
                acquisitionCatalog,
                RecruitmentOwnership.Of(OwnedCharacterIds(data.characters)),
                random,
                unlocks.IsUnlocked);

            if (!selection.IsSelected || string.IsNullOrEmpty(selection.CharacterId))
            {
                return Result(RecruitmentCandidateDrawCode.NoEligibleCandidate);
            }

            DateTime nowUtc = ToUtc(utcNowProvider());
            string oldPendingCharacterId = state.pendingCharacterId;
            string oldStartedAtUtc = state.startedAtUtc;
            string oldReadyAtUtc = state.readyAtUtc;
            SaveMetadataSnapshot metadata = SaveMetadataSnapshot.Capture(data);

            state.pendingCharacterId = selection.CharacterId;
            state.startedAtUtc = SaveData.FormatTimestamp(nowUtc);
            state.readyAtUtc = SaveData.FormatTimestamp(
                AddSeconds(nowUtc, access.Access.ArrivalIntervalSeconds));

            bool saved;
            try
            {
                saved = saveAction();
            }
            catch
            {
                Rollback(state, oldPendingCharacterId, oldStartedAtUtc, oldReadyAtUtc, data, metadata);
                throw;
            }

            if (!saved)
            {
                Rollback(state, oldPendingCharacterId, oldStartedAtUtc, oldReadyAtUtc, data, metadata);
                return Result(RecruitmentCandidateDrawCode.SaveFailed);
            }

            return new RecruitmentCandidateDrawResult(
                RecruitmentCandidateDrawCode.Selected, selection.CharacterId, selection, state);
        }

        private static IEnumerable<string> OwnedCharacterIds(List<CharacterSaveState> characters)
        {
            if (characters == null) yield break;

            for (int i = 0; i < characters.Count; i++)
            {
                CharacterSaveState character = characters[i];
                if (character != null) yield return character.characterId;
            }
        }

        private static void Rollback(
            RecruitmentCycleSaveState state,
            string pendingCharacterId,
            string startedAtUtc,
            string readyAtUtc,
            SaveData data,
            SaveMetadataSnapshot metadata)
        {
            state.pendingCharacterId = pendingCharacterId;
            state.startedAtUtc = startedAtUtc;
            state.readyAtUtc = readyAtUtc;
            SaveData.RestoreMetadata(data, metadata);
        }

        private static RecruitmentCandidateDrawResult Result(RecruitmentCandidateDrawCode code)
        {
            return new RecruitmentCandidateDrawResult(code, string.Empty, null, null);
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
