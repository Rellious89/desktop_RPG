using System;
using System.Collections.Generic;
using Character;
using Common;
using Quest;

namespace Recruitment
{
    /// <summary>보존된 모집 후보를 실제 영입하거나 돌려보낼 때의 결과 갈래.</summary>
    public enum RecruitmentCandidateResolutionCode
    {
        Acquired,
        Returned,
        NoSaveData,
        NoPendingCandidate,
        Unreadable,
        InvalidCandidate,
        AlreadyOwned,
        SaveFailed,
        Reentrant,
    }

    /// <summary>후보 처리 저장 트랜잭션의 불변 결과.</summary>
    public readonly struct RecruitmentCandidateResolutionResult
    {
        internal RecruitmentCandidateResolutionResult(
            RecruitmentCandidateResolutionCode code, string characterId, CharacterDefinition character)
        {
            Code = code;
            CharacterId = characterId;
            Character = character;
        }

        public RecruitmentCandidateResolutionCode Code { get; }
        public string CharacterId { get; }
        public CharacterDefinition Character { get; }
        public bool Success => Code == RecruitmentCandidateResolutionCode.Acquired ||
                               Code == RecruitmentCandidateResolutionCode.Returned;
    }

    /// <summary>
    /// Result 화면에서 보존한 후보를 처리하는 저장 트랜잭션. 비용, UI, 알림은 소유하지 않으며,
    /// 성공한 한 저장 뒤에만 바깥쪽이 화면과 런타임 목록을 갱신한다.
    /// </summary>
    public sealed class RecruitmentCandidateResolutionService
    {
        private readonly Func<SaveData> dataProvider;
        private readonly Func<bool> saveAction;
        private readonly Func<DateTime> utcNowProvider;
        private readonly RecruitmentCycleService cycleService;
        private readonly CharacterCatalog characterCatalog;

        private bool resolving;

        public RecruitmentCandidateResolutionService(
            Func<SaveData> dataProvider,
            Func<bool> saveAction,
            Func<DateTime> utcNowProvider,
            RecruitmentCycleService cycleService,
            CharacterCatalog characterCatalog)
        {
            this.dataProvider = dataProvider ?? throw new ArgumentNullException(nameof(dataProvider));
            this.saveAction = saveAction ?? throw new ArgumentNullException(nameof(saveAction));
            this.utcNowProvider = utcNowProvider ?? throw new ArgumentNullException(nameof(utcNowProvider));
            this.cycleService = cycleService ?? throw new ArgumentNullException(nameof(cycleService));
            this.characterCatalog = characterCatalog;
        }

        public RecruitmentCandidateResolutionResult TryAcquire(string buildingId)
        {
            return TryResolve(() => AcquireInternal(buildingId));
        }

        public RecruitmentCandidateResolutionResult TryReturn(string buildingId)
        {
            return TryResolve(() => ReturnInternal(buildingId));
        }

        private RecruitmentCandidateResolutionResult TryResolve(Func<RecruitmentCandidateResolutionResult> action)
        {
            if (resolving) return Result(RecruitmentCandidateResolutionCode.Reentrant);

            resolving = true;
            try
            {
                return action();
            }
            finally
            {
                resolving = false;
            }
        }

        private RecruitmentCandidateResolutionResult AcquireInternal(string buildingId)
        {
            if (!TryGetPending(buildingId, out SaveData data, out RecruitmentCycleSaveState state,
                    out string pendingId, out RecruitmentCandidateResolutionCode failure))
            {
                return Result(failure);
            }

            // Find only returns the validated, enabled CharacterCatalog definition. A stale or manually edited
            // pending id must remain visible/recoverable instead of silently being consumed.
            CharacterDefinition definition = characterCatalog != null ? characterCatalog.Find(pendingId) : null;
            if (definition == null) return Result(RecruitmentCandidateResolutionCode.InvalidCandidate, pendingId);
            if (IsOwned(data.characters, pendingId)) return Result(RecruitmentCandidateResolutionCode.AlreadyOwned, pendingId, definition);

            List<CharacterSaveState> originalCharacters = data.characters;
            if (data.characters == null) data.characters = new List<CharacterSaveState>();

            var granted = new CharacterSaveState
            {
                characterId = pendingId,
                level = 1,
                currentExp = 0,
                currentStamina = definition.MaxStamina,
                currentCorruption = definition.BaseCorruption,
            };
            string oldPending = state.pendingCharacterId;
            SaveMetadataSnapshot metadata = SaveMetadataSnapshot.Capture(data);

            data.characters.Add(granted);
            state.pendingCharacterId = null;
            // 영입과 루트 수락은 같은 저장 문서의 한 트랜잭션이다. 성공한 영입 뒤에 별도 저장으로
            // 퀘스트를 열면 중간 실패에서 "보유했지만 루트 없음"이 남으므로, 여기서 함께 적용한다.
            CharacterStoryQuestMutationReceipt questReceipt = CharacterStoryQuestService.Instance != null
                ? CharacterStoryQuestService.Instance.ActivateForCharacterWithoutSave(data, pendingId, granted.level)
                : null;

            try
            {
                if (!saveAction())
                {
                    questReceipt?.Restore();
                    RollbackAcquire(data, originalCharacters, granted, state, oldPending, metadata);
                    return Result(RecruitmentCandidateResolutionCode.SaveFailed, pendingId, definition);
                }
            }
            catch
            {
                questReceipt?.Restore();
                RollbackAcquire(data, originalCharacters, granted, state, oldPending, metadata);
                throw;
            }

            CharacterStoryQuestService.Instance?.NotifyReadyAfterExternalSave(questReceipt);
            return Result(RecruitmentCandidateResolutionCode.Acquired, pendingId, definition);
        }

        private RecruitmentCandidateResolutionResult ReturnInternal(string buildingId)
        {
            if (!TryGetPending(buildingId, out SaveData data, out RecruitmentCycleSaveState state,
                    out string pendingId, out RecruitmentCandidateResolutionCode failure))
            {
                return Result(failure);
            }

            if (!SaveData.TryParseTimestamp(state.readyAtUtc, out DateTime oldReadyAtUtc))
            {
                return Result(RecruitmentCandidateResolutionCode.Unreadable, pendingId);
            }

            DateTime nowUtc = ToUtc(utcNowProvider());
            TimeSpan remaining = oldReadyAtUtc > nowUtc ? oldReadyAtUtc - nowUtc : TimeSpan.Zero;
            DateTime newReadyAtUtc = remaining <= TimeSpan.Zero
                ? oldReadyAtUtc
                : AddSeconds(nowUtc, remaining.TotalSeconds * 0.5d);

            string oldPending = state.pendingCharacterId;
            string oldReady = state.readyAtUtc;
            SaveMetadataSnapshot metadata = SaveMetadataSnapshot.Capture(data);
            state.pendingCharacterId = null;
            state.readyAtUtc = SaveData.FormatTimestamp(newReadyAtUtc);

            try
            {
                if (!saveAction())
                {
                    RollbackReturn(data, state, oldPending, oldReady, metadata);
                    return Result(RecruitmentCandidateResolutionCode.SaveFailed, pendingId);
                }
            }
            catch
            {
                RollbackReturn(data, state, oldPending, oldReady, metadata);
                throw;
            }

            return Result(RecruitmentCandidateResolutionCode.Returned, pendingId);
        }

        private bool TryGetPending(
            string buildingId,
            out SaveData data,
            out RecruitmentCycleSaveState state,
            out string pendingId,
            out RecruitmentCandidateResolutionCode failure)
        {
            data = dataProvider();
            state = null;
            pendingId = string.Empty;
            failure = RecruitmentCandidateResolutionCode.Unreadable;
            if (data == null)
            {
                failure = RecruitmentCandidateResolutionCode.NoSaveData;
                return false;
            }

            RecruitmentCycleStatus status = cycleService.GetStatus(buildingId);
            if (status.Phase == RecruitmentCyclePhase.Unreadable)
            {
                return false;
            }

            state = status.State;
            if (state == null || string.IsNullOrEmpty(state.pendingCharacterId))
            {
                failure = RecruitmentCandidateResolutionCode.NoPendingCandidate;
                return false;
            }

            pendingId = state.pendingCharacterId;
            return true;
        }

        private static bool IsOwned(List<CharacterSaveState> characters, string characterId)
        {
            if (characters == null) return false;
            for (int i = 0; i < characters.Count; i++)
            {
                CharacterSaveState state = characters[i];
                if (state != null && string.Equals(state.characterId, characterId, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private static void RollbackAcquire(
            SaveData data,
            List<CharacterSaveState> originalCharacters,
            CharacterSaveState granted,
            RecruitmentCycleSaveState state,
            string pending,
            SaveMetadataSnapshot metadata)
        {
            if (originalCharacters == null)
            {
                data.characters = null;
            }
            else
            {
                originalCharacters.Remove(granted);
                data.characters = originalCharacters;
            }
            state.pendingCharacterId = pending;
            SaveData.RestoreMetadata(data, metadata);
        }

        private static void RollbackReturn(
            SaveData data,
            RecruitmentCycleSaveState state,
            string pending,
            string readyAtUtc,
            SaveMetadataSnapshot metadata)
        {
            state.pendingCharacterId = pending;
            state.readyAtUtc = readyAtUtc;
            SaveData.RestoreMetadata(data, metadata);
        }

        private static RecruitmentCandidateResolutionResult Result(
            RecruitmentCandidateResolutionCode code,
            string characterId = "",
            CharacterDefinition character = null)
        {
            return new RecruitmentCandidateResolutionResult(code, characterId, character);
        }

        private static DateTime ToUtc(DateTime value)
        {
            if (value.Kind == DateTimeKind.Local) return value.ToUniversalTime();
            if (value.Kind == DateTimeKind.Unspecified) return DateTime.SpecifyKind(value, DateTimeKind.Utc);
            return value;
        }

        private static DateTime AddSeconds(DateTime startedAtUtc, double seconds)
        {
            if (seconds <= 0d) return startedAtUtc;
            double remaining = (DateTime.MaxValue - startedAtUtc).TotalSeconds;
            return seconds >= remaining ? DateTime.MaxValue : startedAtUtc.AddSeconds(seconds);
        }
    }
}
