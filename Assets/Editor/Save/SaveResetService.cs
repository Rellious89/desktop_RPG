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
    /// <see cref="Character"/>는 다른 항목과 성격이 다르다 - 목록을 통째로 비우는 것이 아니라 <b>고른
    /// 캐릭터만</b> 지운다. 그래서 이 비트가 켜져 있어도 실제로 고른 characterId가 없으면 캐릭터는
    /// 아무것도 바뀌지 않는다(<see cref="SaveResetService.Apply(SaveData, SaveResetTargets,
    /// IReadOnlyList{string}, IReadOnlyCollection{string}, Func{bool})"/> 참고).
    /// </summary>
    [Flags]
    public enum SaveResetTargets
    {
        None = 0,
        Item = 1 << 0,
        Currency = 1 << 1,
        Construction = 1 << 2,
        Character = 1 << 3,
        All = Item | Currency | Construction | Character,
    }

    /// <summary><see cref="SaveResetService.Apply(SaveData, SaveResetTargets, IReadOnlyList{string}, IReadOnlyCollection{string}, Func{bool})"/>의 결과 갈래.</summary>
    public enum SaveResetOutcome
    {
        /// <summary>고른 항목이 없어 아무것도 하지 않았다 - 저장도 하지 않는다.</summary>
        NothingSelected,

        /// <summary>선택 항목을 모두 초기화하고 저장에 성공했다.</summary>
        Success,

        /// <summary>저장에 실패해 <b>변경 전 상태로 전부 되돌렸다</b>(부분 초기화는 남지 않는다).</summary>
        SaveFailed,
    }

    /// <summary><see cref="SaveResetService.Apply(SaveData, SaveResetTargets, IReadOnlyList{string}, IReadOnlyCollection{string}, Func{bool})"/>가 무엇을 어떻게 했는지.</summary>
    public readonly struct SaveResetResult
    {
        public SaveResetOutcome Outcome { get; }

        /// <summary>실제로 <b>바꾼</b> 항목. 비트가 켜져 있어도 실제 변경이 없으면(예: 고른 캐릭터가
        /// 하나도 없는 <see cref="SaveResetTargets.Character"/>) 여기에 들어가지 않는다. 아무것도 안
        /// 바꿨으면 <see cref="SaveResetTargets.None"/>이다.</summary>
        public SaveResetTargets AppliedTargets { get; }

        /// <summary>실제로 저장 데이터에서 지운 캐릭터 수. 캐릭터를 고르지 않았으면 0이다.</summary>
        public int RemovedCharacterCount { get; }

        public SaveResetResult(SaveResetOutcome outcome, SaveResetTargets appliedTargets, int removedCharacterCount)
        {
            Outcome = outcome;
            AppliedTargets = appliedTargets;
            RemovedCharacterCount = removedCharacterCount;
        }

        public bool Saved => Outcome == SaveResetOutcome.Success;
    }

    /// <summary>
    /// 저장 데이터의 <b>일부 항목만</b> 초기화하는 순수 로직. <see cref="SaveResetWindow"/>가 이 자리로
    /// <see cref="SaveSystem.Data"/>와 <see cref="SaveSystem.Save"/>를 넘기고, 시험은 격리된 메모리
    /// <see cref="SaveData"/>와 저장 대리자를 넘긴다 - 그래서 이 로직은 실제 저장 파일도, 캐릭터 정의
    /// 에셋도 알지 못한다(기본 보유 여부는 <b>문자열 집합</b>으로만 넘어온다).
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
        /// <summary>캐릭터를 고르지 않는 기존 호출부를 위한 짧은 형태. 아이템·재화·건축만 다룬다.</summary>
        public static SaveResetResult Apply(SaveData data, SaveResetTargets targets, Func<bool> save)
        {
            return Apply(data, targets, null, null, save);
        }

        /// <summary>
        /// 선택한 항목을 초기화하고 <paramref name="save"/>를 <b>최대 한 번</b> 부른다.
        ///
        /// <list type="bullet">
        ///   <item>고른 항목이 없거나, 골랐어도 실제로 바뀌는 것이 없으면(예: 고른 캐릭터가 모두 기본
        ///         보유이거나 목록에 없음) 아무것도 바꾸지 않고 저장도 하지 않는다
        ///         (<see cref="SaveResetOutcome.NothingSelected"/>).</item>
        ///   <item>선택 항목을 모두 메모리에 적용한 뒤 <paramref name="save"/>를 한 번 부른다.</item>
        ///   <item>저장이 false를 돌려주면(또는 예외를 던지면) 이번에 바꾼 필드를 전부 되돌린다.</item>
        /// </list>
        ///
        /// <b>캐릭터 삭제 규칙.</b> <see cref="SaveResetTargets.Character"/> 비트가 켜져 있고
        /// <paramref name="characterIdsToRemove"/>에 실제로 지울 것이 있을 때만 캐릭터를 지운다. 지우는
        /// 대상은 <c>요청 ∩ 저장에 존재 ∖ 기본 보유</c>다 - <paramref name="protectedCharacterIds"/>에
        /// 든 id는 요청에 있어도 <b>절대 지우지 않는다</b>. 지운 캐릭터가 회복 중이던 회복 슬롯은
        /// 목록에서 빼지 않고 <b>그 슬롯만 빈 상태로 바꾼다</b>(인덱스가 슬롯 번호이므로 목록을 줄이면
        /// 다른 슬롯 번호가 밀린다).
        /// </summary>
        /// <param name="data">고칠 저장 문서. null이면 <see cref="ArgumentNullException"/>.</param>
        /// <param name="targets">초기화할 항목. <see cref="SaveResetTargets.All"/> 밖의 비트는 무시한다.</param>
        /// <param name="characterIdsToRemove">지울 캐릭터 id. null/빈 목록이면 캐릭터는 건드리지 않는다.</param>
        /// <param name="protectedCharacterIds">절대 지우지 않을 캐릭터 id(기본 보유). null이면 없음.</param>
        /// <param name="save">메모리 적용 뒤 파일에 기록하는 대리자. 성공하면 true. null이면
        /// <see cref="ArgumentNullException"/>.</param>
        public static SaveResetResult Apply(
            SaveData data,
            SaveResetTargets targets,
            IReadOnlyList<string> characterIdsToRemove,
            IReadOnlyCollection<string> protectedCharacterIds,
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

            // 실제로 지울 캐릭터 집합을 미리 확정한다 - 요청 ∩ 존재 ∖ 기본 보유.
            HashSet<string> removeSet = null;
            if ((effective & SaveResetTargets.Character) != 0)
            {
                removeSet = ResolveRemovableIds(data.characters, characterIdsToRemove, protectedCharacterIds);
            }

            bool removeCharacters = removeSet != null && removeSet.Count > 0;

            // 실제로 바뀌는 것이 하나도 없으면 저장하지 않는다. (아이템/재화/건축은 비트만으로 "적용"으로
            // 치지만, 캐릭터는 실제로 지울 대상이 있을 때만 적용이다.)
            if (!resetItems && !resetCurrency && !resetConstruction && !removeCharacters)
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

            List<CharacterSaveState> oldCharacters = null;
            List<RecoverySlotBackup> slotBackups = null;
            int removedCount = 0;

            if (resetItems) data.items = new List<InventoryItemState>();
            if (resetCurrency) data.currency = 0;
            if (resetConstruction)
            {
                data.buildingConstructions = new List<BuildingConstructionSaveState>();
                data.recruitmentCycles = new List<RecruitmentCycleSaveState>();
            }

            if (removeCharacters)
            {
                oldCharacters = data.characters;

                // 지울 대상을 뺀 새 목록으로 교체한다. 살아남는 캐릭터의 순서는 그대로 유지한다.
                var survivors = new List<CharacterSaveState>(oldCharacters.Count);
                foreach (CharacterSaveState state in oldCharacters)
                {
                    if (state != null && state.characterId != null && removeSet.Contains(state.characterId))
                    {
                        removedCount++;
                        continue; // 이 항목을 지운다 - 레벨·경험치·행동력도 이 객체와 함께 사라진다.
                    }

                    survivors.Add(state);
                }

                data.characters = survivors;

                // 지운 캐릭터가 회복 중이던 슬롯만 빈 상태로 바꾼다. 목록에서 빼지 않아 인덱스가 유지된다.
                if (data.recoverySlots != null)
                {
                    slotBackups = new List<RecoverySlotBackup>();
                    for (int i = 0; i < data.recoverySlots.Count; i++)
                    {
                        RecoverySlotSaveState slot = data.recoverySlots[i];
                        if (slot != null && slot.HasCharacter && removeSet.Contains(slot.characterId))
                        {
                            slotBackups.Add(RecoverySlotBackup.Capture(i, slot));
                            slot.Clear();
                        }
                    }
                }
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
                    oldConstructions, oldRecruitmentCycles, removeCharacters, oldCharacters, slotBackups);
                throw;
            }

            if (!saved)
            {
                Rollback(data, resetItems, oldItems, resetCurrency, oldCurrency, resetConstruction,
                    oldConstructions, oldRecruitmentCycles, removeCharacters, oldCharacters, slotBackups);
                return new SaveResetResult(SaveResetOutcome.SaveFailed, effective, 0);
            }

            SaveResetTargets applied = SaveResetTargets.None;
            if (resetItems) applied |= SaveResetTargets.Item;
            if (resetCurrency) applied |= SaveResetTargets.Currency;
            if (resetConstruction) applied |= SaveResetTargets.Construction;
            if (removeCharacters) applied |= SaveResetTargets.Character;

            return new SaveResetResult(SaveResetOutcome.Success, applied, removedCount);
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

        private static void Rollback(
            SaveData data,
            bool resetItems, List<InventoryItemState> oldItems,
            bool resetCurrency, int oldCurrency,
            bool resetConstruction, List<BuildingConstructionSaveState> oldConstructions,
            List<RecruitmentCycleSaveState> oldRecruitmentCycles,
            bool removeCharacters, List<CharacterSaveState> oldCharacters,
            List<RecoverySlotBackup> slotBackups)
        {
            if (resetItems) data.items = oldItems;
            if (resetCurrency) data.currency = oldCurrency;
            if (resetConstruction)
            {
                data.buildingConstructions = oldConstructions;
                data.recruitmentCycles = oldRecruitmentCycles;
            }

            if (removeCharacters)
            {
                data.characters = oldCharacters;

                // 빈 상태로 바꿨던 회복 슬롯을 원래 값으로 되돌린다(같은 객체, 같은 인덱스).
                if (slotBackups != null && data.recoverySlots != null)
                {
                    foreach (RecoverySlotBackup backup in slotBackups)
                    {
                        if (backup.Index >= 0 && backup.Index < data.recoverySlots.Count)
                        {
                            backup.RestoreTo(data.recoverySlots[backup.Index]);
                        }
                    }
                }
            }
        }

        /// <summary>빈 상태로 바꾼 회복 슬롯 한 칸의 원래 값. 저장 실패 시 <see cref="RestoreTo"/>로 되돌린다.</summary>
        private readonly struct RecoverySlotBackup
        {
            public int Index { get; }
            private readonly string characterId;
            private readonly int startStamina;
            private readonly string startedAtUtc;
            private readonly string completeAtUtc;
            private readonly bool completionNotified;

            private RecoverySlotBackup(int index, RecoverySlotSaveState slot)
            {
                Index = index;
                characterId = slot.characterId;
                startStamina = slot.startStamina;
                startedAtUtc = slot.startedAtUtc;
                completeAtUtc = slot.completeAtUtc;
                completionNotified = slot.completionNotified;
            }

            public static RecoverySlotBackup Capture(int index, RecoverySlotSaveState slot)
            {
                return new RecoverySlotBackup(index, slot);
            }

            public void RestoreTo(RecoverySlotSaveState slot)
            {
                if (slot == null) return;
                slot.characterId = characterId;
                slot.startStamina = startStamina;
                slot.startedAtUtc = startedAtUtc;
                slot.completeAtUtc = completeAtUtc;
                slot.completionNotified = completionNotified;
            }
        }
    }
}
