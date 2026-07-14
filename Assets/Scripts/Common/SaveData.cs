using System;

namespace Common
{
    /// <summary>
    /// 로컬에 저장하는 플레이어 진행도 전체. 필드를 늘려야 하면 여기에만 추가하면 된다.
    /// 세션 킬카운트, 콤보, 내구도, 공격/애니메이션 상태처럼 그때그때 휘발되는 값은 포함하지 않는다.
    /// 필드 기본값은 저장 파일이 없거나 일부 필드가 누락됐을 때 쓰는 새 게임 기본값과 같다.
    /// </summary>
    [Serializable]
    public class SaveData
    {
        public int currentLevel = 1;
        public int currentExp = 0;
        public int totalKillCount = 0;
    }
}
