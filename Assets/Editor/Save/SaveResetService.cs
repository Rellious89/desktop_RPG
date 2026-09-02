using System;
using System.Collections.Generic;
using Common;

namespace CommonEditor.Save
{
    /// <summary>
    /// 초기화할 저장 항목을 고르는 플래그. <b>개발 도구 전용</b>이며 런타임 코드는 쓰지 않는다.
    ///
    /// <see cref="All"/>은 "지금 이 도구가 지원하는 초기화 항목 전체"라는 뜻이지 새 계정을 만드는 것이
    /// 아니다 - 계정 진행 같은 다른 필드는 여기에 들어 있지 않으므로 손대지 않는다.
    ///
    /// 값은 <c>1 &lt;&lt; n</c>로 떨어뜨려 두어 <c>EnumFlagsField</c>가 항목별 토글로 그리고,
    /// <see cref="All"/>을 고른 뒤 하나만 해제하는 조합이 그대로 만들어지게 한다.
    ///
    /// <see cref="Character"/>는 카탈로그의 기본 보유 캐릭터를 초기 상태로 복원하고, 고른 비기본
    /// 캐릭터만 삭제하며, 파티를 기본 편성으로 되돌린다.
    /// </summary>
    [Flags]
    public enum SaveResetTargets
    {
        None = 0,
        Item = 1 << 0,
        Currency = 1 << 1,
        Construction = 1 << 2,
        Character = 1 << 3,
        Quest = 1 << 4,
        All = Item | Currency | Construction | Character | Quest,
    }

    /// <summary>Character reset이 카탈로그에서 복사해 온 기본 보유 캐릭터의 초기 상태.</summary>
    public readonly struct InitialCharacterResetSeed
    {
        public string CharacterId { get; }
        public double BaseCorruption { get; }

        public InitialCharacterResetSeed(string characterId, double baseCorruption)
        {
            CharacterId = characterId ?? string.Empty;
            BaseCorruption = baseCorruption;
        }

        public bool IsValid => !string.IsNullOrWhiteSpace(CharacterId) &&
                               !double.IsNaN(BaseCorruption) && !double.IsInfinity(BaseCorruption) &&
                               BaseCorruption >= 0d;
    }

    /// <summary>에디터 리셋 도구가 런타임 퀘스트 에셋에서 복사해 온 최소 연결 정보.</summary>
    public readonly struct StoryQuestResetDefinition
    {
        public string QuestId { get; }
        public string CharacterId { get; }
        public string PreviousQuestId { get; }
        public bool Enabled { get; }

        public StoryQuestResetDefinition(string questId, string characterId, string previousQuestId, bool enabled = true)
        {
            QuestId = questId ?? string.Empty;
            CharacterId = characterId ?? string.Empty;
            PreviousQuestId = previousQuestId ?? string.Empty;
            Enabled = enabled;
        }

        public bool IsValid => Enabled && !string.IsNullOrWhiteSpace(QuestId) &&
                               !string.IsNullOrWhiteSpace(CharacterId);
    }

    public enum StoryQuestResetOutcome
    {
        Success,
        QuestNotFound,
        InvalidQuestChain,
        SaveFailed,
    }

    /// <summary><see cref="SaveResetService.Apply(SaveData, SaveResetTargets, IReadOnlyList{string}, IReadOnlyList{InitialCharacterResetSeed}, int, IReadOnlyList{StoryQuestResetDefinition}, Func{bool})"/>의 결과 갈래.</summary>
    public enum SaveResetOutcome
    {
        /// <summary>고른 항목이 없어 아무것도 하지 않았다 - 저장도 하지 않는다.</summary>
        NothingSelected,

        /// <summary>선택 항목을 모두 초기화하고 저장에 성공했다.</summary>
        Success,

        /// <summary>저장에 실패해 <b>변경 전 상태로 전부 되돌렸다</b>(부분 초기화는 남지 않는다).</summary>
        SaveFailed,

        /// <summary>Character 초기 상태를 만들 카탈로그 시드나 유효한 파티 슬롯 계약이 없어
        /// 아무것도 바꾸거나 저장하지 않았다.</summary>
        InvalidCharacterResetConfiguration,
    }

    /// <summary><see cref="SaveResetService.Apply(SaveData, SaveResetTargets, IReadOnlyList{string}, IReadOnlyList{InitialCharacterResetSeed}, int, IReadOnlyList{StoryQuestResetDefinition}, Func{bool})"/>가 무엇을 어떻게 했는지.</summary>
    public readonly struct SaveResetResult
    {
        public SaveResetOutcome Outcome { get; }

        /// <summary>실제로 <b>바꾼</b> 항목. 아무것도 안 바꿨으면
        /// <see cref="SaveResetTargets.None"/>이다.</summary>
        public SaveResetTargets AppliedTargets { get; }

        /// <summary>실제로 저장 데이터에서 지운 캐릭터 수. 캐릭터를 고르지 않았으면 0이다.</summary>
        public int RemovedCharacterCount { get; }

        /// <summary>초기 상태로 되돌렸거나 누락에서 복구한 기본 보유 캐릭터 수.</summary>
        public int ResetInitialCharacterCount { get; }

        public SaveResetResult(
            SaveResetOutcome outcome,
            SaveResetTargets appliedTargets,
            int removedCharacterCount,
            int resetInitialCharacterCount = 0)
        {
            Outcome = outcome;
            AppliedTargets = appliedTargets;
            RemovedCharacterCount = removedCharacterCount;
            ResetInitialCharacterCount = resetInitialCharacterCount;
        }

        public bool Saved => Outcome == SaveResetOutcome.Success;
    }

    /// <summary>
    /// 저장 데이터의 <b>일부 항목만</b> 초기화하는 순수 로직. <see cref="SaveResetWindow"/>가 이 자리로
    /// <see cref="SaveSystem.Data"/>와 <see cref="SaveSystem.Save"/>를 넘기고, 시험은 격리된 메모리
    /// <see cref="SaveData"/>와 저장 대리자를 넘긴다 - 그래서 이 로직은 실제 저장 파일도, 캐릭터 정의
    /// 에셋도 알지 못한다(기본 캐릭터는 id와 기본 오염도만 담은 순수 시드로 넘어온다).
    ///
    /// <b>SaveSystem에 런타임 Reset API를 만들지 않으려는 것이 이 분리의 목적이다.</b> 초기화는 개발
    /// 도구에서만 필요하므로, 저장 계층은 그대로 두고 여기(Editor 전용)에서 <see cref="SaveData"/>의
    /// 필드를 직접 고친 뒤 넘겨받은 저장 대리자를 <b>정확히 한 번</b> 부른다.
    ///
    /// <b>전부 아니면 전무다.</b> 선택 항목을 모두 메모리에 적용한 뒤 한 번에 저장하고, 저장이 실패하면
    /// 이번에 바꾼 필드를 전부 원래 값으로 되돌린다 - 성공한 일부만 남는 부분 초기화를 만들지 않는다.
    /// </summary>
    public static class SaveResetService
    {
        /// <summary>Character 시드가 필요 없는 호출부를 위한 짧은 형태.</summary>
        public static SaveResetResult Apply(SaveData data, SaveResetTargets targets, Func<bool> save)
        {
            return Apply(data, targets, null, null, 0, null, save);
        }

        /// <summary>
        /// Character 카탈로그에서 만든 초기 시드와 파티 고정 슬롯 계약을 함께 받아 선택 항목을 원자적으로
        /// 초기화한다. Character 비트가 켜져 있으면 기본 캐릭터는 전부 초기 상태로 복원하고, 선택된
        /// 비기본 캐릭터만 삭제하며, 파티는 카탈로그 시드 순서의 초기 편성으로 다시 만든다.
        /// </summary>
        public static SaveResetResult Apply(
            SaveData data,
            SaveResetTargets targets,
            IReadOnlyList<string> characterIdsToRemove,
            IReadOnlyList<InitialCharacterResetSeed> initialCharacterSeeds,
            int partySlotCount,
            IReadOnlyList<StoryQuestResetDefinition> questDefinitions,
            Func<bool> save)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (save == null) throw new ArgumentNullException(nameof(save));

            // 정의되지 않은 비트가 섞여 들어와도 지원하는 항목만 본다. 모집 주기는 별도 대상이 아니라
            // Construction의 종속 기록이므로 그 비트와 함께 움직인다.
            SaveResetTargets effective = targets & SaveResetTargets.All;

            bool resetItems = (effective & SaveResetTargets.Item) != 0;
            bool resetCurrency = (effective & SaveResetTargets.Currency) != 0;
            bool resetConstruction = (effective & SaveResetTargets.Construction) != 0;
            bool resetStory = (effective & SaveResetTargets.Quest) != 0;
            bool resetCharacters = (effective & SaveResetTargets.Character) != 0;

            List<InitialCharacterResetSeed> seeds = null;
            HashSet<string> initialIds = null;
            if (resetCharacters && !TryNormalizeInitialSeeds(initialCharacterSeeds, partySlotCount, out seeds, out initialIds))
            {
                // Character reset 계약을 만족할 수 없으면 다른 선택 항목도 함께 적용하지 않는다.
                return new SaveResetResult(
                    SaveResetOutcome.InvalidCharacterResetConfiguration, SaveResetTargets.None, 0);
            }

            // 실제로 지울 캐릭터 집합을 미리 확정한다 - 요청 ∩ 존재 ∖ catalog InitiallyOwned.
            HashSet<string> removeSet = resetCharacters
                ? ResolveRemovableIds(data.characters, characterIdsToRemove, initialIds)
                : null;

            bool removeCharacters = removeSet != null && removeSet.Count > 0;
            bool resetAllUnlocks = effective == SaveResetTargets.All && data.unlockedRecruitmentCharacterIds != null &&
                                   data.unlockedRecruitmentCharacterIds.Count > 0;
            // Character는 삭제 대상이 없어도 기본 캐릭터 진행 초기화/누락 복구/초기 파티 복원이 있으므로
            // 비트 자체가 실제 적용이다.
            if (!resetItems && !resetCurrency && !resetConstruction && !resetStory && !resetCharacters &&
                !resetAllUnlocks)
            {
                return new SaveResetResult(SaveResetOutcome.NothingSelected, SaveResetTargets.None, 0);
            }

            // 되돌릴 수 있게 바꿀 필드의 원래 값을 들고 있는다. 목록은 새 목록으로 <b>교체</b>하고 예전
            // 참조를 그대로 보관하므로, 되돌리기는 참조를 도로 끼우는 것으로 끝난다(깊은 복사가 없다).
            List<InventoryItemState> oldItems = resetItems ? data.items : null;
            int oldCurrency = resetCurrency ? data.currency : 0;
            List<BuildingConstructionSaveState> oldConstructions =
                resetConstruction ? data.buildingConstructions : null;
            List<RecruitmentCycleSaveState> oldRecruitmentCycles =
                resetConstruction ? data.recruitmentCycles : null;
            List<PurificationSlotSaveState> oldPurificationSlots =
                resetConstruction ? data.purificationSlots : null;
            List<string> oldUnlockedRecruitmentCharacterIds = null;
            if (resetAllUnlocks) oldUnlockedRecruitmentCharacterIds = data.unlockedRecruitmentCharacterIds;
            List<CharacterStoryQuestSaveState> oldCharacterStoryQuests =
                (resetStory || removeCharacters) ? data.characterStoryQuests : null;

            List<CharacterSaveState> oldCharacters = resetCharacters ? data.characters : null;
            List<string> oldPartyCharacterIds = resetCharacters ? data.partyCharacterIds : null;
            List<RecoverySlotSaveState> oldRecoverySlots = resetCharacters ? data.recoverySlots : null;
            if (resetCharacters && !resetConstruction) oldPurificationSlots = data.purificationSlots;
            int removedCount = 0;

            if (resetItems) data.items = new List<InventoryItemState>();
            if (resetCurrency) data.currency = 0;
            if (resetConstruction)
            {
                data.buildingConstructions = new List<BuildingConstructionSaveState>();
                data.recruitmentCycles = new List<RecruitmentCycleSaveState>();
                data.purificationSlots = new List<PurificationSlotSaveState> { new PurificationSlotSaveState() };
            }
            if (resetAllUnlocks) data.unlockedRecruitmentCharacterIds = new List<string>();
            if (resetCharacters)
            {
                if (!resetAllUnlocks) oldUnlockedRecruitmentCharacterIds = data.unlockedRecruitmentCharacterIds;

                // 비기본 생존자는 객체와 순서를 보존한다. 기본 캐릭터는 현재 저장에 있으면 같은 위치에
                // 초기 상태 객체로 교체하고, 누락된 시드는 catalog 순서대로 뒤에 복구한다.
                var restored = new List<CharacterSaveState>(oldCharacters?.Count ?? seeds.Count);
                var restoredInitialIds = new HashSet<string>(StringComparer.Ordinal);
                if (oldCharacters != null)
                {
                    foreach (CharacterSaveState state in oldCharacters)
                    {
                        string id = state?.characterId;
                        if (!string.IsNullOrEmpty(id) && removeSet.Contains(id))
                        {
                            removedCount++;
                            continue;
                        }

                        if (!string.IsNullOrEmpty(id) && initialIds.Contains(id))
                        {
                            InitialCharacterResetSeed seed = FindSeed(seeds, id);
                            restored.Add(CreateInitialCharacterState(seed));
                            restoredInitialIds.Add(id);
                        }
                        else
                        {
                            restored.Add(state);
                        }
                    }
                }

                foreach (InitialCharacterResetSeed seed in seeds)
                {
                    if (restoredInitialIds.Add(seed.CharacterId))
                    {
                        restored.Add(CreateInitialCharacterState(seed));
                    }
                }

                data.characters = restored;

                // Character reset은 초기 편성으로 돌아간다. 슬롯 길이는 PartyConfig 계약 그대로이고,
                // 여러 기본 캐릭터는 catalog 순서로 가능한 앞 슬롯부터 채운다.
                var initialParty = new List<string>(partySlotCount);
                for (int i = 0; i < partySlotCount; i++) initialParty.Add(string.Empty);
                for (int i = 0; i < seeds.Count && i < initialParty.Count; i++)
                {
                    initialParty[i] = seeds[i].CharacterId;
                }
                data.partyCharacterIds = initialParty;

                var affectedCharacterIds = new HashSet<string>(initialIds, StringComparer.Ordinal);
                affectedCharacterIds.UnionWith(removeSet);
                data.recoverySlots = CloneRecoverySlotsClearing(data.recoverySlots, affectedCharacterIds);
                data.purificationSlots = ClonePurificationSlotsClearing(data.purificationSlots, affectedCharacterIds);

                if (!resetStory && data.characterStoryQuests != null)
                {
                    var remainingStories = new List<CharacterStoryQuestSaveState>(data.characterStoryQuests);
                    remainingStories.RemoveAll(state => state != null && removeSet.Contains(state.characterId));
                    data.characterStoryQuests = remainingStories;
                }
                if (data.unlockedRecruitmentCharacterIds != null)
                {
                    var remainingUnlocks = new List<string>(data.unlockedRecruitmentCharacterIds);
                    remainingUnlocks.RemoveAll(id => removeSet.Contains(id));
                    data.unlockedRecruitmentCharacterIds = remainingUnlocks;
                }
            }

            // 캐릭터 삭제와 함께 실행될 때는 살아남은 보유 목록을 기준으로 루트 상태를 만든다.
            if (resetStory)
            {
                data.characterStoryQuests = BuildInitialStoryQuestStates(data.characters, questDefinitions);
            }

            bool saved;
            try
            {
                saved = save();
            }
            catch
            {
                // 대리자가 터져도 메모리는 원래대로 돌려놓고 예외는 그대로 올려보낸다 - 부분 초기화가
                // 남는 것보다 호출부가 실패를 알아채는 편이 낫다.
                Rollback(data, resetItems, oldItems, resetCurrency, oldCurrency, resetConstruction,
                    oldConstructions, oldRecruitmentCycles, oldPurificationSlots, resetCharacters, oldCharacters,
                    oldPartyCharacterIds, oldRecoverySlots, oldUnlockedRecruitmentCharacterIds, resetAllUnlocks,
                    oldCharacterStoryQuests);
                throw;
            }

            if (!saved)
            {
                Rollback(data, resetItems, oldItems, resetCurrency, oldCurrency, resetConstruction,
                    oldConstructions, oldRecruitmentCycles, oldPurificationSlots, resetCharacters, oldCharacters,
                    oldPartyCharacterIds, oldRecoverySlots, oldUnlockedRecruitmentCharacterIds, resetAllUnlocks,
                    oldCharacterStoryQuests);
                return new SaveResetResult(SaveResetOutcome.SaveFailed, effective, 0);
            }

            SaveResetTargets applied = SaveResetTargets.None;
            if (resetItems) applied |= SaveResetTargets.Item;
            if (resetCurrency) applied |= SaveResetTargets.Currency;
            if (resetConstruction) applied |= SaveResetTargets.Construction;
            if (resetCharacters) applied |= SaveResetTargets.Character;
            if (resetStory) applied |= SaveResetTargets.Quest;

            return new SaveResetResult(
                SaveResetOutcome.Success, applied, removedCount, resetCharacters ? seeds.Count : 0);
        }

        /// <summary>
        /// 지정 퀘스트의 캐릭터만 해당 단계가 막 시작된 상태로 되돌린다. 이전 연결 단계들은 완료로
        /// 기록하고 목표 진행도와 완료 가능 표시는 비운다. 정의에 없는 ID는 저장하지 않는다.
        /// </summary>
        public static StoryQuestResetOutcome ResetStoryQuestTo(
            SaveData data,
            string questId,
            IReadOnlyList<StoryQuestResetDefinition> definitions,
            Func<bool> save)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (save == null) throw new ArgumentNullException(nameof(save));

            Dictionary<string, StoryQuestResetDefinition> byId = BuildQuestDefinitionMap(definitions);
            string normalizedId = questId?.Trim() ?? string.Empty;
            if (!byId.TryGetValue(normalizedId, out StoryQuestResetDefinition target))
            {
                return StoryQuestResetOutcome.QuestNotFound;
            }

            var reversedPreviousIds = new List<string>();
            var visited = new HashSet<string>(StringComparer.Ordinal) { target.QuestId };
            StoryQuestResetDefinition cursor = target;
            while (!string.IsNullOrEmpty(cursor.PreviousQuestId))
            {
                if (!byId.TryGetValue(cursor.PreviousQuestId, out StoryQuestResetDefinition previous) ||
                    !string.Equals(previous.CharacterId, target.CharacterId, StringComparison.Ordinal) ||
                    !visited.Add(previous.QuestId))
                {
                    return StoryQuestResetOutcome.InvalidQuestChain;
                }

                reversedPreviousIds.Add(previous.QuestId);
                cursor = previous;
            }
            reversedPreviousIds.Reverse();

            List<CharacterStoryQuestSaveState> oldStates = data.characterStoryQuests;
            var replacement = oldStates != null
                ? new List<CharacterStoryQuestSaveState>(oldStates.Count + 1)
                : new List<CharacterStoryQuestSaveState>();
            if (oldStates != null)
            {
                foreach (CharacterStoryQuestSaveState state in oldStates)
                {
                    if (state == null || !string.Equals(state.characterId, target.CharacterId, StringComparison.Ordinal))
                    {
                        replacement.Add(state);
                    }
                }
            }

            replacement.Add(new CharacterStoryQuestSaveState
            {
                characterId = target.CharacterId,
                activeQuestId = target.QuestId,
                objectiveProgress = new List<CharacterStoryObjectiveProgressSaveState>(),
                completedQuestIds = reversedPreviousIds,
                readyToComplete = false,
                graduated = false,
            });
            data.characterStoryQuests = replacement;

            bool saved;
            try
            {
                saved = save();
            }
            catch
            {
                data.characterStoryQuests = oldStates;
                throw;
            }

            if (saved) return StoryQuestResetOutcome.Success;
            data.characterStoryQuests = oldStates;
            return StoryQuestResetOutcome.SaveFailed;
        }

        private static List<CharacterStoryQuestSaveState> BuildInitialStoryQuestStates(
            IReadOnlyList<CharacterSaveState> characters,
            IReadOnlyList<StoryQuestResetDefinition> definitions)
        {
            var rootsByCharacter = new Dictionary<string, StoryQuestResetDefinition>(StringComparer.Ordinal);
            if (definitions != null)
            {
                foreach (StoryQuestResetDefinition definition in definitions)
                {
                    if (!definition.IsValid || !string.IsNullOrEmpty(definition.PreviousQuestId)) continue;
                    if (!rootsByCharacter.ContainsKey(definition.CharacterId))
                    {
                        rootsByCharacter.Add(definition.CharacterId, definition);
                    }
                }
            }

            var result = new List<CharacterStoryQuestSaveState>();
            var addedCharacters = new HashSet<string>(StringComparer.Ordinal);
            if (characters == null) return result;
            foreach (CharacterSaveState character in characters)
            {
                string characterId = character?.characterId;
                if (string.IsNullOrEmpty(characterId) || !addedCharacters.Add(characterId)) continue;
                if (!rootsByCharacter.TryGetValue(characterId, out StoryQuestResetDefinition root)) continue;
                result.Add(new CharacterStoryQuestSaveState
                {
                    characterId = characterId,
                    activeQuestId = root.QuestId,
                    objectiveProgress = new List<CharacterStoryObjectiveProgressSaveState>(),
                    completedQuestIds = new List<string>(),
                    readyToComplete = false,
                    graduated = false,
                });
            }

            return result;
        }

        private static Dictionary<string, StoryQuestResetDefinition> BuildQuestDefinitionMap(
            IReadOnlyList<StoryQuestResetDefinition> definitions)
        {
            var result = new Dictionary<string, StoryQuestResetDefinition>(StringComparer.Ordinal);
            if (definitions == null) return result;
            foreach (StoryQuestResetDefinition definition in definitions)
            {
                if (definition.IsValid && !result.ContainsKey(definition.QuestId))
                {
                    result.Add(definition.QuestId, definition);
                }
            }
            return result;
        }

        /// <summary>
        /// 실제로 지울 캐릭터 id 집합을 만든다 - <c>요청 ∩ 저장에 존재 ∖ 기본 보유</c>. 순수 함수라
        /// 정의 에셋 없이 시험할 수 있다(창은 정의의 <c>InitiallyOwned</c>로 보호 집합을 채워 넘긴다).
        /// </summary>
        public static HashSet<string> ResolveRemovableIds(
            IReadOnlyList<CharacterSaveState> characters,
            IReadOnlyList<string> requestedIds,
            IReadOnlyCollection<string> protectedIds)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            if (characters == null || requestedIds == null || requestedIds.Count == 0) return result;

            // 저장에 실제로 존재하는 id만 대상으로 삼는다.
            var present = new HashSet<string>(StringComparer.Ordinal);
            foreach (CharacterSaveState state in characters)
            {
                if (state != null && !string.IsNullOrEmpty(state.characterId)) present.Add(state.characterId);
            }

            HashSet<string> guarded = protectedIds != null
                ? new HashSet<string>(protectedIds, StringComparer.Ordinal)
                : null;

            foreach (string id in requestedIds)
            {
                if (string.IsNullOrEmpty(id)) continue;
                if (guarded != null && guarded.Contains(id)) continue; // 기본 보유는 절대 지우지 않는다.
                if (!present.Contains(id)) continue;
                result.Add(id);
            }

            return result;
        }

        private static bool TryNormalizeInitialSeeds(
            IReadOnlyList<InitialCharacterResetSeed> source,
            int partySlotCount,
            out List<InitialCharacterResetSeed> seeds,
            out HashSet<string> ids)
        {
            seeds = new List<InitialCharacterResetSeed>();
            ids = new HashSet<string>(StringComparer.Ordinal);
            if (partySlotCount < 1 || source == null || source.Count == 0) return false;

            foreach (InitialCharacterResetSeed seed in source)
            {
                // 창은 catalog의 유효한 InitiallyOwned 정의만 넘겨야 한다. 서비스도 잘못된 시드를
                // 조용히 일부만 적용하지 않고 전체 Character reset을 거부한다.
                if (!seed.IsValid || !ids.Add(seed.CharacterId)) return false;
                seeds.Add(seed);
            }

            return seeds.Count > 0;
        }

        private static InitialCharacterResetSeed FindSeed(
            IReadOnlyList<InitialCharacterResetSeed> seeds,
            string characterId)
        {
            for (int i = 0; i < seeds.Count; i++)
            {
                if (string.Equals(seeds[i].CharacterId, characterId, StringComparison.Ordinal)) return seeds[i];
            }

            throw new InvalidOperationException($"초기 캐릭터 시드 '{characterId}'를 찾을 수 없습니다.");
        }

        private static CharacterSaveState CreateInitialCharacterState(InitialCharacterResetSeed seed)
        {
            return new CharacterSaveState
            {
                characterId = seed.CharacterId,
                level = 1,
                currentExp = 0,
                currentStamina = -1,
                passiveStaminaLastCalculatedUtc = string.Empty,
                passiveStaminaProgress = 0,
                currentCorruption = seed.BaseCorruption,
            };
        }

        private static List<RecoverySlotSaveState> CloneRecoverySlotsClearing(
            IReadOnlyList<RecoverySlotSaveState> source,
            HashSet<string> affectedCharacterIds)
        {
            if (source == null) return null;
            var result = new List<RecoverySlotSaveState>(source.Count);
            foreach (RecoverySlotSaveState slot in source)
            {
                if (slot == null)
                {
                    result.Add(null);
                    continue;
                }

                var clone = new RecoverySlotSaveState
                {
                    characterId = slot.characterId,
                    startStamina = slot.startStamina,
                    startedAtUtc = slot.startedAtUtc,
                    completeAtUtc = slot.completeAtUtc,
                    completionNotified = slot.completionNotified,
                };
                if (affectedCharacterIds.Contains(clone.characterId)) clone.Clear();
                result.Add(clone);
            }

            return result;
        }

        private static List<PurificationSlotSaveState> ClonePurificationSlotsClearing(
            IReadOnlyList<PurificationSlotSaveState> source,
            HashSet<string> affectedCharacterIds)
        {
            if (source == null) return null;
            var result = new List<PurificationSlotSaveState>(source.Count);
            foreach (PurificationSlotSaveState slot in source)
            {
                if (slot == null)
                {
                    result.Add(null);
                    continue;
                }

                var clone = new PurificationSlotSaveState
                {
                    purificationTypeId = slot.purificationTypeId,
                    characterId = slot.characterId,
                    lastCalculatedAtUtc = slot.lastCalculatedAtUtc,
                    progressTicks = slot.progressTicks,
                };
                if (affectedCharacterIds.Contains(clone.characterId)) clone.Clear();
                result.Add(clone);
            }

            return result;
        }

        private static void Rollback(
            SaveData data,
            bool resetItems, List<InventoryItemState> oldItems,
            bool resetCurrency, int oldCurrency,
            bool resetConstruction, List<BuildingConstructionSaveState> oldConstructions,
            List<RecruitmentCycleSaveState> oldRecruitmentCycles,
            List<PurificationSlotSaveState> oldPurificationSlots,
            bool resetCharacters, List<CharacterSaveState> oldCharacters, List<string> oldPartyCharacterIds,
            List<RecoverySlotSaveState> oldRecoverySlots,
            List<string> oldUnlockedRecruitmentCharacterIds, bool resetAllUnlocks,
            List<CharacterStoryQuestSaveState> oldCharacterStoryQuests = null)
        {
            if (resetItems) data.items = oldItems;
            if (resetCurrency) data.currency = oldCurrency;
            if (resetConstruction)
            {
                data.buildingConstructions = oldConstructions;
                data.recruitmentCycles = oldRecruitmentCycles;
                data.purificationSlots = oldPurificationSlots;
            }

            if (resetCharacters)
            {
                data.characters = oldCharacters;
                data.partyCharacterIds = oldPartyCharacterIds;
                data.recoverySlots = oldRecoverySlots;
                data.purificationSlots = oldPurificationSlots;
                data.unlockedRecruitmentCharacterIds = oldUnlockedRecruitmentCharacterIds;
            }
            if (resetAllUnlocks) data.unlockedRecruitmentCharacterIds = oldUnlockedRecruitmentCharacterIds;
            if (oldCharacterStoryQuests != null) data.characterStoryQuests = oldCharacterStoryQuests;
        }
    }
}
