using System;
using UnityEngine;

#if UNITY_STANDALONE_WIN
using System.Runtime.InteropServices;
#endif

namespace DesktopWindow
{
    /// <summary>
    /// 빌드된 Windows 스탠드얼론 실행 파일의 창을 테두리 없는 투명 창으로 바꾸고 항상 위(Always On Top)
    /// 상태를 유지한다. Win32 API 기반이라 Windows 빌드에서만 동작하며, 에디터/다른 플랫폼에서는
    /// 아무 동작도 하지 않는다(macOS 등 다른 플랫폼에서 동일 기능이 필요하면 별도의 네이티브 플러그인
    /// 구현이 필요함).
    ///
    /// 기본 클릭 관통은 창 전체를 켰다 끄는 방식(WS_EX_TRANSPARENT)이 아니라, WM_NCHITTEST를
    /// 서브클래싱해서 픽셀 단위로 처리한다: controlDockRect의 실제 화면 좌표 안이면 HTCLIENT(정상
    /// 클릭 영역), 그 밖이면 HTTRANSPARENT(클릭 관통)를 매 히트테스트마다 반환한다. 그래서 ControlDock
    /// 버튼은 항상 클릭 가능하고, 그 외 투명 영역은 항상 클릭 관통 상태를 유지한다 - 모드 전환이 필요 없다.
    ///
    /// 창 이동은 두 가지 경로로 시작될 수 있다:
    /// - MoveHandle(기본 UX): ControlDock 안의 버튼이 OnPointerDown에서 BeginManualDrag()를 호출한다.
    /// - F9 배치 모드(레거시, 제거하지 않음): placementModeToggleKey로 전환하면 CustomWndProc이
    ///   ControlDock 판정을 건너뛰고 창 전체를 기본 처리(클릭 가능)로 넘겨서, 아무 곳이나 눌러서
    ///   드래그할 수 있다. WS_EX_TRANSPARENT는 어느 경로에서도 쓰지 않는다 - 클릭 라우팅은 항상
    ///   WM_NCHITTEST 서브클래싱 하나로만 처리한다.
    /// 두 경로 모두 GetCursorPos/GetAsyncKeyState 폴링으로 이동을 진행하고, 같은 저장 로직(마우스를
    /// 놓는 순간 WindowPlacementSaveSystem에 저장)을 공유한다.
    ///
    /// 배치 모드 전환 키는 GlobalKeyboardHook.ExcludedKey로 등록되어, 이 키를 눌러도
    /// AnyKeyDownThisFrame(공격/콤보가 구독하는 신호)에는 포함되지 않는다.
    ///
    /// DefaultExecutionOrder(-100): SizeToggleButton 등 다른 GameObject의 Awake가 이 컴포넌트의
    /// Awake보다 먼저 실행되면 Instance가 아직 null이라 시작 시점 SetSizeScale 호출을 놓친다
    /// (AudioManager와 같은 이유). Unity는 서로 다른 GameObject 간 Awake 순서를 보장하지 않으므로
    /// 이 컴포넌트의 Awake가 항상 먼저 실행되도록 명시적으로 앞당긴다.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public class TransparentWindowController : MonoBehaviour
    {
        public static TransparentWindowController Instance { get; private set; }

        [Header("Window Placement")]
        [SerializeField] private bool alwaysOnTop = true;
        [SerializeField] private int windowWidth = 480;
        [SerializeField] private int windowHeight = 640;
        [SerializeField] private int marginRight = 24;
        [SerializeField] private int marginBottom = 24;

        [Header("Control Dock (선택적 클릭 관통)")]
        [Tooltip("이 RectTransform의 화면 영역만 클릭 가능하게 남기고, 나머지는 계속 클릭 관통 처리한다.")]
        [SerializeField] private RectTransform controlDockRect;

        [Header("Placement Mode (레거시 - MoveHandle 사용을 기본으로 한다)")]
        [Tooltip("이 키로 배치 모드 On/Off를 전환한다. 공격/콤보 입력으로 처리되지 않도록 GlobalKeyboardHook에서 자동으로 제외 등록된다. 지원 범위: A-Z / 0-9 / F1-F15.")]
        [SerializeField] private KeyCode placementModeToggleKey = KeyCode.F9;

        [Header("Rendering")]
        [SerializeField] private Camera targetCamera;

        private bool isPlacementMode;

        /// <summary>tgl_size 배율(1 = 100%). windowWidth/windowHeight는 항상 "100% 기준" 값으로 두고,
        /// 실제 창 크기는 이 배율을 곱해서 계산한다(ScaledWidth/ScaledHeight).</summary>
        private float sizeScale = 1f;

        private int ScaledWidth => Mathf.Max(1, Mathf.RoundToInt(windowWidth * sizeScale));
        private int ScaledHeight => Mathf.Max(1, Mathf.RoundToInt(windowHeight * sizeScale));

#if UNITY_STANDALONE_WIN
        private IntPtr hwnd;
        private bool isDragging;
        private Win32Interop.POINT dragStartCursor;
        private Win32Interop.RECT dragStartWindowRect;

        private Win32Interop.WndProc wndProcDelegate;
        private IntPtr originalWndProc = IntPtr.Zero;
        private Win32Interop.RECT controlDockScreenRect;
        private readonly Vector3[] controlDockWorldCorners = new Vector3[4];
#endif

        private void Awake()
        {
            Instance = this;

            if (targetCamera == null)
            {
                targetCamera = Camera.main;
            }

            // 에디터에서도 이 키가 AnyKeyDownThisFrame(공격/콤보)로 새지 않도록 항상 등록해둔다 -
            // 실제 창 이동/클릭 관통 토글은 Windows 빌드에서만 일어나지만, 제외 등록 자체는
            // 플랫폼과 무관하게 필요하다.
            GlobalKeyboardHook.ExcludedKey = placementModeToggleKey;
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
            // 클릭 관통은 창 전체 토글(WS_EX_TRANSPARENT)이 아니라 WM_NCHITTEST 서브클래싱으로
            // 픽셀 단위로 처리한다.
            InstallHitTestSubclass();
            ApplyStartupPlacement();
#else
            Debug.LogWarning("[TransparentWindowController] 이 기능은 Win32 API 기반이라 Windows 빌드에서만 지원됩니다. 현재 플랫폼에서는 기본 창으로 동작합니다.");
#endif
        }

        private void Update()
        {
            if (GlobalKeyboardHook.ExcludedKeyDownThisFrame)
            {
                TogglePlacementMode();
            }

#if UNITY_STANDALONE_WIN
            if (hwnd == IntPtr.Zero) return;

            RecomputeControlDockScreenRect();

            if (isPlacementMode && !isDragging)
            {
                TryStartDragFromPlacementMode();
            }

            if (isDragging)
            {
                ContinueOrEndDrag();
            }
#endif
        }

#if UNITY_STANDALONE_WIN
        private void OnApplicationQuit()
        {
            if (hwnd != IntPtr.Zero)
            {
                UninstallHitTestSubclass();
                SaveCurrentWindowPosition();
            }
        }
#endif

        private void TogglePlacementMode()
        {
            isPlacementMode = !isPlacementMode;

#if UNITY_STANDALONE_WIN
            // WS_EX_TRANSPARENT는 쓰지 않는다 - 클릭 라우팅은 WM_NCHITTEST 서브클래싱(CustomWndProc)이
            // 전담한다. isPlacementMode가 true인 동안은 CustomWndProc이 ControlDock 밖에서도 항상
            // 기본 처리(클릭 가능)로 넘기도록 분기하므로, 여기서는 상태만 뒤집으면 된다.
            if (!isPlacementMode)
            {
                isDragging = false;
                SaveCurrentWindowPosition(); // 배치 모드 종료 시 저장
            }
#endif

            Debug.Log(isPlacementMode
                ? "[TransparentWindowController] 배치 모드 ON - 창 전체가 클릭 가능해집니다. 아무 곳이나 눌러서 드래그하세요."
                : "[TransparentWindowController] 배치 모드 OFF - ControlDock 영역만 계속 클릭 가능하고, 나머지는 클릭 관통 상태로 돌아갑니다.");
        }

        /// <summary>
        /// MoveHandle이 OnPointerDown에서 호출하는 진입점. Unity UI가 이미 클릭이 MoveHandle 위에서
        /// 시작됐음을 보장하므로 별도의 위치 판정 없이 바로 드래그를 시작한다. MoveHandleDrag(크로스
        /// 플랫폼 스크립트)가 호출하므로, 이 메서드 자체는 #if 밖에 있어야 컴파일이 깨지지 않는다 -
        /// 실제 Win32 동작만 내부에서 플랫폼 가드로 감싼다.
        /// </summary>
        public void BeginManualDrag()
        {
#if UNITY_STANDALONE_WIN
            if (hwnd == IntPtr.Zero || isDragging) return;

            isDragging = true;
            Win32Interop.GetCursorPos(out dragStartCursor);
            Win32Interop.GetWindowRect(hwnd, out dragStartWindowRect);
#endif
        }

        /// <summary>
        /// tgl_size 버튼(SizeToggleButton, 크로스 플랫폼 스크립트)이 호출하는 진입점. 이 메서드 자체는
        /// #if 밖에 있어야 컴파일이 깨지지 않는다 - 실제 Win32 리사이즈만 내부에서 플랫폼 가드로 감싼다.
        /// 창 오른쪽 아래 모서리를 고정한 채(배치가 보통 화면 우하단 근처라 이 앵커가 자연스럽다)
        /// windowWidth/windowHeight 기준 배율만큼 크기를 다시 잡는다. TransparentWindowController.Start()
        /// 보다 먼저(다른 컴포넌트의 Awake에서) 호출되면 hwnd가 아직 없으므로 배율만 기억해뒀다가
        /// ApplyStartupPlacement()가 그 값을 그대로 반영한다.
        /// </summary>
        public void SetSizeScale(float scale)
        {
            if (scale <= 0f || float.IsNaN(scale) || float.IsInfinity(scale)) return;
            sizeScale = scale;

#if UNITY_STANDALONE_WIN
            if (hwnd != IntPtr.Zero)
            {
                ResizeKeepingBottomRightAnchor();
            }
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

        /// <summary>
        /// 시작 시 배치 규칙: 저장된 위치가 있고 지금 연결된 모니터 표시 영역 안에 있으면 그 위치로
        /// 복원한다. 없거나 유효하지 않으면(예: 저장 이후 모니터 구성이 바뀌어 화면 밖으로 벗어난 경우)
        /// 커서가 있는 모니터의 작업 영역 우하단에 안전하게 배치한다.
        /// </summary>
        private void ApplyStartupPlacement()
        {
            WindowPlacementData saved = WindowPlacementSaveSystem.Load();

            if (saved != null && saved.hasSavedPosition && IsPositionOnAnyMonitor(saved.positionX, saved.positionY))
            {
                Win32Interop.SetWindowPos(hwnd, IntPtr.Zero, saved.positionX, saved.positionY,
                    ScaledWidth, ScaledHeight, Win32Interop.SWP_NOZORDER);
                return;
            }

            PlaceAtCursorMonitorBottomRight();
        }

        /// <summary>이 점이 현재 연결된 모니터 중 어느 하나의 표시 영역(전체 모니터 사각형) 안에 있는지.</summary>
        private static bool IsPositionOnAnyMonitor(int x, int y)
        {
            var point = new Win32Interop.POINT { X = x, Y = y };
            // MONITOR_DEFAULTTONULL: 어떤 모니터에도 속하지 않으면 null을 반환한다(가장 가까운 모니터로
            // 보정하지 않음) - "표시 영역에도 없으면 복원하지 않는다" 판정에 그대로 쓸 수 있다.
            IntPtr monitor = Win32Interop.MonitorFromPoint(point, Win32Interop.MONITOR_DEFAULTTONULL);
            return monitor != IntPtr.Zero;
        }

        private void PlaceAtCursorMonitorBottomRight()
        {
            Win32Interop.RECT workArea;

            if (Win32Interop.GetCursorPos(out Win32Interop.POINT cursor))
            {
                // 커서가 있는 모니터를 찾는다 - 여기서는 항상 어딘가의 모니터에 보정되어야 하므로 NEAREST를 쓴다.
                IntPtr monitor = Win32Interop.MonitorFromPoint(cursor, Win32Interop.MONITOR_DEFAULTTONEAREST);
                var info = new Win32Interop.MONITORINFO { cbSize = Marshal.SizeOf(typeof(Win32Interop.MONITORINFO)) };

                if (monitor != IntPtr.Zero && Win32Interop.GetMonitorInfo(monitor, ref info))
                {
                    workArea = info.rcWork; // 작업 표시줄을 제외한 작업 영역
                }
                else
                {
                    workArea = default;
                    Win32Interop.SystemParametersInfo(Win32Interop.SPI_GETWORKAREA, 0, ref workArea, 0);
                }
            }
            else
            {
                // 커서 위치를 못 가져오면 기존 방식(주 모니터 작업 영역)으로 안전하게 대체한다.
                workArea = default;
                Win32Interop.SystemParametersInfo(Win32Interop.SPI_GETWORKAREA, 0, ref workArea, 0);
            }

            int x = workArea.Right - ScaledWidth - marginRight;
            int y = workArea.Bottom - ScaledHeight - marginBottom;

            Win32Interop.SetWindowPos(hwnd, IntPtr.Zero, x, y, ScaledWidth, ScaledHeight, Win32Interop.SWP_NOZORDER);
        }

        /// <summary>SetSizeScale이 hwnd가 이미 있을 때 호출하는 실제 리사이즈. 오른쪽 아래 모서리를 고정한다.</summary>
        private void ResizeKeepingBottomRightAnchor()
        {
            Win32Interop.GetWindowRect(hwnd, out Win32Interop.RECT current);

            int newWidth = ScaledWidth;
            int newHeight = ScaledHeight;
            int newX = current.Right - newWidth;
            int newY = current.Bottom - newHeight;

            Win32Interop.SetWindowPos(hwnd, IntPtr.Zero, newX, newY, newWidth, newHeight, Win32Interop.SWP_NOZORDER);
            SaveCurrentWindowPosition(); // 크기와 함께 위치도 바뀌었으니 같이 저장한다
        }

        /// <summary>
        /// F9 배치 모드 전용 드래그 시작 판정. "클릭이 눌린 순간 커서가 창 위에 있었는지"로만 시작해서,
        /// 배치 모드 중 창 밖 클릭까지 드래그로 오인하지 않는다. MoveHandle 쪽은 Unity UI 이벤트가 이미
        /// "MoveHandle 위에서 눌렸다"를 보장하므로 이 판정 없이 BeginManualDrag()로 바로 시작한다.
        /// </summary>
        private void TryStartDragFromPlacementMode()
        {
            if (!Win32Interop.GetCursorPos(out Win32Interop.POINT cursor)) return;
            bool leftDown = (Win32Interop.GetAsyncKeyState(Win32Interop.VK_LBUTTON) & 0x8000) != 0;
            if (!leftDown || !IsPointInWindow(cursor)) return;

            isDragging = true;
            dragStartCursor = cursor;
            Win32Interop.GetWindowRect(hwnd, out dragStartWindowRect);
        }

        /// <summary>
        /// 왼쪽 마우스 버튼 상태와 커서의 절대 화면 좌표를 폴링해서 드래그를 진행/종료한다
        /// (GetAsyncKeyState + GetCursorPos). 창에 캡션/테두리가 없어서 OS가 기본 제공하는 타이틀바
        /// 드래그를 쓸 수 없고, Unity의 Input 이벤트는 커서가 창 밖으로 빠르게 나가면 놓칠 수 있어서
        /// 대신 전역 상태를 직접 폴링한다. F9 배치 모드/MoveHandle 어느 쪽으로 시작했든 공유한다.
        /// </summary>
        private void ContinueOrEndDrag()
        {
            if (!Win32Interop.GetCursorPos(out Win32Interop.POINT cursor)) return;
            bool leftDown = (Win32Interop.GetAsyncKeyState(Win32Interop.VK_LBUTTON) & 0x8000) != 0;

            if (leftDown)
            {
                int deltaX = cursor.X - dragStartCursor.X;
                int deltaY = cursor.Y - dragStartCursor.Y;
                int newX = dragStartWindowRect.Left + deltaX;
                int newY = dragStartWindowRect.Top + deltaY;

                Win32Interop.SetWindowPos(hwnd, IntPtr.Zero, newX, newY, 0, 0,
                    Win32Interop.SWP_NOSIZE | Win32Interop.SWP_NOZORDER);
            }
            else
            {
                isDragging = false;
                SaveCurrentWindowPosition(); // 이동 완료 후 저장
            }
        }

        private bool IsPointInWindow(Win32Interop.POINT p)
        {
            if (!Win32Interop.GetWindowRect(hwnd, out Win32Interop.RECT rect)) return false;
            return p.X >= rect.Left && p.X <= rect.Right && p.Y >= rect.Top && p.Y <= rect.Bottom;
        }

        private void SaveCurrentWindowPosition()
        {
            if (!Win32Interop.GetWindowRect(hwnd, out Win32Interop.RECT rect)) return;

            WindowPlacementSaveSystem.Save(new WindowPlacementData
            {
                hasSavedPosition = true,
                positionX = rect.Left,
                positionY = rect.Top,
            });
        }

        /// <summary>WM_NCHITTEST를 가로채는 WndProc으로 교체한다. 원래 WndProc은 다른 모든 메시지를 그대로 넘기는 데 쓴다.</summary>
        private void InstallHitTestSubclass()
        {
            wndProcDelegate = CustomWndProc;
            IntPtr newProcPtr = Marshal.GetFunctionPointerForDelegate(wndProcDelegate);
            originalWndProc = Win32Interop.SetWindowLongPtr(hwnd, Win32Interop.GWLP_WNDPROC, newProcPtr);

            if (originalWndProc == IntPtr.Zero)
            {
                Debug.LogError("[TransparentWindowController] WM_NCHITTEST 서브클래싱에 실패했습니다. ControlDock이 클릭되지 않을 수 있습니다.");
            }
        }

        private void UninstallHitTestSubclass()
        {
            if (originalWndProc == IntPtr.Zero) return;
            Win32Interop.SetWindowLongPtr(hwnd, Win32Interop.GWLP_WNDPROC, originalWndProc);
            originalWndProc = IntPtr.Zero;
        }

        /// <summary>
        /// controlDockScreenRect 안이면 기존 WndProc(기본 처리, 정상 클릭)으로 넘기고, 밖이면
        /// HTTRANSPARENT를 반환해 이 지점의 클릭을 아래 창으로 통과시킨다. F9 배치 모드 중에는
        /// ControlDock 판정 자체를 건너뛰고 창 전체를 기본 처리(클릭 가능)로 넘긴다. 그 외 메시지는
        /// 전부 원래 WndProc으로 그대로 전달한다.
        /// </summary>
        private IntPtr CustomWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == Win32Interop.WM_NCHITTEST && !isPlacementMode)
            {
                long raw = lParam.ToInt64();
                int x = unchecked((short)(raw & 0xFFFF));
                int y = unchecked((short)((raw >> 16) & 0xFFFF));

                bool insideDock = x >= controlDockScreenRect.Left && x <= controlDockScreenRect.Right &&
                                   y >= controlDockScreenRect.Top && y <= controlDockScreenRect.Bottom;

                if (!insideDock)
                {
                    return new IntPtr(Win32Interop.HTTRANSPARENT);
                }
            }

            return Win32Interop.CallWindowProc(originalWndProc, hWnd, msg, wParam, lParam);
        }

        /// <summary>
        /// controlDockRect(Canvas UI, Overlay 기준)의 월드 좌표 4모서리를 Unity 스크린 좌표로 바꾼 뒤,
        /// 창 클라이언트 영역의 화면상 원점(ClientToScreen)을 더하고 Y축을 뒤집어(Unity는 좌하단 원점,
        /// Win32는 좌상단 원점) 네이티브 화면 좌표 사각형으로 캐싱한다. 매 프레임 다시 계산해서
        /// Canvas 레이아웃이 바뀌어도 항상 최신 좌표를 쓴다.
        /// </summary>
        private void RecomputeControlDockScreenRect()
        {
            if (controlDockRect == null)
            {
                controlDockScreenRect = default;
                return;
            }

            controlDockRect.GetWorldCorners(controlDockWorldCorners); // 0=bottom-left, 2=top-right

            // Screen Space - Overlay 캔버스 기준이라 카메라 없이(null) 바로 스크린 좌표로 변환된다.
            Vector2 minScreen = RectTransformUtility.WorldToScreenPoint(null, controlDockWorldCorners[0]);
            Vector2 maxScreen = RectTransformUtility.WorldToScreenPoint(null, controlDockWorldCorners[2]);

            int height = Screen.height;
            var clientOrigin = new Win32Interop.POINT { X = 0, Y = 0 };
            Win32Interop.ClientToScreen(hwnd, ref clientOrigin);

            controlDockScreenRect = new Win32Interop.RECT
            {
                Left = clientOrigin.X + Mathf.RoundToInt(minScreen.x),
                Right = clientOrigin.X + Mathf.RoundToInt(maxScreen.x),
                Top = clientOrigin.Y + (height - Mathf.RoundToInt(maxScreen.y)),
                Bottom = clientOrigin.Y + (height - Mathf.RoundToInt(minScreen.y)),
            };
        }
#endif
    }
}
