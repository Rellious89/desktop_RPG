using System;
using System.Collections.Generic;
using UnityEngine;

namespace Recruitment
{
    /// <summary>
    /// "어떤 캐릭터를 어떤 길로 얻을 수 있는가" 목록의 <b>순서와 구성</b>을 소유하는 에셋.
    /// <see cref="Building.BuildingCatalog"/>와 같은 역할이며, 읽는 쪽은 프로젝트를 뒤지지 않고
    /// (AssetDatabase 탐색도 하지 않는다) 이 에셋 하나만 읽는다.
    ///
    /// <b>담기는 것은 활성 행뿐이며 순서는 CSV에 적힌 그대로다.</b> 이 표에는 display_order 칸이
    /// 없다 - 획득 방식은 화면에 줄지어 보이는 것이 아니라 캐릭터 하나를 조회하는 데 쓰이므로,
    /// 없는 정렬 칸을 지어내는 대신 <b>사람이 적은 순서</b>를 그대로 목록의 순서로 삼는다.
    ///
    /// <b>걸러내기는 여기서 한 번만 한다.</b> 비어 있는 칸, 캐릭터를 가리키지 못하는 행, 앞선 항목과
    /// character_id가 겹치는 행은 제외하고 <see cref="Acquisitions"/>는 남은 항목을 작성 순서 그대로
    /// 돌려준다. 겹칠 때 앞의 것을 남기는 것도 다른 카탈로그와 같은 이유다 - 나중에 실수로 복제한
    /// 항목이 먼저 작성한 행을 밀어내지 않게 한다. 비교는 언제나
    /// <see cref="StringComparer.Ordinal"/>이라 <b>적힌 그대로</b> 본다.
    /// </summary>
    [CreateAssetMenu(fileName = "CharacterAcquisitionCatalog", menuName = "Recruitment/Character Acquisition Catalog")]
    public class CharacterAcquisitionCatalog : ScriptableObject
    {
        [Tooltip("획득 방식 행을 CSV에 적힌 순서대로 넣는다. 비어 있는 칸/캐릭터가 없는 행/" +
                 "character_id가 겹치는 행은 자동으로 제외되고 경고가 남는다.")]
        [SerializeField] private List<CharacterAcquisitionDefinition> acquisitions =
            new List<CharacterAcquisitionDefinition>();

        private readonly List<CharacterAcquisitionDefinition> valid = new List<CharacterAcquisitionDefinition>();

        private bool built;

        /// <summary>쓸 수 있는 행들을 <b>작성 순서 그대로</b>. 항목이 없으면 빈 목록이며 null이 아니다.</summary>
        public IReadOnlyList<CharacterAcquisitionDefinition> Acquisitions
        {
            get
            {
                EnsureBuilt();
                return valid;
            }
        }

        /// <summary>쓸 수 있는 행의 수.</summary>
        public int Count
        {
            get
            {
                EnsureBuilt();
                return valid.Count;
            }
        }

        /// <summary>Character Id로 획득 방식을 찾는다. 없으면 null이다 - 목록이 작아 선형 탐색으로
        /// 충분하고, 별도 사전을 두어 캐시 무효화 경로를 하나 더 만들지 않는다. <b>넘어온 문자열을
        /// 손대지 않고 그대로 비교한다</b>.</summary>
        public CharacterAcquisitionDefinition FindByCharacterId(string characterId)
        {
            if (string.IsNullOrWhiteSpace(characterId)) return null;

            EnsureBuilt();

            for (int i = 0; i < valid.Count; i++)
            {
                if (string.Equals(valid[i].CharacterId, characterId, StringComparison.Ordinal)) return valid[i];
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
            if (acquisitions == null) return;

            var seen = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < acquisitions.Count; i++)
            {
                CharacterAcquisitionDefinition entry = acquisitions[i];

                if (entry == null)
                {
                    Debug.LogWarning($"[CharacterAcquisitionCatalog] '{name}': {i}번 항목이 비어 있어 목록에서 제외합니다.", this);
                    continue;
                }

                if (!entry.IsValid)
                {
                    Debug.LogError($"[CharacterAcquisitionCatalog] '{name}': {i}번 항목('{entry.name}')에 " +
                                   "Character Id가 없어 목록에서 제외합니다.", entry);
                    continue;
                }

                if (!seen.Add(entry.CharacterId))
                {
                    Debug.LogError($"[CharacterAcquisitionCatalog] '{name}': {i}번 항목('{entry.name}')의 " +
                                   $"Character Id '{entry.CharacterId}'가 앞선 항목과 겹쳐 목록에서 제외합니다 - " +
                                   "먼저 작성된 행이 남습니다(대소문자는 구분합니다).", entry);
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
