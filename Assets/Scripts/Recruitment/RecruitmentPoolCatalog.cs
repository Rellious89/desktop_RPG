using System;
using System.Collections.Generic;
using UnityEngine;

namespace Recruitment
{
    /// <summary>
    /// 모집 후보 칸 목록의 <b>순서와 구성</b>을 소유하는 에셋.
    ///
    /// <b>순서가 결과를 정한다.</b> 뽑기는 이 목록의 순서대로 가중치를 누적해 경계를 만들기 때문에
    /// (<see cref="RecruitmentCandidateSelector"/>), 목록 순서가 바뀌면 같은 난수가 다른 캐릭터를
    /// 고른다 - 그래서 순서는 <b>CSV에 적힌 그대로</b>이며 여기서 다시 정렬하지 않는다. 확률 자체는
    /// 순서와 무관하지만, "같은 씨앗이면 같은 결과"라는 성질은 순서에 달려 있다.
    ///
    /// <b>걸러내기는 여기서 한 번만 한다.</b> 비어 있는 칸, 짝을 특정할 수 없는 행(두 id 중 하나가
    /// 빈 행), 앞선 항목과 짝 키가 겹치는 행은 제외한다. <b>가중치가 잘못된 칸은 여기서 빼지 않는다</b> -
    /// 목록에 무엇이 들어 있는지와 그중 무엇을 뽑을 수 있는지는 다른 판정이고, 후자는 뽑기가 한다.
    /// </summary>
    [CreateAssetMenu(fileName = "RecruitmentPoolCatalog", menuName = "Recruitment/Recruitment Pool Catalog")]
    public class RecruitmentPoolCatalog : ScriptableObject
    {
        private static readonly RecruitmentPoolEntryDefinition[] EmptyEntries = new RecruitmentPoolEntryDefinition[0];

        [Tooltip("후보 칸을 CSV에 적힌 순서대로 넣는다. 비어 있는 칸/짝을 특정할 수 없는 행/" +
                 "짝 키가 겹치는 행은 자동으로 제외되고 경고가 남는다.")]
        [SerializeField] private List<RecruitmentPoolEntryDefinition> entries =
            new List<RecruitmentPoolEntryDefinition>();

        private readonly List<RecruitmentPoolEntryDefinition> valid = new List<RecruitmentPoolEntryDefinition>();

        private bool built;

        /// <summary>쓸 수 있는 후보 칸 전부를 <b>작성 순서 그대로</b>. 모집 종류가 섞여 있다.</summary>
        public IReadOnlyList<RecruitmentPoolEntryDefinition> Entries
        {
            get
            {
                EnsureBuilt();
                return valid;
            }
        }

        /// <summary>쓸 수 있는 후보 칸 수(모든 모집을 합한 값).</summary>
        public int Count
        {
            get
            {
                EnsureBuilt();
                return valid.Count;
            }
        }

        /// <summary>
        /// 한 모집에 속한 후보 칸들을 <b>작성 순서 그대로</b> 돌려준다. 없으면 빈 목록이며 null이
        /// 아니다 - 후보가 하나도 없는 모집도 정상적인 상태로 다룬다(뽑기가
        /// <see cref="RecruitmentSelectionOutcome.NoEligibleCandidate"/>로 답한다).
        ///
        /// <b>호출할 때마다 새 목록을 만들어 돌려준다.</b> 재사용 버퍼를 돌려주면 다음 호출이 이미
        /// 남에게 건넨 목록을 비우게 되어, 결과를 들고 있던 쪽이 등 뒤에서 다른 답을 보게 된다
        /// (<see cref="Character.OwnedCharacterCollection.OwnedCharacters"/>와 같은 이유다).
        /// </summary>
        public IReadOnlyList<RecruitmentPoolEntryDefinition> EntriesFor(string recruitmentTypeId)
        {
            if (string.IsNullOrWhiteSpace(recruitmentTypeId)) return EmptyEntries;

            EnsureBuilt();

            var matches = new List<RecruitmentPoolEntryDefinition>();
            for (int i = 0; i < valid.Count; i++)
            {
                if (valid[i].BelongsTo(recruitmentTypeId)) matches.Add(valid[i]);
            }

            return matches;
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
            if (entries == null) return;

            var seen = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < entries.Count; i++)
            {
                RecruitmentPoolEntryDefinition entry = entries[i];

                if (entry == null)
                {
                    Debug.LogWarning($"[RecruitmentPoolCatalog] '{name}': {i}번 항목이 비어 있어 목록에서 제외합니다.", this);
                    continue;
                }

                if (!entry.IsIdentified)
                {
                    Debug.LogError($"[RecruitmentPoolCatalog] '{name}': {i}번 항목('{entry.name}')은 " +
                                   "recruitment_type_id와 pool_entry_id가 모두 있어야 합니다 - 목록에서 제외합니다.", entry);
                    continue;
                }

                if (!seen.Add(entry.PairId))
                {
                    Debug.LogError($"[RecruitmentPoolCatalog] '{name}': {i}번 항목('{entry.name}')의 짝 " +
                                   $"'{entry.PairId}'가 앞선 항목과 겹쳐 목록에서 제외합니다 - " +
                                   "먼저 작성된 칸이 남습니다.", entry);
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
