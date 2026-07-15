using System;

namespace DesktopWindow
{
    /// <summary>
    /// 로컬에 저장하는 창 배치 데이터. Common.SaveData(플레이어 진행도)와는 완전히 별개의 파일에
    /// 저장된다 - 창 위치는 게임 진행 상태가 아니라 데스크탑 환경(모니터 배치 등) 값이라 성격이 다르다.
    /// </summary>
    [Serializable]
    public class WindowPlacementData
    {
        public bool hasSavedPosition = false;
        public int positionX = 0;
        public int positionY = 0;
    }
}
