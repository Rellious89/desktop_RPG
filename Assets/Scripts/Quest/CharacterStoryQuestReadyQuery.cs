using System;
using System.Collections.Generic;
using Character;
using Common;

namespace Quest
{
    /// <summary>HUD와 같은 읽기 전용 표시가 공유하는 완료 가능 서사 퀘스트 판정이다. 저장 상태를
    /// 바꾸지 않으며, 선택 결과는 catalog 표시 순서가 아니라 CharacterId Ordinal 순서로 안정적이다.</summary>
    public static class CharacterStoryQuestReadyQuery
    {
        public static List<string> GetReadyCharacterIds(
            SaveData data, CharacterCatalog characterCatalog, CharacterStoryQuestCatalog questCatalog)
        {
            var result = new List<string>();
            if (data?.characterStoryQuests == null || data.characters == null ||
                characterCatalog == null || questCatalog == null) return result;

            var owned = new OwnedCharacterCollection(characterCatalog, data);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < data.characterStoryQuests.Count; i++)
            {
                CharacterStoryQuestSaveState state = data.characterStoryQuests[i];
                if (state == null || !state.readyToComplete || state.graduated ||
                    string.IsNullOrEmpty(state.characterId) || string.IsNullOrEmpty(state.activeQuestId) ||
                    !seen.Add(state.characterId)) continue;

                CharacterDefinition character = characterCatalog.Find(state.characterId);
                CharacterStoryQuestDefinition quest = questCatalog.Find(state.activeQuestId);
                if (character == null || !owned.IsOwned(character) || quest == null ||
                    !string.Equals(quest.CharacterId, state.characterId, StringComparison.Ordinal)) continue;

                result.Add(state.characterId);
            }

            result.Sort(StringComparer.Ordinal);
            return result;
        }
    }
}
