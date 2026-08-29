using System;
using System.Collections.Generic;

namespace Common
{
    [Serializable]
    public sealed class CharacterStoryObjectiveProgressSaveState
    {
        public string objectiveId;
        public int progress;
    }

    /// <summary>한 캐릭터의 활성 서사 단계와 확정 이력. ready는 완료가 아니라 UI가 Confirm을 호출할
    /// 수 있다는 표시이며, 다음 단계는 Confirm 뒤에만 열린다.</summary>
    [Serializable]
    public sealed class CharacterStoryQuestSaveState
    {
        public string characterId;
        public string activeQuestId;
        public List<CharacterStoryObjectiveProgressSaveState> objectiveProgress =
            new List<CharacterStoryObjectiveProgressSaveState>();
        public List<string> completedQuestIds = new List<string>();
        public bool readyToComplete;
        public bool graduated;
    }
}
