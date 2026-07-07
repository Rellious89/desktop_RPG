using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace DesktopWindow
{
    /// <summary>
    /// 빌드된 Windows 스탠드얼론 실행 파일의 창을 테두리 없는 투명 창으로 바꾸고,
    /// 주 모니터 전체를 덮도록 배치한 뒤, 캐릭터/UI가 없는 영역은 클릭이 관통되도록 만든다.
    /// 창 자체가 모니터 전체 크기이므로 화면 어디든(하단 건물, 상단 UI 등) 게임 콘텐츠를 자유롭게 배치할 수 있다.
    /// Win32 API 기반이라 Windows 빌드에서만 동작하며, 에디터/다른 플랫폼에서는 아무 동작도 하지 않는다.
    /// (macOS 등 다른 플랫폼에서 동일 기능이 필요하면 별도의 네이티브 플러그인 구현이 필요함)
    /// </summary>
    [DisallowMultipleComponent]
    public class TransparentWindowController : MonoBehaviour
    {
        [Header("Window Placement")]
        [SerializeField] private bool alwaysOnTop = true;

        [Header("Click-Through Hit Test")]
        [SerializeField] private Camera targetCamera;
        [SerializeField] private LayerMask hitTestLayers = ~0;

#if UNITY_STANDALONE_WIN
        private IntPtr hwnd;
        private bool isClickThroughActive;
        private int screenWidth;
        private int screenHeight;
        private readonly List<RaycastResult> uiRaycastResults = new List<RaycastResult>();
#endif

        private void Awake()
        {
            if (targetCamera == null)
            {
                targetCamera = Camera.main;
            }
        }

        private void Start()
        {
#if UNITY_EDITOR
            Debug.LogWarning("[TransparentWindowController] 투명/보더리스 창 효과는 빌드된 Windows 실행 파일(.exe)에서만 동작합니다. Editor Play 모드에서는 적용되지 않습니다.");
#elif UNITY_STANDALONE_WIN
            SetupCameraBackground();

            hwnd = Win32Interop.GetActiveWindow();
            if (hwnd == IntPtr.Zero)
            {
                Debug.LogError("[TransparentWindowController] 윈도우 핸들을 가져오지 못했습니다.");
                return;
            }

            RemoveWindowBorder();
            EnableWindowTransparency();
            CoverEntireScreen();
            SetClickThrough(true);
#else
            Debug.LogWarning("[TransparentWindowController] 이 기능은 Win32 API 기반이라 Windows 빌드에서만 지원됩니다. 현재 플랫폼에서는 기본 창으로 동작합니다.");
#endif
        }

#if UNITY_STANDALONE_WIN
        private void Update()
        {
            UpdateClickThroughState();
        }

        private void SetupCameraBackground()
        {
            if (targetCamera == null) return;
            targetCamera.clearFlags = CameraClearFlags.SolidColor;
            targetCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
        }

        private void RemoveWindowBorder()
        {
            int style = Win32Interop.GetWindowLong(hwnd, Win32Interop.GWL_STYLE);
            style &= ~(int)(Win32Interop.WS_CAPTION | Win32Interop.WS_THICKFRAME |
                             Win32Interop.WS_MINIMIZEBOX | Win32Interop.WS_MAXIMIZEBOX | Win32Interop.WS_SYSMENU);
            Win32Interop.SetWindowLong(hwnd, Win32Interop.GWL_STYLE, (uint)style);
        }

        private void EnableWindowTransparency()
        {
            int exStyle = Win32Interop.GetWindowLong(hwnd, Win32Interop.GWL_EXSTYLE);
            exStyle |= (int)Win32Interop.WS_EX_LAYERED;
            Win32Interop.SetWindowLong(hwnd, Win32Interop.GWL_EXSTYLE, (uint)exStyle);

            // DWM에 클라이언트 영역 전체를 "유리(glass)"로 확장 요청(음수 마진) -> 알파 채널이 그대로 합성되어 배경이 비친다.
            var margins = new Win32Interop.MARGINS { cxLeftWidth = -1, cxRightWidth = -1, cyTopHeight = -1, cyBottomHeight = -1 };
            Win32Interop.DwmExtendFrameIntoClientArea(hwnd, ref margins);

            Win32Interop.SetWindowPos(hwnd, alwaysOnTop ? Win32Interop.HWND_TOPMOST : Win32Interop.HWND_NOTOPMOST,
                0, 0, 0, 0, Win32Interop.SWP_NOMOVE | Win32Interop.SWP_NOSIZE | Win32Interop.SWP_FRAMECHANGED);
        }

        private void CoverEntireScreen()
        {
            screenWidth = Win32Interop.GetSystemMetrics(Win32Interop.SM_CXSCREEN);
            screenHeight = Win32Interop.GetSystemMetrics(Win32Interop.SM_CYSCREEN);

            Win32Interop.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, screenWidth, screenHeight, Win32Interop.SWP_NOZORDER);
        }

        private void SetClickThrough(bool enabled)
        {
            if (isClickThroughActive == enabled) return;
            isClickThroughActive = enabled;

            int exStyle = Win32Interop.GetWindowLong(hwnd, Win32Interop.GWL_EXSTYLE);
            exStyle = enabled
                ? exStyle | (int)Win32Interop.WS_EX_TRANSPARENT
                : exStyle & ~(int)Win32Interop.WS_EX_TRANSPARENT;

            Win32Interop.SetWindowLong(hwnd, Win32Interop.GWL_EXSTYLE, (uint)exStyle);
        }

        /// <summary>
        /// WS_EX_TRANSPARENT가 걸려 있으면 창이 마우스 메시지를 아예 받지 못해
        /// Input.mousePosition이 갱신되지 않는다. 그래서 OS 커서 좌표를 직접 읽어 판정한다.
        /// </summary>
        private void UpdateClickThroughState()
        {
            if (!Win32Interop.GetCursorPos(out Win32Interop.POINT screenPoint)) return;

            var clientPoint = screenPoint;
            Win32Interop.ScreenToClient(hwnd, ref clientPoint);

            bool insideWindow = clientPoint.X >= 0 && clientPoint.X < screenWidth &&
                                 clientPoint.Y >= 0 && clientPoint.Y < screenHeight;

            if (!insideWindow)
            {
                SetClickThrough(true);
                return;
            }

            // Win32 클라이언트 좌표는 좌상단 원점, Unity 화면 좌표는 좌하단 원점이므로 Y를 뒤집는다.
            Vector2 unityScreenPoint = new Vector2(clientPoint.X, screenHeight - clientPoint.Y);

            bool hitSomething = IsPointerOverUI(unityScreenPoint) || IsPointerOverObject(unityScreenPoint);
            SetClickThrough(!hitSomething);
        }

        private bool IsPointerOverUI(Vector2 screenPoint)
        {
            if (EventSystem.current == null) return false;

            var eventData = new PointerEventData(EventSystem.current) { position = screenPoint };
            uiRaycastResults.Clear();
            EventSystem.current.RaycastAll(eventData, uiRaycastResults);
            return uiRaycastResults.Count > 0;
        }

        private bool IsPointerOverObject(Vector2 screenPoint)
        {
            if (targetCamera == null) return false;

            Ray ray = targetCamera.ScreenPointToRay(screenPoint);
            return Physics.Raycast(ray, out _, 1000f, hitTestLayers);
        }
#endif
    }
}
