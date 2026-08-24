using System;
using System.Collections.Generic;
using Character;
using Common;
using Party;
using Recovery;

namespace Corruption
{
    public enum PurificationResultCode
    {
        Success, NoSaveData, ConfigurationMissing, ConfigurationInvalid, RequiredBuildingUnavailable,
        InvalidCharacter, NotOwned, InvalidSlot, SlotOccupied, AlreadyInPurification, InRecovery,
        MinimumPartySize, NothingToStop, SaveFailed, Reentrant,
    }

    public readonly struct PurificationResult
    {
        internal PurificationResult(PurificationResultCode code, int slotIndex, int settledCount = 0)
        { Code = code; SlotIndex = slotIndex; SettledCount = settledCount; }
        public PurificationResultCode Code { get; }
        public int SlotIndex { get; }
        public int SettledCount { get; }
        public bool Success => Code == PurificationResultCode.Success;
    }

    /// <summary>SaveData v6 정화 슬롯의 등록, UTC 정산, 중단을 한 번의 저장 트랜잭션으로 처리한다.</summary>
    public sealed class PurificationService
    {
        private const int MinimumPartyCount = 1;
        private readonly Func<SaveData> dataProvider;
        private readonly Func<bool> saveAction;
        private readonly Func<DateTime> utcNowProvider;
        private readonly CharacterCatalog characterCatalog;
        private readonly PurificationConfigCatalog configCatalog;
        private readonly Func<string, bool> buildingCompletedProvider;
        private bool changing;

        public PurificationService(Func<SaveData> dataProvider, Func<bool> saveAction, Func<DateTime> utcNowProvider,
                                  CharacterCatalog characterCatalog, PurificationConfigCatalog configCatalog,
                                  Func<string, bool> buildingCompletedProvider)
        {
            this.dataProvider = dataProvider ?? throw new ArgumentNullException(nameof(dataProvider));
            this.saveAction = saveAction ?? throw new ArgumentNullException(nameof(saveAction));
            this.utcNowProvider = utcNowProvider ?? throw new ArgumentNullException(nameof(utcNowProvider));
            this.characterCatalog = characterCatalog;
            this.configCatalog = configCatalog;
            this.buildingCompletedProvider = buildingCompletedProvider ?? throw new ArgumentNullException(nameof(buildingCompletedProvider));
        }

        public PurificationResult TryRegister(string purificationTypeId, string characterId, int slotIndex)
        {
            return Change(() => RegisterInternal(purificationTypeId, characterId, slotIndex));
        }

        public PurificationResult TryRegister(int slotIndex, string characterId, string purificationTypeId)
        {
            return TryRegister(purificationTypeId, characterId, slotIndex);
        }

        public PurificationResult TryStart(string purificationTypeId, string characterId, int slotIndex)
        {
            return TryRegister(purificationTypeId, characterId, slotIndex);
        }

        public PurificationResult TryStop(int slotIndex) => Change(() => StopInternal(slotIndex));
        public PurificationResult Tick() => Change(TickInternal);

        /// <summary>저장된 정화 슬롯만으로 판정한다. 씬 서비스가 아직 없을 때도 동일한 답을 낸다.</summary>
        public static bool IsCharacterIdInSavedSlot(SaveData data, string characterId)
        {
            if (data == null || data.purificationSlots == null || string.IsNullOrEmpty(characterId)) return false;
            for (int i = 0; i < data.purificationSlots.Count; i++)
                if (data.purificationSlots[i] != null && string.Equals(data.purificationSlots[i].characterId, characterId, StringComparison.Ordinal)) return true;
            return false;
        }

        private PurificationResult Change(Func<PurificationResult> action)
        {
            if (changing) return Result(PurificationResultCode.Reentrant, -1);
            changing = true;
            try { return action(); }
            finally { changing = false; }
        }

        private PurificationResult RegisterInternal(string typeId, string characterId, int slotIndex)
        {
            SaveData data = dataProvider();
            if (data == null) return Result(PurificationResultCode.NoSaveData, slotIndex);
            PurificationConfigDefinition config = ResolveConfig(typeId, out PurificationResultCode configFailure);
            if (config == null) return Result(configFailure, slotIndex);
            if (slotIndex < 0 || slotIndex >= config.BaseSlotCount) return Result(PurificationResultCode.InvalidSlot, slotIndex);
            if (!IsBuildingCompleted(config.RequiredBuildingId)) return Result(PurificationResultCode.RequiredBuildingUnavailable, slotIndex);
            if (string.IsNullOrEmpty(characterId) || characterCatalog == null || characterCatalog.Find(characterId) == null)
                return Result(PurificationResultCode.InvalidCharacter, slotIndex);
            CharacterSaveState state = FindOwned(data.characters, characterId);
            if (state == null) return Result(PurificationResultCode.NotOwned, slotIndex);
            if (RecoveryStation.IsCharacterIdInSavedSlot(data, characterId)) return Result(PurificationResultCode.InRecovery, slotIndex);
            if (IsCharacterIdInSavedSlot(data, characterId)) return Result(PurificationResultCode.AlreadyInPurification, slotIndex);

            int partyIndex = PartySlotUtility.IndexOf(data.partyCharacterIds, characterId);
            if (partyIndex >= 0 && PartySlotUtility.OccupiedCount(data.partyCharacterIds) <= MinimumPartyCount)
                return Result(PurificationResultCode.MinimumPartySize, slotIndex);

            List<PurificationSlotSaveState> changedSlots = CloneSlots(data.purificationSlots);
            EnsureSlots(changedSlots, config.BaseSlotCount);
            if (changedSlots[slotIndex].HasCharacter) return Result(PurificationResultCode.SlotOccupied, slotIndex);

            DateTime now = UtcNow();
            changedSlots[slotIndex].purificationTypeId = config.PurificationTypeId;
            changedSlots[slotIndex].characterId = characterId;
            changedSlots[slotIndex].lastCalculatedAtUtc = SaveData.FormatTimestamp(now);
            changedSlots[slotIndex].progressTicks = 0;
            List<string> originalParty = data.partyCharacterIds;
            List<string> changedParty = originalParty;
            if (partyIndex >= 0)
            {
                changedParty = new List<string>(originalParty);
                changedParty[partyIndex] = string.Empty;
            }
            return Save(data, data.purificationSlots, changedSlots, originalParty, changedParty,
                        (List<CorruptionChange>)null, slotIndex, 0);
        }

        private PurificationResult StopInternal(int slotIndex)
        {
            SaveData data = dataProvider();
            if (data == null) return Result(PurificationResultCode.NoSaveData, slotIndex);
            if (slotIndex < 0 || data.purificationSlots == null || slotIndex >= data.purificationSlots.Count)
                return Result(PurificationResultCode.InvalidSlot, slotIndex);
            PurificationSlotSaveState currentSlot = data.purificationSlots[slotIndex];
            if (currentSlot == null || !currentSlot.HasCharacter) return Result(PurificationResultCode.NothingToStop, slotIndex);
            PurificationConfigDefinition config = ResolveConfig(currentSlot.purificationTypeId, out PurificationResultCode configFailure);
            if (config == null) return Result(configFailure, slotIndex);
            CharacterSaveState state = FindOwned(data.characters, currentSlot.characterId);
            CharacterDefinition definition = characterCatalog != null ? characterCatalog.Find(currentSlot.characterId) : null;
            if (state == null) return Result(PurificationResultCode.NotOwned, slotIndex);
            if (definition == null) return Result(PurificationResultCode.InvalidCharacter, slotIndex);

            List<PurificationSlotSaveState> changedSlots = CloneSlots(data.purificationSlots);
            double oldCorruption = state.currentCorruption;
            bool settled = Settle(changedSlots[slotIndex], state, definition, config, UtcNow());
            changedSlots[slotIndex].Clear();
            return Save(data, data.purificationSlots, changedSlots, data.partyCharacterIds, data.partyCharacterIds,
                        new CorruptionChange(state, oldCorruption), slotIndex, settled ? 1 : 0);
        }

        private PurificationResult TickInternal()
        {
            SaveData data = dataProvider();
            if (data == null) return Result(PurificationResultCode.NoSaveData, -1);
            if (data.purificationSlots == null || data.purificationSlots.Count == 0) return Result(PurificationResultCode.Success, -1);

            DateTime now = UtcNow();
            List<PurificationSlotSaveState> changedSlots = CloneSlots(data.purificationSlots);
            var changes = new List<CorruptionChange>();
            bool mutated = false;
            int settledCount = 0;
            for (int i = 0; i < changedSlots.Count; i++)
            {
                PurificationSlotSaveState slot = changedSlots[i];
                if (!slot.HasCharacter) continue;
                PurificationConfigDefinition config = ResolveConfig(slot.purificationTypeId, out _);
                CharacterSaveState state = FindOwned(data.characters, slot.characterId);
                CharacterDefinition definition = characterCatalog != null ? characterCatalog.Find(slot.characterId) : null;
                if (config == null || state == null || definition == null) continue;
                double before = state.currentCorruption;
                SlotSnapshot beforeSlot = new SlotSnapshot(slot);
                if (!Settle(slot, state, definition, config, now)) continue;
                TrackOriginal(changes, state, before);
                if (!beforeSlot.Equals(slot) || before != state.currentCorruption) mutated = true;
                settledCount++;
            }
            if (!mutated) return Result(PurificationResultCode.Success, -1, 0);
            return Save(data, data.purificationSlots, changedSlots, data.partyCharacterIds, data.partyCharacterIds,
                        changes, -1, settledCount);
        }

        private PurificationResult Save(SaveData data, List<PurificationSlotSaveState> originalSlots,
                                        List<PurificationSlotSaveState> changedSlots, List<string> originalParty,
                                        List<string> changedParty, CorruptionChange? singleChange, int slotIndex, int settledCount)
        {
            var changes = singleChange.HasValue ? new List<CorruptionChange> { singleChange.Value } : null;
            return Save(data, originalSlots, changedSlots, originalParty, changedParty, changes, slotIndex, settledCount);
        }

        private PurificationResult Save(SaveData data, List<PurificationSlotSaveState> originalSlots,
                                        List<PurificationSlotSaveState> changedSlots, List<string> originalParty,
                                        List<string> changedParty, List<CorruptionChange> changes, int slotIndex, int settledCount)
        {
            SaveMetadataSnapshot metadata = SaveMetadataSnapshot.Capture(data);
            data.purificationSlots = changedSlots;
            data.partyCharacterIds = changedParty;
            try
            {
                if (saveAction()) return Result(PurificationResultCode.Success, slotIndex, settledCount);
            }
            catch { }
            data.purificationSlots = originalSlots;
            data.partyCharacterIds = originalParty;
            if (changes != null) for (int i = 0; i < changes.Count; i++) changes[i].State.currentCorruption = changes[i].Old;
            SaveData.RestoreMetadata(data, metadata);
            return Result(PurificationResultCode.SaveFailed, slotIndex);
        }

        private PurificationConfigDefinition ResolveConfig(string typeId, out PurificationResultCode failure)
        {
            failure = PurificationResultCode.ConfigurationMissing;
            if (configCatalog == null || string.IsNullOrEmpty(typeId)) return null;
            PurificationConfigDefinition raw = configCatalog.FindConfigured(typeId);
            if (raw == null) return null;
            if (!raw.Enabled || !raw.IsValid) { failure = PurificationResultCode.ConfigurationInvalid; return null; }
            return raw;
        }

        private bool IsBuildingCompleted(string id)
        {
            try { return buildingCompletedProvider(id); }
            catch { return false; }
        }

        private static bool Settle(PurificationSlotSaveState slot, CharacterSaveState state, CharacterDefinition definition,
                                   PurificationConfigDefinition config, DateTime now)
        {
            SlotSnapshot before = new SlotSnapshot(slot);
            double old = state.currentCorruption;
            double floor = definition.BaseCorruption;
            if (!IsFinite(old) || old <= floor)
            {
                state.currentCorruption = floor;
                slot.lastCalculatedAtUtc = SaveData.FormatTimestamp(now);
                slot.progressTicks = 0;
                return !before.Equals(slot) || old != state.currentCorruption;
            }
            if (!SaveData.TryParseTimestamp(slot.lastCalculatedAtUtc, out DateTime last) || last > now)
            {
                slot.lastCalculatedAtUtc = SaveData.FormatTimestamp(now);
                slot.progressTicks = 0;
                return !before.Equals(slot);
            }
            long intervalTicks = (long)config.PurificationIntervalSeconds * TimeSpan.TicksPerSecond;
            if (slot.progressTicks < 0 || slot.progressTicks >= intervalTicks) slot.progressTicks = 0;
            long elapsed = now.Ticks - last.Ticks;
            long total = elapsed > long.MaxValue - slot.progressTicks ? long.MaxValue : elapsed + slot.progressTicks;
            long periods = total / intervalTicks;
            slot.lastCalculatedAtUtc = SaveData.FormatTimestamp(now);
            slot.progressTicks = total % intervalTicks;
            if (periods > 0)
            {
                double decrease = periods * (double)config.PurificationValuePerInterval;
                if (decrease >= old - floor) decrease = old - floor;
                state.currentCorruption = Math.Max(floor, old - decrease);
                if (state.currentCorruption <= floor) slot.progressTicks = 0;
            }
            return !before.Equals(slot) || old != state.currentCorruption;
        }

        private static DateTime Utc(DateTime value) => value.Kind == DateTimeKind.Local ? value.ToUniversalTime() :
            value.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(value, DateTimeKind.Utc) : value;
        private DateTime UtcNow() => Utc(utcNowProvider());
        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        private static CharacterSaveState FindOwned(List<CharacterSaveState> states, string id)
        {
            if (states == null || string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < states.Count; i++) if (states[i] != null && string.Equals(states[i].characterId, id, StringComparison.Ordinal)) return states[i];
            return null;
        }
        private static List<PurificationSlotSaveState> CloneSlots(List<PurificationSlotSaveState> slots)
        {
            var clone = new List<PurificationSlotSaveState>();
            if (slots == null) return clone;
            for (int i = 0; i < slots.Count; i++) clone.Add(slots[i] == null ? new PurificationSlotSaveState() : new SlotSnapshot(slots[i]).ToState());
            return clone;
        }
        private static void EnsureSlots(List<PurificationSlotSaveState> slots, int count)
        { while (slots.Count < count) slots.Add(new PurificationSlotSaveState()); }
        private static void TrackOriginal(List<CorruptionChange> changes, CharacterSaveState state, double value)
        {
            for (int i = 0; i < changes.Count; i++) if (ReferenceEquals(changes[i].State, state)) return;
            changes.Add(new CorruptionChange(state, value));
        }
        private static PurificationResult Result(PurificationResultCode code, int slot, int settled = 0) => new PurificationResult(code, slot, settled);

        private readonly struct CorruptionChange { public readonly CharacterSaveState State; public readonly double Old; public CorruptionChange(CharacterSaveState state, double old) { State = state; Old = old; } }
        private readonly struct SlotSnapshot
        {
            private readonly string type, character, last; private readonly long progress;
            public SlotSnapshot(PurificationSlotSaveState slot) { type = slot.purificationTypeId; character = slot.characterId; last = slot.lastCalculatedAtUtc; progress = slot.progressTicks; }
            public bool Equals(PurificationSlotSaveState slot) => type == slot.purificationTypeId && character == slot.characterId && last == slot.lastCalculatedAtUtc && progress == slot.progressTicks;
            public PurificationSlotSaveState ToState() => new PurificationSlotSaveState { purificationTypeId = type, characterId = character, lastCalculatedAtUtc = last, progressTicks = progress };
        }
    }
}
