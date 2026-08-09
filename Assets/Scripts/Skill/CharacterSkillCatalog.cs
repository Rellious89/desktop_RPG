using System;
using System.Collections.Generic;
using UnityEngine;

namespace Skill
{
    /// <summary>
    /// 캐릭터-스킬 관계 목록의 <b>순서와 구성</b>을 소유하는 에셋. 읽는 쪽은 프로젝트를 뒤져 관계를
    /// 모으지 않고 이 에셋 하나만 읽는다.
    ///
    /// <b>비어 있는 것이 정상적인 상태다.</b> 지금 CharacterSkill.csv에는 실제 관계 행이 하나도
    /// 없으므로 이 카탈로그도 비어 있으며, 그것은 오류가 아니다.
    ///
    /// <b>담기는 것은 활성 관계뿐이다.</b> enabled=1인 행만, display_order 오름차순 → 같으면
    /// character_id → 같으면 skill_id Ordinal 오름차순으로 임포터가 넣는다.
    ///
    /// <b>여기서 스킬을 열어 주지 않는다.</b> 이 목록은 "누가 어떤 스킬을 가진다고 표에 적혀 있는가"
    /// 뿐이고, 필요 레벨을 보고 사용 가능 여부를 정하는 코드는 어디에도 없다.
    /// </summary>
    [CreateAssetMenu(fileName = "CharacterSkillCatalog", menuName = "Skill/Character Skill Catalog")]
    public class CharacterSkillCatalog : ScriptableObject
    {
        [Tooltip("관계를 나올 순서대로 넣는다. 비어 있는 칸/식별자가 반쪽인 관계/같은 짝이 두 번 " +
                 "들어온 관계는 자동으로 제외되고 경고가 남는다.")]
        [SerializeField] private List<CharacterSkillDefinition> relations = new List<CharacterSkillDefinition>();

        /// <summary>검사를 통과한 항목만 작성 순서대로 담아 둔 캐시.</summary>
        private readonly List<CharacterSkillDefinition> validRelations = new List<CharacterSkillDefinition>();

        private bool built;

        /// <summary>쓸 수 있는 관계들을 <b>작성 순서 그대로</b> 돌려준다. 항목이 하나도 없으면 빈
        /// 목록이며 null이 아니다.</summary>
        public IReadOnlyList<CharacterSkillDefinition> Relations
        {
            get
            {
                EnsureBuilt();
                return validRelations;
            }
        }

        /// <summary>쓸 수 있는 관계 수.</summary>
        public int Count
        {
            get
            {
                EnsureBuilt();
                return validRelations.Count;
            }
        }

        /// <summary>한 캐릭터가 가진 관계들을 <b>목록 순서 그대로</b> 모아 돌려준다. 없으면 빈
        /// 목록이다. 비교는 Ordinal 완전 일치이며 넘어온 문자열을 다듬지 않는다.
        ///
        /// <b>새 목록을 만들어 돌려준다</b> - 내부 캐시를 그대로 넘기면 호출하는 쪽이 고칠 수 있다.</summary>
        public List<CharacterSkillDefinition> FindByCharacter(string characterId)
        {
            var found = new List<CharacterSkillDefinition>();
            if (string.IsNullOrWhiteSpace(characterId)) return found;

            EnsureBuilt();

            for (int i = 0; i < validRelations.Count; i++)
            {
                if (string.Equals(validRelations[i].CharacterId, characterId, StringComparison.Ordinal))
                {
                    found.Add(validRelations[i]);
                }
            }

            return found;
        }

        /// <summary>한 스킬을 가진 관계들을 <b>목록 순서 그대로</b> 모아 돌려준다. 규칙은
        /// <see cref="FindByCharacter"/>와 같다.</summary>
        public List<CharacterSkillDefinition> FindBySkill(string skillId)
        {
            var found = new List<CharacterSkillDefinition>();
            if (string.IsNullOrWhiteSpace(skillId)) return found;

            EnsureBuilt();

            for (int i = 0; i < validRelations.Count; i++)
            {
                if (string.Equals(validRelations[i].SkillId, skillId, StringComparison.Ordinal))
                {
                    found.Add(validRelations[i]);
                }
            }

            return found;
        }

        /// <summary>다음 조회 때 검사를 다시 하도록 표시한다.</summary>
        public void MarkDirty()
        {
            built = false;
        }

        private void OnEnable()
        {
            built = false;
        }

        private void EnsureBuilt()
        {
            if (built) return;
            built = true;

            validRelations.Clear();
            if (relations == null) return;

            var seenPairs = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < relations.Count; i++)
            {
                CharacterSkillDefinition relation = relations[i];

                if (relation == null)
                {
                    Debug.LogWarning($"[CharacterSkillCatalog] '{name}': {i}번 항목이 비어 있어 목록에서 제외합니다.", this);
                    continue;
                }

                if (!relation.IsValid)
                {
                    Debug.LogError($"[CharacterSkillCatalog] '{name}': {i}번 항목('{relation.name}')에 Character Id " +
                                   "또는 Skill Id가 없어 목록에서 제외합니다 - 관계는 두 식별자가 모두 있어야 " +
                                   "성립합니다.", relation);
                    continue;
                }

                if (!seenPairs.Add(relation.PairId))
                {
                    Debug.LogError($"[CharacterSkillCatalog] '{name}': {i}번 항목('{relation.name}')의 짝 " +
                                   $"'{relation.CharacterId}' + '{relation.SkillId}'가 앞선 항목과 겹쳐 목록에서 " +
                                   "제외합니다 - 먼저 작성된 관계가 남습니다(대소문자는 구분합니다).", relation);
                    continue;
                }

                validRelations.Add(relation);
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            built = false;
        }
#endif
    }
}
