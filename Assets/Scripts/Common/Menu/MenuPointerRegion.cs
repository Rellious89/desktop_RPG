using UnityEngine;
using UnityEngine.EventSystems;

namespace Common
{
    /// <summary>
    /// 이 영역 안에 마우스가 있는지만 들고 있는 최소 컴포넌트. <see cref="MenuBarExpander"/>가
    /// 자동 접힘 타이머를 멈출지 판단하는 데 쓴다.
    ///
    /// <b>자식 버튼 위에 있어도 "안에 있음"이다.</b> EventSystem은 포인터가 들어간 오브젝트에서
    /// 부모 쪽으로 올라가며 Enter/Exit를 모두 보내므로(StandaloneInputModule의
    /// <c>Send Pointer Hover To Parent</c>), btnArea에 하나만 붙이면 안쪽 버튼을 오갈 때는
    /// Exit가 발생하지 않고 영역 밖으로 나갈 때만 Exit가 온다. 버튼마다 붙일 필요가 없고,
    /// 나중에 하위 메뉴가 이 아래에 생겨도 그대로 포함된다.
    ///
    /// <b>이벤트가 아니라 상태로 둔 이유.</b> Enter는 들어올 때 한 번만 오므로, 이벤트만 세면
    /// 버튼 위에 마우스를 가만히 올려둔 사용자의 메뉴가 접혀 버린다.
    /// </summary>
    [DisallowMultipleComponent]
    public class MenuPointerRegion : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        /// <summary>지금 이 영역(또는 그 자식) 위에 마우스가 있는지.</summary>
        public bool PointerInside { get; private set; }

        public void OnPointerEnter(PointerEventData eventData) => PointerInside = true;

        public void OnPointerExit(PointerEventData eventData) => PointerInside = false;

        private void OnDisable()
        {
            // 메뉴가 접히는 순간 Exit가 오지 않을 수 있다 - 꺼질 때 상태를 직접 되돌려야
            // 다음에 펼쳤을 때 "안에 있음"으로 잘못 시작하지 않는다.
            PointerInside = false;
        }
    }
}
