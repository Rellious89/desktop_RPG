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

        // Entries를 매번 순회해 새 리스트를 만들지 않도록 재사용하는 버퍼. 로스터의 사용 가능 목록은
        // 시작할 때 한 번 확정되므로 첫 접근에 한 번만 채운다.
        private readonly List<CharacterDefinition> characters = new List<CharacterDefinition>();

        public CharacterRosterRecoveryAdapter(CharacterRoster roster)
        {
            this.roster = roster;

            IReadOnlyList<CharacterRoster.Entry> entries = roster.Entries;
            for (int i = 0; i < entries.Count; i++)
            {
                characters.Add(entries[i].definition);
            }
        }

        public IReadOnlyList<CharacterDefinition> RecoverableCharacters => characters;

        public CharacterDefinition CurrentCharacter => roster.Current;

        public bool Contains(CharacterDefinition definition)
        {
            return definition != null && characters.Contains(definition);
        }

        public CharacterDefinition FindById(string characterId)
        {
            if (string.IsNullOrEmpty(characterId)) return null;

            for (int i = 0; i < characters.Count; i++)
            {
                if (characters[i] != null && characters[i].CharacterId == characterId) return characters[i];
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
    }
}
