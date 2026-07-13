using System;
using UnityEngine;

namespace DesktopWindow
{
    /// <summary>
    /// 빌드된 Windows 스탠드얼론 실행 파일의 창을 테두리 없는 투명 창으로 바꾸고,
    /// 화면 우하단에 고정 크기로 배치한 뒤 항상 위(Always On Top) 상태를 유지한다.
    /// 클릭은 그대로 통과되지 않고 창이 받는다(클릭 가능 상태).
    /// Win32 API 기반이라 Windows 빌드에서만 동작하며, 에디터/다른 플랫폼에서는 아무 동작도 하지 않는다.
    /// (macOS 등 다른 플랫폼에서 동일 기능이 필요하면 별도의 네이티브 플러그인 구현이 필요함)
    /// </summary>
    [DisallowMultipleComponent]
    public class TransparentWindowController : MonoBehaviour
    {
        [Header("Window Placement")]
        [SerializeField] private bool alwaysOnTop = true;
        [SerializeField] private int windowWidth = 480;
        [SerializeField] private int windowHeight = 640;
        [SerializeField] private int marginRight = 24;
        [SerializeField] private int marginBottom = 24;

        [Header("Rendering")]
        [SerializeField] private Camera targetCamera;

#if UNITY_STANDALONE_WIN
        private IntPtr hwnd;
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
            PlaceAtBottomRight();
#else
            Debug.LogWarning("[TransparentWindowController] 이 기능은 Win32 API 기반이라 Windows 빌드에서만 지원됩니다. 현재 플랫폼에서는 기본 창으로 동작합니다.");
#endif
        }

#if UNITY_STANDALONE_WIN
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

        private void PlaceAtBottomRight()
        {
            // 작업 표시줄을 제외한 작업 영역(work area) 기준으로 배치해야 taskbar에 가려지지 않는다.
            Win32Interop.RECT workArea = default;
            Win32Interop.SystemParametersInfo(Win32Interop.SPI_GETWORKAREA, 0, ref workArea, 0);

            int x = workArea.Right - windowWidth - marginRight;
            int y = workArea.Bottom - windowHeight - marginBottom;

            Win32Interop.SetWindowPos(hwnd, IntPtr.Zero, x, y, windowWidth, windowHeight, Win32Interop.SWP_NOZORDER);
        }
#endif
    }
}
