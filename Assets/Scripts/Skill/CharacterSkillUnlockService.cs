using System;
using System.Collections.Generic;
using Character;
using Common;

namespace Skill
{
    /// <summary>
    /// "이 캐릭터가 이 스킬을 지금 쓸 수 있는가"를 <b>계산해서</b> 답하는 자리.
    ///
    /// <b>해금 상태를 저장하지 않는다.</b> 해금은 어디에도 적히지 않으며, 물을 때마다 표(카탈로그)와
    /// 저장된 레벨로 다시 계산한다 - 플래그를 저장하기 시작하면 표의 필요 레벨을 고친 순간 저장 파일과
    /// 표가 어긋나고, 어느 쪽이 맞는지 정할 방법이 없어진다. 계산이 근거이므로 표를 고치면 답도 함께
    /// 바뀐다.
    ///
    /// <b>저장소도 씬도 모른다.</b> 생성될 때 받은 카탈로그 셋과 저장 문서 하나만 본다 - 그래야 시험이
    /// 디스크 없이 전부 돌아간다. <b>어떤 경로도 문서를 고치지 않고 항목을 만들지 않는다</b>(전부 조회다).
    ///
    /// <b>비교는 언제나 Ordinal 완전 일치다.</b> 'CatKnight'와 'catknight'는 서로 다른 캐릭터이며,
    /// 어느 쪽도 다른 쪽의 스킬을 열어 주지 않는다. 문자열을 다듬지도(Trim) 않는다 - 다른 카탈로그들과
    /// 같은 규칙이다.
    ///
    /// 하나라도 어긋나면 열리지 않는다:
    /// <list type="bullet">
    ///   <item>캐릭터가 <b>지금 쓰는 캐릭터 카탈로그</b>에 있어야 한다(예전 빌드에 남은 저장 전용 id는
    ///         스킬을 얻지 못한다).</item>
    ///   <item>저장 목록에 그 id의 항목이 있어야 한다(= 보유). 같은 id가 두 번 있으면 <b>먼저 나온
    ///         항목</b>이 근거다.</item>
    ///   <item>관계가 목록에 있고 두 식별자가 모두 있어야 한다.</item>
    ///   <item>그 스킬이 <b>정식 스킬 카탈로그</b>에 있어야 한다(관계만 있고 스킬이 없는 줄은 스킬이
    ///         아니다).</item>
    ///   <item>관계가 들고 있는 캐릭터/스킬 참조가 <b>비어 있지 않고</b>, 그 참조의 id가 관계에 적힌
    ///         id와 <b>일치</b>해야 한다 - 임포터가 같은 행에서 채운 연결이므로 어긋났다는 것은
    ///         데이터가 깨졌다는 뜻이다.</item>
    ///   <item>저장된 레벨(하한 1)이 필요 레벨 이상이어야 한다.</item>
    /// </list>
    ///
    /// <b>비어 있는 카탈로그는 오류가 아니다.</b> 지금 Skill.csv / CharacterSkill.csv에는 실제 행이
    /// 하나도 없으므로 생성 카탈로그도 비어 있고, 그때 모든 조회는 조용히 "없음"으로 답한다. 카탈로그가
    /// 아예 연결되지 않은 경우(null)도 같다 - 씬 구성이 덜 된 상태에서 터지는 것보다 "아무것도 없다"가
    /// 훨씬 다루기 쉬운 답이다.
    /// </summary>
    public sealed class CharacterSkillUnlockService
    {
        private static readonly IReadOnlyList<SkillDefinition> EmptySkills = new SkillDefinition[0];

        /// <summary>런타임 레벨의 하한. 저장 항목에 0이나 음수가 적혀 있어도 계산은 1로 본다 -
        /// 성장 계산(<see cref="CharacterProgressionService.MinimumLevel"/>)과 같은 하한이며,
        /// <b>저장 항목을 고치지는 않는다</b>.</summary>
        public const int MinimumLevel = CharacterProgressionService.MinimumLevel;

        private readonly CharacterCatalog characters;
        private readonly SkillCatalog skills;
        private readonly CharacterSkillCatalog relations;
        private readonly OwnedCharacterCollection owned;

        /// <param name="characters">이 게임의 활성 캐릭터 전체. null이면 어떤 캐릭터도 해금 대상이 아니다.</param>
        /// <param name="skills">정식 스킬 목록. null이면 열릴 스킬이 하나도 없다.</param>
        /// <param name="relations">캐릭터-스킬 관계 목록. null이면 볼 관계가 없다.</param>
        /// <param name="document">보유와 레벨을 담은 저장 문서. null이면 아무것도 보유하지 않은 것으로 다룬다.</param>
        public CharacterSkillUnlockService(
            CharacterCatalog characters,
            SkillCatalog skills,
            CharacterSkillCatalog relations,
            SaveData document)
        {
            this.characters = characters;
            this.skills = skills;
            this.relations = relations;

            // 보유 판정을 여기서 새로 쓰지 않는다 - "먼저 나온 항목이 근거이고 없으면 만들지 않는다"는
            // 규칙은 이미 한 곳에 있고, 그것을 그대로 빌려 쓴다.
            owned = new OwnedCharacterCollection(characters, document);
        }

        /// <summary>
        /// 이 캐릭터가 이 스킬을 지금 쓸 수 있는가. 어느 조건 하나라도 어긋나면 false이며,
        /// <b>어떤 값도 만들거나 고치지 않는다</b>.
        /// </summary>
        public bool IsUnlocked(string characterId, string skillId)
        {
            if (string.IsNullOrEmpty(skillId)) return false;
            if (!TryResolveCharacter(characterId, out _, out int level)) return false;

            IReadOnlyList<CharacterSkillDefinition> all = AllRelations;

            for (int i = 0; i < all.Count; i++)
            {
                CharacterSkillDefinition relation = all[i];
                if (!Matches(relation, characterId)) continue;
                if (!string.Equals(relation.SkillId, skillId, StringComparison.Ordinal)) continue;

                // 같은 짝이 두 번 담기지 않는 것은 관계 카탈로그가 이미 보장하므로, 처음 만난 것이
                // 이 짝의 전부다.
                return TryResolveSkill(relation, out _) && level >= relation.RequiredCharacterLevel;
            }

            return false;
        }

        /// <summary>
        /// 지금 열려 있는 스킬들을 <b>관계 카탈로그의 순서 그대로</b>. 없으면 빈 목록이며 null이 아니다.
        ///
        /// 돌려주는 것은 <b>정식 스킬 카탈로그의 정의</b>다 - 관계가 들고 있는 참조가 같은 id의 다른
        /// 에셋이어도, 받는 쪽은 언제나 목록에 있는 그 객체를 본다.
        /// </summary>
        public IReadOnlyList<SkillDefinition> GetUnlockedSkills(string characterId)
        {
            return Collect(characterId, unlocked: true);
        }

        /// <summary>아직 잠겨 있는 스킬들. 규칙은 <see cref="GetUnlockedSkills"/>와 같고 레벨 판정만
        /// 반대다 - <b>구조가 깨진 관계는 어느 쪽에도 나오지 않는다</b>(그것은 잠긴 스킬이 아니라
        /// 스킬이 아니다).</summary>
        public IReadOnlyList<SkillDefinition> GetLockedSkills(string characterId)
        {
            return Collect(characterId, unlocked: false);
        }

        /// <summary>
        /// 레벨이 <paramref name="previousLevel"/>에서 <paramref name="newLevel"/>로 오르는 동안
        /// <b>이번에 새로 열린</b> 스킬들. 판정은 <c>이전 &lt; 필요 &lt;= 이후</c> 하나다.
        ///
        /// 이전 레벨에서 이미 열려 있던 스킬은 <b>다시 나오지 않는다</b> - 처치를 반복할 때마다 같은
        /// 스킬이 계속 "새로 열렸다"고 나오면 그 신호는 아무 뜻도 갖지 못한다.
        ///
        /// <b>거꾸로거나 같은 구간은 빈 목록이다.</b> 레벨이 내려간 경우와 그대로인 경우에는 새로
        /// 열린 것이 없다. 1보다 작은 값은 계산할 때만 하한으로 보며 <b>저장 항목을 고치지 않는다</b>.
        ///
        /// 보유/카탈로그/참조 검사는 <see cref="GetUnlockedSkills"/>와 <b>똑같이</b> 지난다 - 여기만
        /// 느슨하면 성장 순간에 규칙을 우회해 스킬이 열린다.
        /// </summary>
        public IReadOnlyList<SkillDefinition> GetNewlyUnlockedSkills(
            string characterId, int previousLevel, int newLevel)
        {
            if (!TryResolveCharacter(characterId, out _, out _)) return EmptySkills;

            int previous = ClampLevel(previousLevel);
            int current = ClampLevel(newLevel);
            if (current <= previous) return EmptySkills;

            List<SkillDefinition> found = null;
            HashSet<string> seen = null;
            IReadOnlyList<CharacterSkillDefinition> all = AllRelations;

            for (int i = 0; i < all.Count; i++)
            {
                CharacterSkillDefinition relation = all[i];
                if (!Matches(relation, characterId)) continue;
                if (!TryResolveSkill(relation, out SkillDefinition skill)) continue;

                int required = relation.RequiredCharacterLevel;
                if (previous >= required || required > current) continue;

                Add(ref found, ref seen, skill);
            }

            return (IReadOnlyList<SkillDefinition>)found ?? EmptySkills;
        }

        // ---- 내부 ----

        private IReadOnlyList<CharacterSkillDefinition> AllRelations =>
            relations != null ? relations.Relations : Array.Empty<CharacterSkillDefinition>();

        private IReadOnlyList<SkillDefinition> Collect(string characterId, bool unlocked)
        {
            if (!TryResolveCharacter(characterId, out _, out int level)) return EmptySkills;

            List<SkillDefinition> found = null;
            HashSet<string> seen = null;
            IReadOnlyList<CharacterSkillDefinition> all = AllRelations;

            for (int i = 0; i < all.Count; i++)
            {
                CharacterSkillDefinition relation = all[i];
                if (!Matches(relation, characterId)) continue;
                if (!TryResolveSkill(relation, out SkillDefinition skill)) continue;

                if (level >= relation.RequiredCharacterLevel != unlocked) continue;

                Add(ref found, ref seen, skill);
            }

            return (IReadOnlyList<SkillDefinition>)found ?? EmptySkills;
        }

        /// <summary>같은 스킬을 두 번 담지 않는다. 관계 카탈로그가 같은 짝을 이미 걸러 주지만, 이
        /// 서비스가 받는 목록이 언제나 그 카탈로그라고 <b>가정하지 않는다</b> - 밖에서 만든 목록을
        /// 넘겨도 받는 쪽이 같은 스킬을 두 번 그리는 일은 없어야 한다.</summary>
        private static void Add(ref List<SkillDefinition> found, ref HashSet<string> seen, SkillDefinition skill)
        {
            found ??= new List<SkillDefinition>();
            seen ??= new HashSet<string>(StringComparer.Ordinal);

            if (seen.Add(skill.SkillId)) found.Add(skill);
        }

        /// <summary>
        /// 캐릭터를 <b>카탈로그와 저장 문서 양쪽에서</b> 확인하고 계산에 쓸 레벨을 낸다.
        ///
        /// 둘 다 필요한 이유가 다르다 - 카탈로그는 "지금 빌드에 있는 캐릭터인가"를, 저장 항목은
        /// "이 플레이어가 가지고 있는가"를 답한다. 예전 빌드에서 남은 저장 전용 id는 보유는 맞지만
        /// 지금 캐릭터가 아니므로 스킬을 얻지 못한다.
        /// </summary>
        private bool TryResolveCharacter(string characterId, out CharacterDefinition canonical, out int level)
        {
            canonical = null;
            level = MinimumLevel;

            if (string.IsNullOrEmpty(characterId)) return false;

            canonical = characters != null ? characters.Find(characterId) : null;
            if (canonical == null) return false;

            if (!owned.TryGetState(characterId, out CharacterSaveState state)) return false;

            level = ClampLevel(state.level);
            return true;
        }

        /// <summary>이 관계가 이 캐릭터의 <b>쓸 수 있는 한 줄</b>인지. 두 식별자가 모두 있고 앞쪽이
        /// 이 캐릭터여야 한다.</summary>
        private static bool Matches(CharacterSkillDefinition relation, string characterId)
        {
            return relation != null
                   && relation.IsValid
                   && string.Equals(relation.CharacterId, characterId, StringComparison.Ordinal);
        }

        /// <summary>
        /// 관계가 가리키는 스킬을 <b>정식 카탈로그의 정의</b>로 푼다. 참조가 비어 있거나 참조의 id가
        /// 관계에 적힌 id와 어긋나면 실패다.
        ///
        /// 참조를 <b>id로</b> 검사하는 이유는, 임포터가 같은 행에서 채운 연결이라 어긋날 수 없어야
        /// 하는데 어긋났다면 그것은 데이터가 깨졌다는 뜻이기 때문이다. 참조가 <b>같은 객체인지</b>까지
        /// 요구하지 않는 것은, 같은 id의 수동 에셋을 물고 있어도 가리키는 스킬은 하나이기 때문이다.
        /// </summary>
        private bool TryResolveSkill(CharacterSkillDefinition relation, out SkillDefinition canonical)
        {
            canonical = null;

            // 관계가 들고 있는 캐릭터 참조도 함께 본다 - 앞쪽이 어긋난 줄은 뒤쪽이 맞아도 믿을 수 없다.
            CharacterDefinition character = relation.Character;
            if (character == null) return false;
            if (!string.Equals(character.CharacterId, relation.CharacterId, StringComparison.Ordinal)) return false;

            SkillDefinition skill = relation.Skill;
            if (skill == null) return false;
            if (!string.Equals(skill.SkillId, relation.SkillId, StringComparison.Ordinal)) return false;

            canonical = skills != null ? skills.Find(relation.SkillId) : null;
            return canonical != null;
        }

        private static int ClampLevel(int level)
        {
            return level < MinimumLevel ? MinimumLevel : level;
        }
    }
}
