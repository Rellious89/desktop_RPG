using System;
using System.Collections.Generic;
using Common;
using Recovery;

namespace Party
{
    public enum PartyCompositionCode
    {
        Success,
        NoSaveData,
        ConfigurationMissing,
        ConfigurationInvalid,
        InvalidCharacterId,
        NotOwned,
        AlreadyInParty,
        NotInParty,
        CapacityReached,
        MinimumPartySize,
        InRecovery,
        InvalidIndex,
        NoChange,
        SaveFailed,
        Reentrant,
        InvalidPartyData,
    }

    public readonly struct PartyCapacityResult
    {
        internal PartyCapacityResult(PartyCompositionCode code, int capacity)
        {
            Code = code;
            Capacity = capacity;
        }

        public PartyCompositionCode Code { get; }
        public int Capacity { get; }
        public bool IsAvailable => Code == PartyCompositionCode.Success;
    }

    public readonly struct PartyCompositionResult
    {
        internal PartyCompositionResult(PartyCompositionCode code, int capacity, IReadOnlyList<string> party)
        {
            Code = code;
            Capacity = capacity;
            Party = party;
        }

        public PartyCompositionCode Code { get; }
        public int Capacity { get; }
        public IReadOnlyList<string> Party { get; }
        public bool Success => Code == PartyCompositionCode.Success;
    }

    /// <summary>
    /// SaveData v4의 고정 출전 슬롯만 바꾸는 순수 저장 트랜잭션. UI, CharacterRoster, 씬 상태에는 의존하지
    /// 않으며 회복 중 판정도 저장된 RecoverySlot만 사용한다.
    /// </summary>
    public sealed class PartyCompositionService
    {
        private const int MinimumPartyCount = 1;

        private readonly Func<SaveData> dataProvider;
        private readonly Func<bool> saveAction;
        private readonly PartyConfigCatalog configCatalog;
        // 건축물 정원 보너스가 생기면 이 좁은 공급자만 연결한다. 현재 기본값은 항상 0이다.
        private readonly Func<int> capacityBonusProvider;
        private bool changing;

        public PartyCompositionService(
            Func<SaveData> dataProvider,
            Func<bool> saveAction,
            PartyConfigCatalog configCatalog,
            Func<int> capacityBonusProvider = null)
        {
            this.dataProvider = dataProvider ?? throw new ArgumentNullException(nameof(dataProvider));
            this.saveAction = saveAction ?? throw new ArgumentNullException(nameof(saveAction));
            this.configCatalog = configCatalog;
            this.capacityBonusProvider = capacityBonusProvider ?? (() => 0);
        }

        public PartyCapacityResult GetCapacity()
        {
            return ResolveCapacity();
        }

        public PartyCompositionResult TryJoin(string characterId)
        {
            return TryChange(() => JoinInternal(characterId, -1));
        }

        public PartyCompositionResult TryJoinAt(string characterId, int targetSlotIndex)
        {
            return TryChange(() => JoinInternal(characterId, targetSlotIndex));
        }

        public PartyCompositionResult TryLeave(string characterId)
        {
            return TryChange(() => LeaveInternal(characterId));
        }

        public PartyCompositionResult TryReplace(string outgoingCharacterId, string incomingCharacterId)
        {
            return TryChange(() => ReplaceInternal(outgoingCharacterId, incomingCharacterId));
        }

        public PartyCompositionResult TryMove(string characterId, int targetIndex)
        {
            return TryChange(() => MoveInternal(characterId, targetIndex));
        }

        private PartyCompositionResult TryChange(Func<PartyCompositionResult> action)
        {
            if (changing) return Result(PartyCompositionCode.Reentrant);

            changing = true;
            try { return action(); }
            finally { changing = false; }
        }

        private PartyCompositionResult JoinInternal(string characterId, int requestedSlot)
        {
            if (string.IsNullOrEmpty(characterId)) return Result(PartyCompositionCode.InvalidCharacterId);
            if (!TryGetMutable(out SaveData data, out List<string> party, out int capacity, out PartyCompositionCode failure))
                return Result(failure, capacity);
            if (!IsOwned(data.characters, characterId)) return Result(PartyCompositionCode.NotOwned, capacity, party);
            if (IndexOf(party, characterId) >= 0) return Result(PartyCompositionCode.AlreadyInParty, capacity, party);
            if (RecoveryStation.IsCharacterIdInSavedSlot(data, characterId))
                return Result(PartyCompositionCode.InRecovery, capacity, party);
            if (PartySlotUtility.OccupiedCount(party) >= capacity) return Result(PartyCompositionCode.CapacityReached, capacity, party);
            int slot = requestedSlot >= 0 ? requestedSlot : PartySlotUtility.FirstEmpty(party, capacity);
            if (slot < 0 || slot >= capacity) return Result(PartyCompositionCode.InvalidIndex, capacity, party);
            if (!string.IsNullOrEmpty(PartySlotUtility.At(party, slot))) return Result(PartyCompositionCode.InvalidIndex, capacity, party);
            var changed = new List<string>(party); PartySlotUtility.EnsureIndex(changed, slot); changed[slot] = characterId;
            return SaveChanged(data, party, changed, capacity);
        }

        private PartyCompositionResult LeaveInternal(string characterId)
        {
            if (string.IsNullOrEmpty(characterId)) return Result(PartyCompositionCode.InvalidCharacterId);
            if (!TryGetMutable(out SaveData data, out List<string> party, out int capacity, out PartyCompositionCode failure))
                return Result(failure, capacity);

            int index = IndexOf(party, characterId);
            if (index < 0) return Result(PartyCompositionCode.NotInParty, capacity, party);
            if (PartySlotUtility.OccupiedCount(party) <= MinimumPartyCount) return Result(PartyCompositionCode.MinimumPartySize, capacity, party);
            if (RecoveryStation.IsCharacterIdInSavedSlot(data, characterId))
                return Result(PartyCompositionCode.InRecovery, capacity, party);

            var changed = new List<string>(party); changed[index] = string.Empty;
            return SaveChanged(data, party, changed, capacity);
        }

        private PartyCompositionResult ReplaceInternal(string outgoingCharacterId, string incomingCharacterId)
        {
            if (string.IsNullOrEmpty(outgoingCharacterId) || string.IsNullOrEmpty(incomingCharacterId))
                return Result(PartyCompositionCode.InvalidCharacterId);
            if (!TryGetMutable(out SaveData data, out List<string> party, out int capacity, out PartyCompositionCode failure))
                return Result(failure, capacity);

            int outgoingIndex = IndexOf(party, outgoingCharacterId);
            if (outgoingIndex < 0) return Result(PartyCompositionCode.NotInParty, capacity, party);
            if (string.Equals(outgoingCharacterId, incomingCharacterId, StringComparison.Ordinal))
                return Result(PartyCompositionCode.NoChange, capacity, party);
            if (!IsOwned(data.characters, incomingCharacterId)) return Result(PartyCompositionCode.NotOwned, capacity, party);
            if (IndexOf(party, incomingCharacterId) >= 0)
                return Result(PartyCompositionCode.AlreadyInParty, capacity, party);
            if (RecoveryStation.IsCharacterIdInSavedSlot(data, outgoingCharacterId) ||
                RecoveryStation.IsCharacterIdInSavedSlot(data, incomingCharacterId))
            {
                return Result(PartyCompositionCode.InRecovery, capacity, party);
            }

            var changed = new List<string>(party);
            changed[outgoingIndex] = incomingCharacterId;
            return SaveChanged(data, party, changed, capacity);
        }

        private PartyCompositionResult MoveInternal(string characterId, int targetIndex)
        {
            if (string.IsNullOrEmpty(characterId)) return Result(PartyCompositionCode.InvalidCharacterId);
            if (!TryGetMutable(out SaveData data, out List<string> party, out int capacity, out PartyCompositionCode failure))
                return Result(failure, capacity);

            int currentIndex = IndexOf(party, characterId);
            if (currentIndex < 0) return Result(PartyCompositionCode.NotInParty, capacity, party);
            if (targetIndex < 0 || targetIndex >= capacity) return Result(PartyCompositionCode.InvalidIndex, capacity, party);
            if (targetIndex == currentIndex) return Result(PartyCompositionCode.NoChange, capacity, party);

            var changed = new List<string>(party); PartySlotUtility.EnsureIndex(changed, targetIndex);
            string target = changed[targetIndex]; changed[targetIndex] = characterId; changed[currentIndex] = target;
            return SaveChanged(data, party, changed, capacity);
        }

        private bool TryGetMutable(
            out SaveData data, out List<string> party, out int capacity, out PartyCompositionCode failure)
        {
            data = dataProvider();
            party = null;
            capacity = 0;
            failure = PartyCompositionCode.InvalidPartyData;
            if (data == null)
            {
                failure = PartyCompositionCode.NoSaveData;
                return false;
            }

            PartyCapacityResult resolved = ResolveCapacity();
            capacity = resolved.Capacity;
            if (!resolved.IsAvailable)
            {
                failure = resolved.Code;
                return false;
            }

            party = data.partyCharacterIds;
            if (!IsPartyValid(party, data.characters)) return false;
            return true;
        }

        private PartyCapacityResult ResolveCapacity()
        {
            if (configCatalog == null) return new PartyCapacityResult(PartyCompositionCode.ConfigurationMissing, 0);

            PartyConfigDefinition config = configCatalog.Find(PartyConfigIds.Default);
            if (config == null) return new PartyCapacityResult(PartyCompositionCode.ConfigurationMissing, 0);
            if (!config.Enabled || !config.IsValid)
                return new PartyCapacityResult(PartyCompositionCode.ConfigurationInvalid, 0);

            int bonus = capacityBonusProvider();
            if (bonus < 0 || config.BaseCapacity > int.MaxValue - bonus)
                return new PartyCapacityResult(PartyCompositionCode.ConfigurationInvalid, 0);
            return new PartyCapacityResult(PartyCompositionCode.Success, config.BaseCapacity + bonus);
        }

        private PartyCompositionResult SaveChanged(
            SaveData data, List<string> originalParty, List<string> changedParty, int capacity)
        {
            SaveMetadataSnapshot metadata = SaveMetadataSnapshot.Capture(data);
            data.partyCharacterIds = changedParty;
            try
            {
                if (!saveAction())
                {
                    data.partyCharacterIds = originalParty;
                    SaveData.RestoreMetadata(data, metadata);
                    return Result(PartyCompositionCode.SaveFailed, capacity, originalParty);
                }
            }
            catch
            {
                data.partyCharacterIds = originalParty;
                SaveData.RestoreMetadata(data, metadata);
                return Result(PartyCompositionCode.SaveFailed, capacity, originalParty);
            }

            return Result(PartyCompositionCode.Success, capacity, changedParty);
        }

        private static bool IsPartyValid(List<string> party, List<CharacterSaveState> characters)
        {
            if (party == null) return false;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in party)
            {
                if (string.IsNullOrEmpty(id)) continue;
                if (!seen.Add(id) || !IsOwned(characters, id)) return false;
            }

            return true;
        }

        private static bool IsOwned(List<CharacterSaveState> characters, string characterId)
        {
            if (characters == null) return false;
            foreach (CharacterSaveState state in characters)
            {
                if (state != null && string.Equals(state.characterId, characterId, StringComparison.Ordinal)) return true;
            }

            return false;
        }

        private static int IndexOf(List<string> party, string characterId) => PartySlotUtility.IndexOf(party, characterId);

        private static PartyCompositionResult Result(
            PartyCompositionCode code, int capacity = 0, IReadOnlyList<string> party = null)
        {
            return new PartyCompositionResult(code, capacity, party);
        }
    }
}
