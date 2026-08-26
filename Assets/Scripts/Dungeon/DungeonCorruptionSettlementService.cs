using System;
using System.Collections.Generic;
using Character;
using Common;
using Corruption;

namespace Dungeon
{
    public sealed class DungeonCorruptionSettlementService
    {
        public sealed class DefeatCorruptionMutationReceipt
        {
            internal DefeatCorruptionMutationReceipt(CharacterSaveState state, double before, bool changed)
            { State = state; CorruptionBefore = before; Changed = changed; }
            internal CharacterSaveState State { get; }
            public double CorruptionBefore { get; }
            public bool Changed { get; }
        }
        private readonly CharacterCatalog characters;
        private readonly CorruptionConfigCatalog configs;
        private readonly Func<bool> save;

        public DungeonCorruptionSettlementService(CharacterCatalog characters, CorruptionConfigCatalog configs, Func<bool> save)
        {
            this.characters = characters;
            this.configs = configs;
            this.save = save;
        }

        /// <summary>
        /// 유효한 한 번의 몬스터 처치에 따른 오염도를 즉시 적용하고 저장한다. 저장이 실패하거나
        /// 예외가 나면 이 처치가 바꾼 오염도와 저장 메타데이터를 모두 되돌린다.
        /// </summary>
        public bool TryApplyDefeat(DungeonDefinition dungeon, string characterId, SaveData data)
        {
            DefeatCorruptionMutationReceipt receipt = ApplyDefeatWithoutSave(dungeon, characterId, data);
            if (!receipt.Changed) return false;
            SaveMetadataSnapshot metadata = SaveMetadataSnapshot.Capture(data);
            try { if (save != null && save()) return true; }
            catch { RollbackDefeat(receipt); SaveData.RestoreMetadata(data, metadata); throw; }
            RollbackDefeat(receipt); SaveData.RestoreMetadata(data, metadata); return false;
        }

        public DefeatCorruptionMutationReceipt ApplyDefeatWithoutSave(
            DungeonDefinition dungeon, string characterId, SaveData data)
        {
            if (dungeon == null || data == null || string.IsNullOrEmpty(characterId))
                return new DefeatCorruptionMutationReceipt(null, 0d, false);

            CorruptionConfigDefinition config = configs != null ? configs.Find("default") : null;
            if (config == null) return new DefeatCorruptionMutationReceipt(null, 0d, false);

            CharacterSaveState state = Find(data.characters, characterId);
            CharacterDefinition definition = characters != null ? characters.Find(characterId) : null;
            if (state == null || definition == null) return new DefeatCorruptionMutationReceipt(null, 0d, false);

            double gain = CalculateGain(1L, dungeon.CorruptionGainPerDefeat);
            if (gain <= 0d) return new DefeatCorruptionMutationReceipt(state, state.currentCorruption, false);

            double current = Valid(state.currentCorruption) ? state.currentCorruption : 0d;
            current = Math.Max(current, definition.BaseCorruption);
            double next = Math.Min(config.MaxCorruption, current + gain);
            if (next == state.currentCorruption) return new DefeatCorruptionMutationReceipt(state, state.currentCorruption, false);
            double previous = state.currentCorruption;
            state.currentCorruption = next;
            return new DefeatCorruptionMutationReceipt(state, previous, true);
        }

        public static void RollbackDefeat(DefeatCorruptionMutationReceipt receipt)
        {
            if (receipt == null || !receipt.Changed || receipt.State == null) return;
            receipt.State.currentCorruption = receipt.CorruptionBefore;
        }

        public static double CalculateGain(long defeats, double gainPerDefeat)
        {
            if (defeats <= 0 || !Valid(gainPerDefeat) || gainPerDefeat <= 0d) return 0d;
            double result = defeats * gainPerDefeat;
            return Valid(result) ? result : double.MaxValue;
        }
        private static bool Valid(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        private static CharacterSaveState Find(List<CharacterSaveState> states, string id)
        {
            if (states == null) return null;
            foreach (CharacterSaveState state in states)
                if (state != null && string.Equals(state.characterId, id, StringComparison.Ordinal))
                    return state;
            return null;
        }
        private static void Rollback(
            SaveData data, SaveMetadataSnapshot metadata, CharacterSaveState state, double previous)
        {
            state.currentCorruption = previous;
            SaveData.RestoreMetadata(data, metadata);
        }
    }
}
