using System;
using System.Collections.Generic;
using Common;

namespace Recruitment
{
    /// <summary>조건을 읽는 순수 평가와, 최초 달성을 저장하는 트랜잭션을 한 곳에 둔다.</summary>
    public sealed class RecruitmentUnlockService
    {
        private readonly Func<SaveData> dataProvider;
        private readonly Func<bool> saveAction;
        private readonly CharacterAcquisitionCatalog acquisitions;
        private readonly CharacterUnlockConditionCatalog conditions;

        public RecruitmentUnlockService(Func<SaveData> dataProvider, Func<bool> saveAction,
            CharacterAcquisitionCatalog acquisitions, CharacterUnlockConditionCatalog conditions)
        {
            this.dataProvider = dataProvider ?? throw new ArgumentNullException(nameof(dataProvider));
            this.saveAction = saveAction ?? throw new ArgumentNullException(nameof(saveAction));
            this.acquisitions = acquisitions; this.conditions = conditions;
        }

        public bool IsUnlocked(string characterId)
        {
            SaveData data = dataProvider();
            return data != null && data.unlockedRecruitmentCharacterIds != null &&
                data.unlockedRecruitmentCharacterIds.Contains(characterId);
        }

        /// <summary>조건부 획득 행 전체를 한 번 평가한다. 여러 해금도 저장은 정확히 한 번이다.</summary>
        public bool TryPersistCurrentUnlocks()
        {
            SaveData data = dataProvider();
            if (data == null || acquisitions == null) return false;
            var before = data.unlockedRecruitmentCharacterIds;
            var next = before == null ? new List<string>() : new List<string>(before);
            var known = new HashSet<string>(next, StringComparer.Ordinal);
            bool changed = false;
            foreach (CharacterAcquisitionDefinition acquisition in acquisitions.Acquisitions)
            {
                if (acquisition == null || !acquisition.Enabled || !acquisition.HasCondition || known.Contains(acquisition.CharacterId)) continue;
                CharacterUnlockConditionDefinition condition = conditions != null ? conditions.Find(acquisition.ConditionId) : null;
                if (condition != null && Evaluate(condition, data.characters)) { known.Add(acquisition.CharacterId); next.Add(acquisition.CharacterId); changed = true; }
            }
            if (!changed) return true;
            SaveMetadataSnapshot metadata = SaveMetadataSnapshot.Capture(data);
            data.unlockedRecruitmentCharacterIds = next;
            try
            {
                if (saveAction()) return true;
            }
            catch
            {
                data.unlockedRecruitmentCharacterIds = before; SaveData.RestoreMetadata(data, metadata); throw;
            }
            data.unlockedRecruitmentCharacterIds = before; SaveData.RestoreMetadata(data, metadata); return false;
        }

        public static bool Evaluate(CharacterUnlockConditionDefinition definition, IList<CharacterSaveState> characters)
        {
            if (definition == null || definition.Entries == null || definition.Entries.Count == 0) return false;
            var groups = new Dictionary<string, bool>(StringComparer.Ordinal);
            var hasActive = new HashSet<string>(StringComparer.Ordinal);
            foreach (CharacterUnlockConditionEntry entry in definition.Entries)
            {
                if (!entry.Enabled) continue;
                if (string.IsNullOrEmpty(entry.GroupId) || entry.RequiredValue <= 0 || !string.IsNullOrEmpty(entry.TargetId) || entry.Type == CharacterUnlockConditionType.Unknown) return false;
                bool passed = EvaluateEntry(entry, characters);
                if (hasActive.Add(entry.GroupId)) groups.Add(entry.GroupId, passed); else groups[entry.GroupId] &= passed;
            }
            foreach (bool passed in groups.Values) if (passed) return true;
            return false;
        }

        private static bool EvaluateEntry(CharacterUnlockConditionEntry entry, IList<CharacterSaveState> characters)
        {
            int count = 0, maxLevel = 0;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            if (characters != null) foreach (CharacterSaveState item in characters)
            {
                if (item == null || string.IsNullOrEmpty(item.characterId) || !seen.Add(item.characterId)) continue;
                count++; if (item.level > maxLevel) maxLevel = item.level;
            }
            return entry.Type == CharacterUnlockConditionType.MaxOwnedCharacterLevelAtLeast
                ? maxLevel >= entry.RequiredValue : count >= entry.RequiredValue;
        }
    }
}
