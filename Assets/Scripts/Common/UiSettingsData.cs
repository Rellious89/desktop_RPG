using System;

namespace Common
{
    /// <summary>
    /// 로컬에 저장하는 ControlDock 토글 상태(SFX On/Off, HUD 표시 여부, 창 크기 배율). PlayerProgress.SaveData
    /// (진행도), WindowPlacementData(창 위치)와는 완전히 별개의 파일에 저장된다 - UI 표시 설정이라 성격이 다르다.
    /// </summary>
    [Serializable]
    public class UiSettingsData
    {
        public bool sfxEnabled = false;
        public bool hudVisible = true;

        /// <summary>tgl_size 배율(1 = 100%). 그때그때 정해지는 순환 목록(SizeToggleButton.sizePercentages)의
        /// 인덱스가 아니라 실제 배율값 자체를 저장한다 - 목록 구성이 나중에 바뀌어도 이 값은 그대로 유효하다.</summary>
        public float sizeScale = 1f;
    }
}
