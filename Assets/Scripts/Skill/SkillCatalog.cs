using System;
using System.Collections.Generic;
using UnityEngine;

namespace Skill
{
    /// <summary>
    /// 스킬 목록의 <b>순서와 구성</b>을 소유하는 에셋. <see cref="Inventory.ItemCatalog"/>와 같은
    /// 역할이며, 읽는 쪽은 프로젝트를 뒤져 스킬을 모으지 않고 이 에셋 하나만 읽는다.
    ///
    /// <b>비어 있는 것이 정상적인 상태다.</b> 지금 Skill.csv에는 실제 스킬 행이 하나도 없으므로 이
    /// 카탈로그도 비어 있으며, 그것은 오류가 아니라 "아직 스킬을 정하지 않았다"는 뜻이다.
    ///
    /// <b>담기는 것은 활성 스킬뿐이다.</b> enabled=1인 행만, display_order 오름차순 →
    /// 같으면 skill_id Ordinal 오름차순으로 임포터가 넣는다.
    /// </summary>
    [CreateAssetMenu(fileName = "SkillCatalog", menuName = "Skill/Skill Catalog")]
    public class SkillCatalog : ScriptableObject
    {
        [Tooltip("스킬을 나올 순서대로 넣는다. 비어 있는 칸/식별자가 없는 스킬/id가 겹치는 스킬은 " +
                 "자동으로 제외되고 경고가 남는다.")]
        [SerializeField] private List<SkillDefinition> skills = new List<SkillDefinition>();

        /// <summary>검사를 통과한 항목만 작성 순서대로 담아 둔 캐시. 조회할 때마다 새로 만들지 않는다.</summary>
        private readonly List<SkillDefinition> validSkills = new List<SkillDefinition>();

        private bool built;

        /// <summary>쓸 수 있는 스킬들을 <b>작성 순서 그대로</b> 돌려준다. 항목이 하나도 없으면 빈
        /// 목록이며 null이 아니다.</summary>
        public IReadOnlyList<SkillDefinition> Skills
        {
            get
            {
                EnsureBuilt();
                return validSkills;
            }
        }

        /// <summary>쓸 수 있는 스킬 수.</summary>
        public int Count
        {
            get
            {
                EnsureBuilt();
                return validSkills.Count;
            }
        }

        /// <summary>식별자로 스킬을 찾는다. 없으면 null이다. <b>넘어온 문자열을 손대지 않고 그대로
        /// 비교한다</b> - 대소문자를 구분하고 앞뒤 공백도 떼지 않는다.</summary>
        public SkillDefinition Find(string skillId)
        {
            if (string.IsNullOrWhiteSpace(skillId)) return null;

            EnsureBuilt();

            for (int i = 0; i < validSkills.Count; i++)
            {
                if (string.Equals(validSkills[i].SkillId, skillId, StringComparison.Ordinal))
                {
                    return validSkills[i];
                }
            }

            return null;
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

            validSkills.Clear();
            if (skills == null) return;

            var seenIds = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < skills.Count; i++)
            {
                SkillDefinition skill = skills[i];

                if (skill == null)
                {
                    Debug.LogWarning($"[SkillCatalog] '{name}': {i}번 항목이 비어 있어 목록에서 제외합니다.", this);
                    continue;
                }

                if (!skill.IsValid)
                {
                    Debug.LogError($"[SkillCatalog] '{name}': {i}번 항목('{skill.name}')에 Skill Id가 없어 " +
                                   "목록에서 제외합니다 - 에셋에서 식별자를 직접 지정하세요.", skill);
                    continue;
                }

                if (!seenIds.Add(skill.SkillId))
                {
                    Debug.LogError($"[SkillCatalog] '{name}': {i}번 항목('{skill.name}')의 Skill Id " +
                                   $"'{skill.SkillId}'가 앞선 항목과 겹쳐 목록에서 제외합니다 - " +
                                   "먼저 작성된 스킬이 남습니다(대소문자는 구분합니다).", skill);
                    continue;
                }

                validSkills.Add(skill);
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
