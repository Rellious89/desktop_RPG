using System;
using System.Collections.Generic;
using UnityEngine;

namespace Recruitment
{
    /// <summary>
    /// 모집 창구 목록의 <b>순서와 구성</b>을 소유하는 에셋. 이 표에는 display_order 칸이 있으므로
    /// 다른 도메인과 같은 규칙으로 담긴다 - 활성 행만, display_order 오름차순 →
    /// 같으면 recruitment_access_id Ordinal 오름차순으로 임포터가 넣는다.
    ///
    /// <b>걸러내기는 여기서 한 번만 한다.</b> 비어 있는 칸, 식별자나 모집 키가 없는 행, 앞선 항목과
    /// id가 겹치는 행은 제외한다.
    /// </summary>
    [CreateAssetMenu(fileName = "RecruitmentAccessCatalog", menuName = "Recruitment/Recruitment Access Catalog")]
    public class RecruitmentAccessCatalog : ScriptableObject
    {
        [Tooltip("모집 창구를 나올 순서대로 넣는다. 비어 있는 칸/식별자나 모집 키가 없는 행/" +
                 "id가 겹치는 행은 자동으로 제외되고 경고가 남는다.")]
        [SerializeField] private List<RecruitmentAccessDefinition> accesses = new List<RecruitmentAccessDefinition>();

        private readonly List<RecruitmentAccessDefinition> valid = new List<RecruitmentAccessDefinition>();

        private bool built;

        /// <summary>쓸 수 있는 창구들을 <b>작성 순서 그대로</b>. 비어 있으면 빈 목록이며 null이 아니다.</summary>
        public IReadOnlyList<RecruitmentAccessDefinition> Accesses
        {
            get
            {
                EnsureBuilt();
                return valid;
            }
        }

        /// <summary>쓸 수 있는 창구 수.</summary>
        public int Count
        {
            get
            {
                EnsureBuilt();
                return valid.Count;
            }
        }

        /// <summary>식별자로 창구를 찾는다. 없으면 null이며 <b>넘어온 문자열을 손대지 않고</b> 비교한다.</summary>
        public RecruitmentAccessDefinition Find(string recruitmentAccessId)
        {
            if (string.IsNullOrWhiteSpace(recruitmentAccessId)) return null;

            EnsureBuilt();

            for (int i = 0; i < valid.Count; i++)
            {
                if (string.Equals(valid[i].RecruitmentAccessId, recruitmentAccessId, StringComparison.Ordinal))
                {
                    return valid[i];
                }
            }

            return null;
        }

        /// <summary>
        /// 대상(종류 + id)에 붙어 있는 창구를 찾는다. 없으면 null이다.
        ///
        /// <b>여럿이면 목록의 앞선 것을 돌려준다.</b> 목록은 display_order 오름차순이므로 "가장 먼저
        /// 보여야 할 창구"가 곧 답이 되며, 어느 것을 고를지가 조회 순서에 따라 달라지지 않는다.
        /// </summary>
        public RecruitmentAccessDefinition FindBySource(string sourceType, string sourceId)
        {
            if (string.IsNullOrWhiteSpace(sourceType) || string.IsNullOrWhiteSpace(sourceId)) return null;

            EnsureBuilt();

            for (int i = 0; i < valid.Count; i++)
            {
                if (valid[i].MatchesSource(sourceType, sourceId)) return valid[i];
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
            if (accesses == null) return;

            var seen = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < accesses.Count; i++)
            {
                RecruitmentAccessDefinition entry = accesses[i];

                if (entry == null)
                {
                    Debug.LogWarning($"[RecruitmentAccessCatalog] '{name}': {i}번 항목이 비어 있어 목록에서 제외합니다.", this);
                    continue;
                }

                if (!entry.IsValid)
                {
                    Debug.LogError($"[RecruitmentAccessCatalog] '{name}': {i}번 항목('{entry.name}')에 " +
                                   "Recruitment Access Id 또는 Recruitment Type Id가 없어 목록에서 제외합니다.", entry);
                    continue;
                }

                if (!seen.Add(entry.RecruitmentAccessId))
                {
                    Debug.LogError($"[RecruitmentAccessCatalog] '{name}': {i}번 항목('{entry.name}')의 " +
                                   $"Recruitment Access Id '{entry.RecruitmentAccessId}'가 앞선 항목과 겹쳐 " +
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
