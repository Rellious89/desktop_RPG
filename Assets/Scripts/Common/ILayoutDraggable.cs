using UnityEngine;

namespace Common
{
    /// <summary>
    /// Layout Mode에서 직접 드래그로 옮길 수 있는 그룹(StageVisualRoot/GameHUDGroup/ControlDockGroup)의
    /// 공통 계약. LayoutModeController가 이 인터페이스만으로 세 그룹을 동일하게 다루고,
    /// TransparentWindowController가 클릭 관통/드래그 판정을 위해 화면 영역을 조회한다.
    ///
    /// 배치는 항상 "화면 우측/하단 기준 정규화 여백(0~1)"으로 주고받는다 - 절대 픽셀이 아니라서
    /// 해상도가 달라져도 화면 구석의 의도한 위치가 유지된다.
    /// </summary>
    public interface ILayoutDraggable
    {
        /// <summary>저장 데이터 필드를 구분하는 짧은 식별자. LayoutModeController.StageGroupId 등
        /// 상수와 맞춰 쓴다.</summary>
        string GroupId { get; }

        /// <summary>현재 Overlay가 표시된 모니터의 Work Area 픽셀 크기가 바뀔 때마다 호출된다.</summary>
        void NotifyWorkAreaChanged(int widthPixels, int heightPixels);

        /// <summary>드래그 중 매 프레임 호출된다. 네이티브 화면 픽셀 델타(Win32 좌표계, Y 아래로 증가)를 받는다.</summary>
        void ApplyDragDeltaPixels(int deltaXPixels, int deltaYPixels);

        void SetPlacement(float rightMarginFraction, float bottomMarginFraction);

        (float rightMarginFraction, float bottomMarginFraction) GetPlacement();

        /// <summary>기본 배치로 되돌린다 - 모니터 이동 등으로 저장된 배치를 신뢰할 수 없을 때 쓴다.</summary>
        void ResetToDefaultPlacement();

        /// <summary>
        /// 이 그룹이 지금 화면에서 차지하는 영역을 Unity 스크린 좌표계(좌하단 원점, Camera/RectTransformUtility의
        /// WorldToScreenPoint와 같은 규약)로 반환한다. 계산할 수 없으면(카메라/RectTransform 미할당 등) false.
        /// TransparentWindowController가 네이티브 좌표로 변환해서 클릭 관통/드래그 시작 판정에 쓴다.
        /// </summary>
        bool TryGetUnityScreenRect(out Rect unityScreenRect);

        /// <summary>
        /// Layout Mode 진입/종료 시 호출된다. UI 기반 그룹(GameHUDGroup/ControlDockGroup)은 이 안에서
        /// 드래그 캐처의 레이캐스트와 하이라이트 표시를 함께 켜고 끈다. StageVisualRoot처럼 별도 시각
        /// 표시가 없는 그룹은 no-op으로 둬도 된다.
        /// </summary>
        void SetLayoutModeActive(bool active);

        /// <summary>진단 로그 전용 - 이 그룹의 현재 상태를 한 줄로 요약해 돌려준다(예: 활성 여부,
        /// 레이캐스트 대상 여부). TransparentWindowController가 [LayoutHitTest] 로그에 그대로 붙여 쓴다.</summary>
        string GetDebugState();
    }
}
