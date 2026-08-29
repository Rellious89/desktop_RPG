using System;
using System.Collections.Generic;
using Character;

namespace Recruitment
{
    /// <summary>뽑기 한 번의 결과가 <b>무엇인지</b>. 후보가 없는 것을 null이나 빈 문자열로 흘려보내지
    /// 않는다 - "아무도 뽑지 못했다"는 오류가 아니라 <b>정상적인 답</b>이며, 부르는 쪽이 그 상태를
    /// 반드시 마주 보게 한다.</summary>
    public enum RecruitmentSelectionOutcome
    {
        /// <summary>후보 한 명을 뽑았다.</summary>
        Selected = 0,

        /// <summary>뽑을 수 있는 후보가 하나도 없었다. 표가 비었거나, 전부 꺼져 있거나, 조건이
        /// 걸렸거나, 이미 다 보유한 경우다 - 어느 쪽이든 결과는 같다.</summary>
        NoEligibleCandidate = 1,
    }

    /// <summary>
    /// 뽑기 한 번의 결과. <b>값 타입</b>이며 아무것도 소유하지 않는다.
    ///
    /// <see cref="Roll"/>과 <see cref="TotalWeight"/>를 함께 담는 이유는 시험과 로그가 "왜 이 사람이
    /// 뽑혔는가"를 되짚을 수 있어야 하기 때문이다 - 경계값을 확인하려면 굴린 수를 볼 수 있어야 한다.
    /// </summary>
    public readonly struct RecruitmentSelection
    {
        private RecruitmentSelection(
            RecruitmentSelectionOutcome outcome, RecruitmentPoolEntryDefinition entry, int totalWeight, int roll)
        {
            Outcome = outcome;
            Entry = entry;
            TotalWeight = totalWeight;
            Roll = roll;
        }

        public RecruitmentSelectionOutcome Outcome { get; }

        /// <summary>뽑힌 후보 칸. 뽑지 못했으면 null이다.</summary>
        public RecruitmentPoolEntryDefinition Entry { get; }

        /// <summary>뽑기에 참여한 칸들의 가중치 합. 후보가 없었으면 0이다.</summary>
        public int TotalWeight { get; }

        /// <summary>실제로 굴린 수(0 이상 <see cref="TotalWeight"/> 미만). 후보가 없었으면 0이다.</summary>
        public int Roll { get; }

        /// <summary>후보를 뽑았는지.</summary>
        public bool IsSelected => Outcome == RecruitmentSelectionOutcome.Selected;

        /// <summary>뽑힌 캐릭터의 Character Id. 뽑지 못했으면 빈 문자열이며 <b>null이 아니다</b>.</summary>
        public string CharacterId => Entry != null ? Entry.CharacterId : string.Empty;

        /// <summary>뽑힌 캐릭터의 정의. 참조가 끊겨 있으면 null이며, 그때도
        /// <see cref="CharacterId"/>는 남는다 - 판정의 기준은 언제나 id다.</summary>
        public CharacterDefinition Character => Entry != null ? Entry.Character : null;

        internal static RecruitmentSelection None =>
            new RecruitmentSelection(RecruitmentSelectionOutcome.NoEligibleCandidate, null, 0, 0);

        internal static RecruitmentSelection Of(RecruitmentPoolEntryDefinition entry, int totalWeight, int roll)
        {
            return new RecruitmentSelection(RecruitmentSelectionOutcome.Selected, entry, totalWeight, roll);
        }
    }

    /// <summary>
    /// 모집 후보를 <b>가중치로</b> 한 명 고르는 자리.
    ///
    /// <b>아무것도 바꾸지 않는다.</b> 저장 문서를 읽지도 쓰지도 않고(보유 여부는
    /// <see cref="IRecruitmentOwnership"/>로 넘겨받는다), 카탈로그도 정의 에셋도 고치지 않으며,
    /// 캐릭터를 지급하지도 알림을 내지도 UI를 건드리지도 않는다 - 이 클래스가 하는 일은 "누가
    /// 뽑혔는가"를 <b>값으로 돌려주는 것</b>뿐이고, 그 결과로 무엇을 할지는 바깥이 정한다.
    /// 실제 획득은 이 단계에 아예 존재하지 않는다.
    ///
    /// <b>후보에서 빠지는 이유는 여섯이다.</b> 순서대로 본다:
    /// <list type="number">
    ///   <item>칸이 비었거나 <b>가중치가 1 미만</b>이다(<see cref="RecruitmentPoolEntryDefinition.IsValid"/>).</item>
    ///   <item>표가 그 칸을 <b>꺼 두었다</b>.</item>
    ///   <item>같은 캐릭터가 <b>이미 후보에 들어 있다</b> - 앞선 칸을 남기고 뒤의 칸을 뺀다.</item>
    ///   <item>그 캐릭터의 <b>획득 방식 행이 없거나 꺼져 있거나</b>, 런타임이 모르는 낱말이다.</item>
    ///   <item>획득에 <b>조건이 걸려 있는데 영구 등장 해금 기록이 없다</b>.</item>
    ///   <item>이미 <b>보유한 캐릭터</b>이고 중복 모집이 허용되지 않았다.</item>
    /// </list>
    /// 넷째가 "모르면 뺀다"인 것은 의도적이다 - 표가 앞서가고 코드가 따라오는 동안, 코드가 뜻을
    /// 추측해 엉뚱한 방식으로 지급하는 것보다 그 캐릭터가 아직 안 나오는 편이 훨씬 낫다.
    /// </summary>
    public static class RecruitmentCandidateSelector
    {
        private static readonly RecruitmentPoolEntryDefinition[] EmptyCandidates = new RecruitmentPoolEntryDefinition[0];

        /// <summary>
        /// 한 모집에서 지금 뽑을 수 있는 후보들을 <b>풀에 적힌 순서 그대로</b> 돌려준다.
        /// 없으면 빈 목록이며 null이 아니다. <b>호출할 때마다 새 목록을 만든다</b>.
        ///
        /// 뽑기와 <b>같은 규칙</b>을 쓰는 유일한 자리다 - <see cref="Select"/>가 이 함수를 그대로
        /// 부르므로, "후보 목록"과 "뽑힐 수 있는 사람"이 어긋날 수 없다.
        /// </summary>
        public static IReadOnlyList<RecruitmentPoolEntryDefinition> CollectEligible(
            string recruitmentTypeId,
            RecruitmentPoolCatalog pool,
            CharacterAcquisitionCatalog acquisitions,
            IRecruitmentOwnership ownership,
            Func<string, bool> isPermanentlyUnlocked = null)
        {
            if (string.IsNullOrWhiteSpace(recruitmentTypeId) || pool == null) return EmptyCandidates;

            IReadOnlyList<RecruitmentPoolEntryDefinition> entries = pool.EntriesFor(recruitmentTypeId);
            if (entries == null || entries.Count == 0) return EmptyCandidates;

            var eligible = new List<RecruitmentPoolEntryDefinition>(entries.Count);
            var seenCharacters = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < entries.Count; i++)
            {
                RecruitmentPoolEntryDefinition entry = entries[i];

                if (!PassesEntryRules(entry)) continue;
                if (!seenCharacters.Add(entry.CharacterId)) continue;
                if (!PassesCharacterRules(entry.CharacterId, acquisitions, ownership, isPermanentlyUnlocked)) continue;

                eligible.Add(entry);
            }

            return eligible;
        }

        /// <summary>
        /// 지금 뽑을 수 있는 후보가 <b>한 명이라도 있는지</b>만 답한다. <see cref="CollectEligible"/>와
        /// <b>글자 그대로 같은 규칙</b>을 쓰지만(둘 다 <see cref="PassesEntryRules"/>와
        /// <see cref="PassesCharacterRules"/>만을 본다) 목록도 HashSet도 만들지 않는다 - 매 프레임
        /// 물어보는 화면이 쓰레기를 쌓지 않게 하기 위해서다.
        ///
        /// 겹친 캐릭터를 거르지 <b>않는데도</b> 답이 같은 이유는, 겹침 판정이 칸 규칙을 통과한 뒤에야
        /// 일어나기 때문이다 - 어떤 칸이 모든 규칙을 통과했다면 그 캐릭터의 <b>첫 번째</b> 칸도
        /// 반드시 통과하므로, <see cref="CollectEligible"/>의 목록도 비어 있지 않다.
        ///
        /// <b>아무것도 바꾸지 않는다.</b> 난수를 굴리지 않고, 저장 문서도 모집 주기도 건드리지 않는다.
        /// </summary>
        public static bool HasEligibleCandidate(
            string recruitmentTypeId,
            RecruitmentPoolCatalog pool,
            CharacterAcquisitionCatalog acquisitions,
            IRecruitmentOwnership ownership,
            Func<string, bool> isPermanentlyUnlocked = null)
        {
            if (string.IsNullOrWhiteSpace(recruitmentTypeId) || pool == null) return false;

            // EntriesFor는 부를 때마다 새 목록을 만든다. 여기서는 모집 종류를 직접 걸러, 카탈로그가
            // 이미 들고 있는 목록을 그대로 훑는다 - 걸러내는 기준은 EntriesFor와 같은 BelongsTo다.
            IReadOnlyList<RecruitmentPoolEntryDefinition> entries = pool.Entries;
            if (entries == null) return false;

            for (int i = 0; i < entries.Count; i++)
            {
                RecruitmentPoolEntryDefinition entry = entries[i];

                if (entry == null || !entry.BelongsTo(recruitmentTypeId)) continue;
                if (!PassesEntryRules(entry)) continue;
                if (!PassesCharacterRules(entry.CharacterId, acquisitions, ownership, isPermanentlyUnlocked)) continue;

                return true;
            }

            return false;
        }

        /// <summary>칸 자체가 뽑기에 참여할 수 있는지 - 비었거나 가중치가 1 미만이거나 꺼져 있으면 안 된다.</summary>
        private static bool PassesEntryRules(RecruitmentPoolEntryDefinition entry)
        {
            return entry != null && entry.IsValid && entry.Enabled;
        }

        /// <summary>그 캐릭터를 지금 지급할 수 있는지 - 획득 방식과 보유 여부를 본다.</summary>
        private static bool PassesCharacterRules(
            string characterId, CharacterAcquisitionCatalog acquisitions, IRecruitmentOwnership ownership,
            Func<string, bool> isPermanentlyUnlocked)
        {
            CharacterAcquisitionDefinition acquisition =
                acquisitions != null ? acquisitions.FindByCharacterId(characterId) : null;

            // 획득 방식을 모르는 캐릭터는 뽑지 않는다 - 뽑아 놓고 줄 수 없는 것이 더 나쁘다.
            if (acquisition == null) return false;
            if (!acquisition.Enabled) return false;
            if (!acquisition.IsRecruitable) return false;
            if (acquisition.HasCondition && (isPermanentlyUnlocked == null || !isPermanentlyUnlocked(characterId))) return false;

            bool owned = ownership != null && ownership.IsOwned(characterId);
            return !owned || acquisition.AllowDuplicateRecruitment;
        }

        /// <summary>
        /// 한 모집에서 후보 한 명을 가중치로 고른다. 후보가 하나도 없으면
        /// <see cref="RecruitmentSelectionOutcome.NoEligibleCandidate"/>를 돌려준다 - <b>예외를 던지지
        /// 않는다</b>(다 가진 상태는 오류가 아니다).
        ///
        /// <b>경계 규칙은 하나뿐이다.</b> 후보를 순서대로 늘어놓고 가중치를 누적해, 굴린 수
        /// <c>roll</c>이 처음으로 누적 합보다 작아지는 칸을 고른다 - 즉 <c>i</c>번 칸이 차지하는 구간은
        /// <c>[앞선 합, 앞선 합 + 가중치)</c>이며 <b>왼쪽 끝은 포함, 오른쪽 끝은 제외</b>다. 그래서
        /// 가중치 100/60인 두 칸에서 <c>roll=99</c>는 첫째, <c>roll=100</c>은 둘째다.
        ///
        /// <paramref name="random"/>이 약속을 어기고 범위 밖의 수를 주면 <b>범위 안으로 조인다</b> -
        /// 뽑기가 아무도 고르지 못한 채 끝나는 경로를 만들지 않기 위해서다(후보가 있는데 결과가
        /// 없는 상태는 부르는 쪽이 다룰 수 없다).
        /// </summary>
        public static RecruitmentSelection Select(
            string recruitmentTypeId,
            RecruitmentPoolCatalog pool,
            CharacterAcquisitionCatalog acquisitions,
            IRecruitmentOwnership ownership,
            IRecruitmentRandom random,
            Func<string, bool> isPermanentlyUnlocked = null)
        {
            IReadOnlyList<RecruitmentPoolEntryDefinition> candidates =
                CollectEligible(recruitmentTypeId, pool, acquisitions, ownership, isPermanentlyUnlocked);

            return SelectFrom(candidates, random);
        }

        /// <summary>
        /// 이미 걸러 둔 후보 목록에서 하나를 고른다. <see cref="Select"/>가 쓰는 <b>같은</b> 경계
        /// 규칙이며, 후보를 두 번 모으지 않고 결과만 다시 뽑고 싶을 때 쓴다.
        /// </summary>
        public static RecruitmentSelection SelectFrom(
            IReadOnlyList<RecruitmentPoolEntryDefinition> candidates, IRecruitmentRandom random)
        {
            if (candidates == null || candidates.Count == 0) return RecruitmentSelection.None;

            int totalWeight = 0;
            for (int i = 0; i < candidates.Count; i++) totalWeight += candidates[i].Weight;

            // 후보가 있으면 가중치 합은 언제나 1 이상이다(1 미만인 칸은 후보가 되지 못한다).
            if (totalWeight <= 0) return RecruitmentSelection.None;

            int roll = random != null ? random.Next(totalWeight) : 0;
            if (roll < 0) roll = 0;
            if (roll >= totalWeight) roll = totalWeight - 1;

            int cumulative = 0;
            for (int i = 0; i < candidates.Count; i++)
            {
                cumulative += candidates[i].Weight;
                if (roll < cumulative) return RecruitmentSelection.Of(candidates[i], totalWeight, roll);
            }

            // 누적 합이 곧 전체 합이고 roll은 그보다 작으므로 여기에 닿을 수 없다. 그래도 마지막 칸을
            // 돌려주어, 계산이 틀리더라도 "후보가 있는데 아무도 뽑히지 않는" 상태는 만들지 않는다.
            return RecruitmentSelection.Of(candidates[candidates.Count - 1], totalWeight, roll);
        }
    }
}
