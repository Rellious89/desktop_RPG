using System;
using System.Collections.Generic;
using UnityEngine;

namespace Character
{
    /// <summary>
    /// 캐릭터 목록의 <b>순서와 구성</b>을 소유하는 에셋. <see cref="Inventory.ItemCatalog"/> /
    /// <see cref="Inventory.CurrencyCatalog"/>와 같은 역할이며, 읽는 쪽은 프로젝트를 뒤져 캐릭터를
    /// 모으지 않고(AssetDatabase 탐색도 하지 않는다) 이 에셋 하나만 읽는다.
    ///
    /// <b>이 목록은 "지금 보유한 캐릭터"가 아니다.</b> 보유/투입/행동력은 계속
    /// <see cref="CharacterRoster"/>와 저장 데이터가 소유하고, 여기는 "이 게임에 어떤 캐릭터가
    /// 있는가"를 표에서 만들어 모아 두는 자리다 - <b>이 카탈로그는 로스터에 연결되어 있지 않다</b>.
    ///
    /// <b>담기는 것은 생성 폴더의 활성 캐릭터뿐이다.</b> 목록을 채우는 임포터가
    /// Assets/Generated/TableData/Character 아래에서 만든 정의만, enabled=1인 행만,
    /// display_order 오름차순 → 같으면 character_id Ordinal 오름차순으로 넣는다. 사람이 만든
    /// Assets/Data 이하의 수동 CharacterDefinition은 <b>여기에 들어오지 않는다</b> - 같은 id를 가진
    /// 수동 에셋이 아직 남아 있어도 이 목록의 구성은 표 하나로만 정해진다.
    ///
    /// <b>걸러내기는 여기서 한 번만 한다.</b> 비어 있는 칸, 식별자가 없는 캐릭터, 앞선 항목과 id가
    /// 겹치는 캐릭터는 목록에서 제외하고 <see cref="Characters"/>는 <b>남은 항목을 작성 순서 그대로</b>
    /// 돌려준다 - 정렬은 목록을 채우는 임포터의 몫이지 읽는 쪽의 몫이 아니다. id 비교는
    /// <see cref="StringComparer.Ordinal"/>이라 <b>적힌 그대로</b> 본다.
    /// </summary>
    [CreateAssetMenu(fileName = "CharacterCatalog", menuName = "Character/Character Catalog")]
    public class CharacterCatalog : ScriptableObject
    {
        [Tooltip("캐릭터를 나올 순서대로 넣는다. 비어 있는 칸/식별자가 없는 캐릭터/id가 겹치는 " +
                 "캐릭터는 자동으로 제외되고 경고가 남는다.")]
        [SerializeField] private List<CharacterDefinition> characters = new List<CharacterDefinition>();

        /// <summary>검사를 통과한 항목만 작성 순서대로 담아 둔 캐시. 조회할 때마다 새로 만들지 않는다.</summary>
        private readonly List<CharacterDefinition> validCharacters = new List<CharacterDefinition>();

        private bool built;

        /// <summary>쓸 수 있는 캐릭터들을 <b>작성 순서 그대로</b> 돌려준다. 항목이 하나도 없으면 빈
        /// 목록이며 null이 아니다 - 비어 있는 카탈로그도 정상적인 상태로 다룬다.</summary>
        public IReadOnlyList<CharacterDefinition> Characters
        {
            get
            {
                EnsureBuilt();
                return validCharacters;
            }
        }

        /// <summary>쓸 수 있는 캐릭터 수.</summary>
        public int Count
        {
            get
            {
                EnsureBuilt();
                return validCharacters.Count;
            }
        }

        /// <summary>식별자로 캐릭터를 찾는다. 없으면 null이다. <b>넘어온 문자열을 손대지 않고 그대로
        /// 비교한다</b> - 대소문자를 구분하고 앞뒤 공백도 떼지 않는다(다른 카탈로그와 같은 규칙).</summary>
        public CharacterDefinition Find(string characterId)
        {
            if (string.IsNullOrWhiteSpace(characterId)) return null;

            EnsureBuilt();

            for (int i = 0; i < validCharacters.Count; i++)
            {
                if (string.Equals(validCharacters[i].CharacterId, characterId, StringComparison.Ordinal))
                {
                    return validCharacters[i];
                }
            }

            return null;
        }

        /// <summary>다음 조회 때 검사를 다시 하도록 표시한다. 에디터에서 목록을 고친 뒤나 임포터가
        /// 목록을 채운 뒤에 쓴다.</summary>
        public void MarkDirty()
        {
            built = false;
        }

        private void OnEnable()
        {
            // 에셋이 로드될 때마다 한 번은 다시 검사한다.
            built = false;
        }

        private void EnsureBuilt()
        {
            if (built) return;
            built = true;

            validCharacters.Clear();
            if (characters == null) return;

            var seenIds = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < characters.Count; i++)
            {
                CharacterDefinition character = characters[i];

                if (character == null)
                {
                    Debug.LogWarning($"[CharacterCatalog] '{name}': {i}번 항목이 비어 있어 목록에서 제외합니다.", this);
                    continue;
                }

                // CharacterId는 비어 있으면 에셋 파일 이름을 돌려주는 기존 규칙이라 여기서 빈 값이
                // 나오는 일은 사실상 없지만, 그 규칙이 바뀌어도 목록이 조용히 깨지지 않게 확인한다.
                if (string.IsNullOrWhiteSpace(character.CharacterId))
                {
                    Debug.LogError($"[CharacterCatalog] '{name}': {i}번 항목('{character.name}')에 Character Id가 " +
                                   "없어 목록에서 제외합니다.", character);
                    continue;
                }

                if (!seenIds.Add(character.CharacterId))
                {
                    Debug.LogError($"[CharacterCatalog] '{name}': {i}번 항목('{character.name}')의 Character Id " +
                                   $"'{character.CharacterId}'가 앞선 항목과 겹쳐 목록에서 제외합니다 - " +
                                   "먼저 작성된 캐릭터가 남습니다(대소문자는 구분합니다).", character);
                    continue;
                }

                validCharacters.Add(character);
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            // 에디터에서 목록을 고치면 다음 조회 때 검사와 경고가 최신 내용 기준으로 한 번 다시 돈다.
            built = false;
        }
#endif
    }
}
