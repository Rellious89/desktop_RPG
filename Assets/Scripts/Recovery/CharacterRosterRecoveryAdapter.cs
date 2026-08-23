using System;
using System.Collections.Generic;
using Character;

namespace Recovery
{
    /// <summary>
    /// 회복소가 요구하는 <see cref="IRecoveryRoster"/>를 실제 <see cref="CharacterRoster"/>에 연결하는
    /// 어댑터. 로직은 하나도 갖지 않는다 - 회복소 규칙을 로스터에 심지 않고, 로스터의 행동력 소유권도
    /// 회복소로 옮기지 않기 위한 얇은 층이다.
    /// </summary>
    public class CharacterRosterRecoveryAdapter : IRecoveryRoster
    {
        private readonly CharacterRoster roster;

        // 새 영입도 저장 직후 즉시 회복소의 선택 목록에 반영되어야 하므로, 이 버퍼는 호출마다 전체
        // 보유 목록에서 다시 채운다. 출전 파티는 전투/교체의 경계이고, 비파티 보유 캐릭터도 회복소에는
        // 등록할 수 있어야 한다. RecoveryStation은 어댑터를 계속 보관해도 오래된 목록을 보지 않는다.
        private readonly List<CharacterDefinition> characters = new List<CharacterDefinition>();

        public CharacterRosterRecoveryAdapter(CharacterRoster roster)
        {
            this.roster = roster;

        }

        public IReadOnlyList<CharacterDefinition> RecoverableCharacters
        {
            get
            {
                RefreshCharacters();
                return characters;
            }
        }

        public CharacterDefinition CurrentCharacter => roster.Current;

        /// <summary>
        /// 회복소가 다룰 수 있는 캐릭터인지. 판정은 <b>CharacterId의 Ordinal 완전 일치</b>이며 에셋
        /// 참조가 아니다 - 씬이 들고 있는 수동 에셋과 카탈로그의 생성 에셋은 서로 다른 객체지만 같은
        /// 캐릭터이므로, 참조로 비교하면 같은 캐릭터를 "회복소가 모르는 캐릭터"로 판정하게 된다.
        ///
        /// 목록 자체가 로스터의 보유 목록이므로, <b>보유하지 않은 캐릭터는 여기서 false</b>다.
        /// </summary>
        public bool Contains(CharacterDefinition definition)
        {
            return FindById(definition != null ? definition.CharacterId : null) != null;
        }

        /// <summary>id로 찾는다. 없으면 null이며(모르는 슬롯 id나 보유하지 않은 캐릭터), 호출부는 그
        /// null을 그대로 다뤄야 한다 - 여기서 상태를 만들어 채워 주지 않는다.</summary>
        public CharacterDefinition FindById(string characterId)
        {
            if (string.IsNullOrEmpty(characterId)) return null;

            RefreshCharacters();

            for (int i = 0; i < characters.Count; i++)
            {
                CharacterDefinition candidate = characters[i];
                if (candidate != null && string.Equals(candidate.CharacterId, characterId, StringComparison.Ordinal))
                {
                    return candidate;
                }
            }

            return null;
        }

        public string GetCharacterId(CharacterDefinition definition)
        {
            return definition != null ? definition.CharacterId : null;
        }

        public int GetStamina(CharacterDefinition definition)
        {
            return roster.GetStamina(definition);
        }

        public int GetMaxStamina(CharacterDefinition definition)
        {
            return roster.GetMaxStamina(definition);
        }

        public bool ApplyRecoveryStamina(CharacterDefinition definition, int value)
        {
            return roster.ApplyRecoveryStamina(definition, value);
        }

        public void RaiseCharacterStateChanged(CharacterDefinition definition)
        {
            roster.RaiseCharacterStateChanged(definition);
        }

        private void RefreshCharacters()
        {
            characters.Clear();
            IReadOnlyList<CharacterDefinition> owned = roster.OwnedCharacters;
            for (int i = 0; i < owned.Count; i++)
            {
                CharacterDefinition definition = owned[i];
                if (definition != null) characters.Add(definition);
            }
        }
    }
}
