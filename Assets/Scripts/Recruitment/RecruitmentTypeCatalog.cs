using System;
using System.Collections.Generic;
using UnityEngine;

namespace Recruitment
{
    /// <summary>
    /// 모집 종류 목록의 <b>순서와 구성</b>을 소유하는 에셋. 담기는 것은 활성 행뿐이며 순서는 CSV에
    /// 적힌 그대로다 - 이 표에도 display_order 칸이 없다(<see cref="CharacterAcquisitionCatalog"/>와
    /// 같은 이유다).
    ///
    /// <b>걸러내기는 여기서 한 번만 한다.</b> 비어 있는 칸, 식별자가 없는 행, 앞선 항목과 id가
    /// 겹치는 행은 제외하고 남은 항목을 작성 순서 그대로 돌려준다. 비교는
    /// <see cref="StringComparer.Ordinal"/>이다.
    /// </summary>
    [CreateAssetMenu(fileName = "RecruitmentTypeCatalog", menuName = "Recruitment/Recruitment Type Catalog")]
    public class RecruitmentTypeCatalog : ScriptableObject
    {
        [Tooltip("모집 종류를 CSV에 적힌 순서대로 넣는다. 비어 있는 칸/식별자가 없는 행/id가 겹치는 " +
                 "행은 자동으로 제외되고 경고가 남는다.")]
        [SerializeField] private List<RecruitmentTypeDefinition> types = new List<RecruitmentTypeDefinition>();

        private readonly List<RecruitmentTypeDefinition> valid = new List<RecruitmentTypeDefinition>();

        private bool built;

        /// <summary>쓸 수 있는 모집들을 <b>작성 순서 그대로</b>. 비어 있으면 빈 목록이며 null이 아니다.</summary>
        public IReadOnlyList<RecruitmentTypeDefinition> Types
        {
            get
            {
                EnsureBuilt();
                return valid;
            }
        }

        /// <summary>쓸 수 있는 모집 수.</summary>
        public int Count
        {
            get
            {
                EnsureBuilt();
                return valid.Count;
            }
        }

        /// <summary>식별자로 모집을 찾는다. 없으면 null이며 <b>넘어온 문자열을 손대지 않고</b> 비교한다.</summary>
        public RecruitmentTypeDefinition Find(string recruitmentTypeId)
        {
            if (string.IsNullOrWhiteSpace(recruitmentTypeId)) return null;

            EnsureBuilt();

            for (int i = 0; i < valid.Count; i++)
            {
                if (string.Equals(valid[i].RecruitmentTypeId, recruitmentTypeId, StringComparison.Ordinal))
                {
                    return valid[i];
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

            valid.Clear();
            if (types == null) return;

            var seen = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < types.Count; i++)
            {
                RecruitmentTypeDefinition entry = types[i];

                if (entry == null)
                {
                    Debug.LogWarning($"[RecruitmentTypeCatalog] '{name}': {i}번 항목이 비어 있어 목록에서 제외합니다.", this);
                    continue;
                }

                if (!entry.IsValid)
                {
                    Debug.LogError($"[RecruitmentTypeCatalog] '{name}': {i}번 항목('{entry.name}')에 " +
                                   "Recruitment Type Id가 없어 목록에서 제외합니다.", entry);
                    continue;
                }

                if (!seen.Add(entry.RecruitmentTypeId))
                {
                    Debug.LogError($"[RecruitmentTypeCatalog] '{name}': {i}번 항목('{entry.name}')의 " +
                                   $"Recruitment Type Id '{entry.RecruitmentTypeId}'가 앞선 항목과 겹쳐 " +
                                   "목록에서 제외합니다 - 먼저 작성된 행이 남습니다.", entry);
                    continue;
                }

                valid.Add(entry);
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
