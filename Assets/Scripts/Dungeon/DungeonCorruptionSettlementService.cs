using System;
using System.Collections.Generic;
using Character;
using Common;
using Corruption;

namespace Dungeon
{
    public sealed class DungeonCorruptionSettlementService
    {
        private readonly CharacterCatalog characters;
        private readonly CorruptionConfigCatalog configs;
        private readonly Func<bool> save;

        public DungeonCorruptionSettlementService(CharacterCatalog characters, CorruptionConfigCatalog configs, Func<bool> save)
        {
            this.characters = characters;
            this.configs = configs;
            this.save = save;
        }

        public bool TrySettle(DungeonSessionSnapshot snapshot, SaveData data)
        {
            if (snapshot == null || data == null || snapshot.DefeatsByCharacter.Count == 0)
                return false;

            CorruptionConfigDefinition config = configs != null ? configs.Find("default") : null;
            if (config == null || snapshot.DungeonDefinition == null) return false;
            var changed = new List<Change>();
            SaveMetadataSnapshot metadata = SaveMetadataSnapshot.Capture(data);
            foreach (DungeonSessionDefeatRecord defeat in snapshot.DefeatsByCharacter)
            {
                string id = defeat.CharacterId;
                CharacterSaveState state = Find(data.characters, id);
                CharacterDefinition definition = characters != null ? characters.Find(id) : null;
                if (state == null || definition == null) continue;
                double gain = CalculateGain(defeat.DefeatedMonsterCount, snapshot.DungeonDefinition.CorruptionGainPerDefeat);
                if (gain <= 0d) continue;

                double current = Valid(state.currentCorruption) ? state.currentCorruption : 0d;
                current = Math.Max(current, definition.BaseCorruption);
                double next = Math.Min(config.MaxCorruption, current + gain);
                if (next == state.currentCorruption) continue;

                changed.Add(new Change(state, state.currentCorruption));
                state.currentCorruption = next;
            }
            if (changed.Count == 0) return false;
            try
            {
                if (save != null && save()) return true;
            }
            catch
            {
                Rollback(data, metadata, changed);
                throw;
            }

            Rollback(data, metadata, changed);
            return false;
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
        private static void Rollback(SaveData data, SaveMetadataSnapshot metadata, List<Change> changes)
        {
            foreach (var change in changes) change.State.currentCorruption = change.Old;
            SaveData.RestoreMetadata(data, metadata);
        }
        private readonly struct Change
        {
            public readonly CharacterSaveState State;
            public readonly double Old;

            public Change(CharacterSaveState state, double old)
            {
                State = state;
                Old = old;
            }
        }
    }
}
