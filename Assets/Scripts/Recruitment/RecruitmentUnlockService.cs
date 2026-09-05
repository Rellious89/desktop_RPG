using System;
using System.Collections.Generic;
using Common;

namespace Recruitment
{
    /// <summary>조건을 읽는 순수 평가와, 최초 달성을 저장하는 트랜잭션을 한 곳에 둔다.</summary>
    public sealed class RecruitmentUnlockService
    {
        public readonly struct UnlockConditionProgress
        {
            public UnlockConditionProgress(CharacterUnlockConditionEntry entry, int currentValue, bool isSatisfied)
            {
                Entry = entry;
                CurrentValue = currentValue;
                IsSatisfied = isSatisfied;
            }

            public CharacterUnlockConditionEntry Entry { get; }
            public int CurrentValue { get; }
            public bool IsSatisfied { get; }
        }

        /// <summary>UI가 저장 없이 읽을 수 있는 현재 모집 자격 화면용 결과다.</summary>
        public sealed class UnlockProgressSnapshot
        {
            internal UnlockProgressSnapshot(bool hasEnabledAcquisition, bool hasCondition, bool isDefinitionValid,
                bool currentConditionSatisfied, bool permanentlyUnlocked, List<UnlockConditionProgress> conditions)
            {
                HasEnabledAcquisition = hasEnabledAcquisition;
                HasCondition = hasCondition;
                IsDefinitionValid = isDefinitionValid;
                IsCurrentConditionSatisfied = currentConditionSatisfied;
                IsPermanentlyUnlocked = permanentlyUnlocked;
                Conditions = conditions ?? new List<UnlockConditionProgress>();
            }

            public bool HasEnabledAcquisition { get; }
            public bool HasCondition { get; }
            public bool IsDefinitionValid { get; }
            public bool IsCurrentConditionSatisfied { get; }
            public bool IsPermanentlyUnlocked { get; }
            public IReadOnlyList<UnlockConditionProgress> Conditions { get; }
            public int SatisfiedConditionCount
            {
                get
                {
                    int count = 0;
                    for (int i = 0; i < Conditions.Count; i++) if (Conditions[i].IsSatisfied) count++;
                    return count;
                }
            }

            /// <summary>조건 없는 활성 획득 행, 현재 조건 달성, 또는 보존된 영구 해금만 모집 포함 상태다.</summary>
            public bool IsRecruitmentEligible => HasEnabledAcquisition && IsDefinitionValid &&
                (!HasCondition || IsCurrentConditionSatisfied || IsPermanentlyUnlocked);
        }
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

        /// <summary>
        /// 저장이나 알림을 전혀 하지 않는 현재 진행 조회다. 영구 해금 기록과 현재 조건값은 별도로 보존해
        /// UI가 모집 포함 여부와 각 행의 현재 충족 상태를 혼동하지 않게 한다.
        /// </summary>
        public UnlockProgressSnapshot GetProgress(string characterId)
        {
            return EvaluateProgress(acquisitions, conditions, dataProvider(), characterId, IsUnlocked(characterId));
        }

        public static UnlockProgressSnapshot EvaluateProgress(CharacterAcquisitionCatalog acquisitions,
            CharacterUnlockConditionCatalog conditions, SaveData data, string characterId, bool permanentlyUnlocked = false)
        {
            CharacterAcquisitionDefinition acquisition = FindEnabledAcquisition(acquisitions, characterId);
            if (acquisition == null)
                return new UnlockProgressSnapshot(false, false, false, false, permanentlyUnlocked, new List<UnlockConditionProgress>());
            if (!acquisition.HasCondition)
                return new UnlockProgressSnapshot(true, false, true, true, permanentlyUnlocked, new List<UnlockConditionProgress>());

            CharacterUnlockConditionDefinition definition = conditions != null ? conditions.Find(acquisition.ConditionId) : null;
            return EvaluateProgress(definition, data != null ? data.characters : null, permanentlyUnlocked);
        }

        private static CharacterAcquisitionDefinition FindEnabledAcquisition(CharacterAcquisitionCatalog acquisitions, string characterId)
        {
            if (acquisitions == null || string.IsNullOrEmpty(characterId)) return null;
            IReadOnlyList<CharacterAcquisitionDefinition> values = acquisitions.Acquisitions;
            for (int i = 0; i < values.Count; i++)
            {
                CharacterAcquisitionDefinition candidate = values[i];
                if (candidate != null && candidate.Enabled && string.Equals(candidate.CharacterId, characterId, StringComparison.Ordinal))
                    return candidate;
            }
            return null;
        }

        public static UnlockProgressSnapshot EvaluateProgress(CharacterUnlockConditionDefinition definition,
            IList<CharacterSaveState> characters, bool permanentlyUnlocked = false)
        {
            var progress = new List<UnlockConditionProgress>();
            if (definition == null || definition.Entries == null || definition.Entries.Count == 0)
                return new UnlockProgressSnapshot(true, true, false, false, permanentlyUnlocked, progress);

            var groups = new Dictionary<string, bool>(StringComparer.Ordinal);
            var seenGroups = new HashSet<string>(StringComparer.Ordinal);
            foreach (CharacterUnlockConditionEntry entry in definition.Entries)
            {
                if (!entry.Enabled) continue;
                if (!IsSupportedEntry(entry))
                    return new UnlockProgressSnapshot(true, true, false, false, permanentlyUnlocked, new List<UnlockConditionProgress>());

                int current = CurrentValue(entry, characters);
                bool passed = current >= entry.RequiredValue;
                progress.Add(new UnlockConditionProgress(entry, current, passed));
                if (seenGroups.Add(entry.GroupId)) groups.Add(entry.GroupId, passed); else groups[entry.GroupId] &= passed;
            }

            // An all-disabled condition definition is not a condition-less acquisition row.
            if (progress.Count == 0)
                return new UnlockProgressSnapshot(true, true, false, false, permanentlyUnlocked, progress);
            bool overall = false;
            foreach (bool passed in groups.Values) if (passed) { overall = true; break; }
            return new UnlockProgressSnapshot(true, true, true, overall, permanentlyUnlocked, progress);
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
            UnlockProgressSnapshot snapshot = EvaluateProgress(definition, characters);
            return snapshot.IsDefinitionValid && snapshot.IsCurrentConditionSatisfied;
        }

        private static bool IsSupportedEntry(CharacterUnlockConditionEntry entry) =>
            !string.IsNullOrEmpty(entry.GroupId) && entry.RequiredValue > 0 && string.IsNullOrEmpty(entry.TargetId) &&
            entry.Type != CharacterUnlockConditionType.Unknown;

        private static int CurrentValue(CharacterUnlockConditionEntry entry, IList<CharacterSaveState> characters)
        {
            int count = 0, maxLevel = 0;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            if (characters != null) foreach (CharacterSaveState item in characters)
            {
                if (item == null || string.IsNullOrEmpty(item.characterId) || !seen.Add(item.characterId)) continue;
                count++; if (item.level > maxLevel) maxLevel = item.level;
            }
            return entry.Type == CharacterUnlockConditionType.MaxOwnedCharacterLevelAtLeast ? maxLevel : count;
        }
    }
}
