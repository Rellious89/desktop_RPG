using DesktopWindow;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Common
{
    /// <summary>
    /// ControlDock의 MoveHandle 버튼에 붙는 드래그 시작 트리거. 이동 진행/종료(위치 저장 포함)는
    /// TransparentWindowController가 매 프레임 전역 마우스 상태(GetCursorPos/GetAsyncKeyState)를
    /// 폴링해서 처리하므로, 여기서는 "MoveHandle 위에서 눌렸다"는 신호만 한 번 전달하면 된다 -
    /// OnDrag/OnPointerUp을 따로 구현할 필요가 없다.
    /// </summary>
    public class MoveHandleDrag : MonoBehaviour, IPointerDownHandler
    {
        public void OnPointerDown(PointerEventData eventData)
        {
            if (TransparentWindowController.Instance == null) return;
            TransparentWindowController.Instance.BeginManualDrag();
        }
    }
}
