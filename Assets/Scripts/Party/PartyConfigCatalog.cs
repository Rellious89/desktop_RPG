using System;
using System.Collections.Generic;
using UnityEngine;

namespace Party
{
    /// <summary>
    /// 파티 설정 목록의 <b>순서와 구성</b>을 소유하는 에셋. 담기는 것은 활성 행뿐이며 순서는 CSV에
    /// 적힌 그대로다 - 이 표에는 display_order 칸이 없다(<see cref="Recruitment.RecruitmentTypeCatalog"/>와
    /// 같은 이유다).
    ///
    /// <b>걸러내기는 여기서 한 번만 한다.</b> 비어 있는 칸, 식별자가 없거나 정원이 1 미만인 행,
    /// 앞선 항목과 id가 겹치는 행은 제외하고 남은 항목을 작성 순서 그대로 돌려준다. 비교는
    /// <see cref="StringComparer.Ordinal"/>이다.
    ///
    /// <b>조회는 아무것도 바꾸지 않는다.</b> 목록을 몇 번을 물어도 에셋의 직렬화 값은 한 글자도
    /// 달라지지 않으며(검사 결과만 메모리에 캐시한다), 저장 문서도 파일도 건드리지 않는다.
    /// </summary>
    [CreateAssetMenu(fileName = "PartyConfigCatalog", menuName = "Party/Party Config Catalog")]
    public class PartyConfigCatalog : ScriptableObject
    {
        [Tooltip("파티 설정을 CSV에 적힌 순서대로 넣는다. 비어 있는 칸/식별자가 없거나 정원이 1 미만인 " +
                 "행/id가 겹치는 행은 자동으로 제외되고 경고가 남는다.")]
        [SerializeField] private List<PartyConfigDefinition> configs = new List<PartyConfigDefinition>();

        private readonly List<PartyConfigDefinition> valid = new List<PartyConfigDefinition>();

        private bool built;

        /// <summary>쓸 수 있는 설정들을 <b>작성 순서 그대로</b>. 비어 있으면 빈 목록이며 null이 아니다.</summary>
        public IReadOnlyList<PartyConfigDefinition> Configs
        {
            get
            {
                EnsureBuilt();
                return valid;
            }
        }

        /// <summary>쓸 수 있는 설정 수.</summary>
        public int Count
        {
            get
            {
                EnsureBuilt();
                return valid.Count;
            }
        }

        /// <summary>식별자로 설정을 찾는다. 없으면 null이며 <b>넘어온 문자열을 손대지 않고</b> 비교한다 -
        /// 다듬거나 대소문자를 맞추면 표에 적힌 id와 코드가 쓰는 키가 어긋난 것을 못 보고 지나친다.</summary>
        public PartyConfigDefinition Find(string partyConfigId)
        {
            if (string.IsNullOrWhiteSpace(partyConfigId)) return null;

            EnsureBuilt();

            for (int i = 0; i < valid.Count; i++)
            {
                if (string.Equals(valid[i].ConfigId, partyConfigId, StringComparison.Ordinal)) return valid[i];
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
            if (configs == null) return;

            var seen = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < configs.Count; i++)
            {
                PartyConfigDefinition entry = configs[i];

                if (entry == null)
                {
                    Debug.LogWarning($"[PartyConfigCatalog] '{name}': {i}번 항목이 비어 있어 목록에서 제외합니다.", this);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(entry.ConfigId))
                {
                    Debug.LogError($"[PartyConfigCatalog] '{name}': {i}번 항목('{entry.name}')에 " +
                                   "Config Id가 없어 목록에서 제외합니다.", entry);
                    continue;
                }

                if (!entry.IsValid)
                {
                    Debug.LogError($"[PartyConfigCatalog] '{name}': {i}번 항목('{entry.name}')의 " +
                                   $"Base Capacity가 {entry.BaseCapacity}라 목록에서 제외합니다 - " +
                                   $"{PartyConfigRules.MinimumBaseCapacity} 이상이어야 합니다.", entry);
                    continue;
                }

                if (!seen.Add(entry.ConfigId))
                {
                    Debug.LogError($"[PartyConfigCatalog] '{name}': {i}번 항목('{entry.name}')의 " +
                                   $"Config Id '{entry.ConfigId}'가 앞선 항목과 겹쳐 목록에서 제외합니다 - " +
                                   "먼저 작성된 행이 남습니다.", entry);
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
