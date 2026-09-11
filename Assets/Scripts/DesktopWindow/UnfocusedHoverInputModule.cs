using UnityEngine;
using UnityEngine.EventSystems;

namespace DesktopWindow
{
    /// <summary>
    /// Windows 오버레이가 다른 앱에 포커스를 넘긴 동안에도 uGUI hover만 갱신한다.
    ///
    /// StandaloneInputModule은 포커스를 잃으면 Process를 즉시 중단하므로 PointerEnter/Exit도 함께
    /// 멈춘다. Windows 빌드의 무포커스 상태에서만 전역 커서 위치를 읽어 기존 마우스 포인터 데이터를
    /// 갱신하고 ProcessMove만 호출한다. 클릭, 드래그, 스크롤, 선택/navigation 이벤트는 의도적으로
    /// 생성하지 않으며 창 포커스나 WS_EX_TRANSPARENT도 건드리지 않는다.
    /// </summary>
    [AddComponentMenu("Event/Unfocused Hover Input Module")]
    [DisallowMultipleComponent]
    public sealed class UnfocusedHoverInputModule : StandaloneInputModule
    {
        public override void Process()
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            if (!TransparentWindowController.HasWindowFocus)
            {
                ProcessWindowsUnfocusedHover();
                return;
            }
#endif

            base.Process();
        }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        private void ProcessWindowsUnfocusedHover()
        {
            TransparentWindowController windowController = TransparentWindowController.Instance;
            if (windowController == null || windowController.NativeWindowHandle == System.IntPtr.Zero)
            {
                ClearHover();
                return;
            }

            if (!Win32Interop.GetCursorPos(out Win32Interop.POINT cursor))
            {
                ClearHover();
                return;
            }

            var clientOrigin = new Win32Interop.POINT { X = 0, Y = 0 };
            if (!Win32Interop.ClientToScreen(windowController.NativeWindowHandle, ref clientOrigin))
            {
                ClearHover();
                return;
            }

            Vector2 unityPosition = ConvertNativeToUnityScreenPosition(
                cursor.X,
                cursor.Y,
                clientOrigin.X,
                clientOrigin.Y,
                Screen.height);

            ProcessHoverAt(unityPosition);
        }
#endif

        /// <summary>
        /// Win32 화면 좌표(좌상단 원점)를 Unity 클라이언트 좌표(좌하단 원점)로 변환한다.
        /// 별도 DPI 배율을 적용하지 않는다. 프로젝트가 시작 시 Per-Monitor DPI aware를 설정하므로
        /// GetCursorPos와 ClientToScreen이 같은 물리 픽셀 좌표계를 사용한다.
        /// </summary>
        internal static Vector2 ConvertNativeToUnityScreenPosition(
            int cursorX,
            int cursorY,
            int clientOriginX,
            int clientOriginY,
            int clientHeight)
        {
            return new Vector2(
                cursorX - clientOriginX,
                clientHeight - (cursorY - clientOriginY));
        }

        /// <summary>
        /// StandaloneInputModule이 사용하던 좌클릭 포인터 데이터를 재사용해 hover 대상만 갱신한다.
        /// 이 메서드에는 버튼/휠 상태 조회나 press/drag 처리가 없어 무포커스 클릭이 Unity UI로 새지 않는다.
        /// </summary>
        internal void ProcessHoverAt(Vector2 position)
        {
            bool created = GetPointerData(kMouseLeftId, out PointerEventData pointerData, true);
            pointerData.Reset();

            pointerData.delta = created ? Vector2.zero : position - pointerData.position;
            pointerData.position = position;
            pointerData.scrollDelta = Vector2.zero;
            pointerData.button = PointerEventData.InputButton.Left;

            eventSystem.RaycastAll(pointerData, m_RaycastResultCache);
            pointerData.pointerCurrentRaycast = FindFirstRaycast(m_RaycastResultCache);
            m_RaycastResultCache.Clear();

            ProcessMove(pointerData);
        }

        private void ClearHover()
        {
            if (!GetPointerData(kMouseLeftId, out PointerEventData pointerData, false)) return;

            pointerData.delta = Vector2.zero;
            pointerData.pointerCurrentRaycast = default;
            ProcessMove(pointerData);
        }
    }
}
