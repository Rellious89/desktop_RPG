using System;
using System.Collections.Generic;

namespace DesktopWindow
{
    /// <summary>
    /// 로컬에 저장하는 Overlay 배치 데이터. Common.SaveData(플레이어 진행도)와는 완전히 별개의 파일에
    /// 저장된다 - 창 위치는 게임 진행 상태가 아니라 데스크탑 환경(모니터 배치 등) 값이라 성격이 다르다.
    ///
    /// 소형 창 모델(픽셀 좌표로 창을 옮기던 시절)에서 전체 모니터 Overlay 모델로 구조가 바뀌면서
    /// 저장 목적도 "창을 어디에 둘지"에서 "어느 모니터를 쓸지 + 그 안에서 각 UI 그룹을 어디에
    /// 둘지"로 나뉘었다. positionX/positionY/hasSavedPosition은 레거시 필드로 남겨둔다 - 새 필드가
    /// 없는(구버전에서 저장된) 파일을 읽었을 때 hasMonitorSelection이 false가 되므로, 그 경우에만
    /// 레거시 좌표로 "가장 가까운 모니터"를 찾는 마이그레이션 힌트로 한 번 활용한다.
    ///
    /// groupPlacements(신규)는 그룹 개수가 고정이던 시절(Stage/HUD/Dock 세 개, 필드를 하나씩
    /// 직접 나열)에서 그룹이 임의로 늘어날 수 있는 구조로 바뀌면서 도입한 목록형 저장 구조다 - 이제
    /// 새 UI 그룹을 추가해도 이 클래스에 필드를 더 늘릴 필요가 없다(TransparentWindowController가
    /// LayoutModeController.AllGroups를 순회해서 이 목록을 채우고 읽는다). hasStagePlacement 이하
    /// 예전 필드들은 groupPlacements가 비어있는(구버전에서 저장된) 파일을 한 번만 마이그레이션하는
    /// 용도로만 남겨두고, 더 이상 새로 쓰지 않는다.
    /// </summary>
    [Serializable]
    public class WindowPlacementData
    {
        /// <summary>레거시: 소형 창 시절의 절대 화면 픽셀 좌표. 새 구조에서는 창 위치 자체로는 쓰지
        /// 않고, hasMonitorSelection이 false일 때만 마이그레이션용 "가장 가까운 모니터 찾기" 힌트로 쓴다.</summary>
        public bool hasSavedPosition = false;
        public int positionX = 0;
        public int positionY = 0;

        /// <summary>Overlay를 표시할 모니터. HMONITOR 핸들은 세션마다(드라이버 재열거 등으로) 바뀔 수
        /// 있어 저장에 쓰지 않고, MONITORINFOEX.szDevice(예: "\\.\DISPLAY1")로 저장한다.</summary>
        public bool hasMonitorSelection = false;
        public string monitorDeviceName = "";

        /// <summary>레거시(마이그레이션 전용, 더 이상 새로 쓰지 않음): 예전 StageVisualRoot 단독 배치.</summary>
        public bool hasStagePlacement = false;
        public float stageRightMarginFraction = 0.05f;
        public float stageBottomMarginFraction = 0.05f;

        /// <summary>레거시(마이그레이션 전용, 더 이상 새로 쓰지 않음): 예전 HUD 전체가 하나의 그룹이던
        /// 시절의 배치. 새 구조로 넘어올 때 combo/progress/killCount 세 그룹의 초기값으로 그대로
        /// 복사해서 쓴다(완벽히 맞진 않아도 "HUD가 있던 근처"에서 시작하게 하려는 목적).</summary>
        public bool hasHudPlacement = false;
        public float hudRightMarginFraction = 0f;
        public float hudBottomMarginFraction = 0f;

        /// <summary>레거시(마이그레이션 전용, 더 이상 새로 쓰지 않음): 예전 ControlDock 단독 배치.</summary>
        public bool hasDockPlacement = false;
        public float dockRightMarginFraction = 0f;
        public float dockBottomMarginFraction = 0f;

        /// <summary>신규 - groupId 기반 일반화된 배치 목록. 그룹이 몇 개든, 어떤 이름이든 이 하나의
        /// 목록으로 저장/복원된다.</summary>
        public List<LayoutGroupPlacement> groupPlacements = new List<LayoutGroupPlacement>();
    }

    /// <summary>Layout Mode의 한 그룹(ILayoutDraggable.GroupId)에 대한 정규화 배치 저장 단위.
    /// normalizedPositionX/Y의 의미는 UiGroupDraggable/StageVisualRootController가 각자 정의한다
    /// (둘 다 "anchoredPosition 계열 오프셋 / 현재 Work Area 픽셀 크기" 형태의 정규화 값을 쓴다).</summary>
    [Serializable]
    public class LayoutGroupPlacement
    {
        public string groupId = "";
        public float normalizedPositionX;
        public float normalizedPositionY;
    }
}
