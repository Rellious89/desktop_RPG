using System;
using System.Collections;
using UnityEngine;

#if UNITY_STANDALONE_WIN
using System.Runtime.InteropServices;
using System.Text;
#endif

namespace DesktopWindow
{
    /// <summary>
    /// 빌드된 Windows 스탠드얼론 실행 파일의 창을 테두리 없는 투명 창으로 바꾸고 항상 위(Always On Top)
    /// 상태를 유지한다. Win32 API 기반이라 Windows 빌드에서만 동작하며, 에디터/다른 플랫폼에서는
    /// 아무 동작도 하지 않는다(macOS 등 다른 플랫폼에서 동일 기능이 필요하면 별도의 네이티브 플러그인
    /// 구현이 필요함).
    ///
    /// 클릭 관통은 WS_EX_TRANSPARENT 켜고 끄기로 처리한다(OS 레벨). 예전에는 WM_NCHITTEST를
    /// 서브클래싱해서 픽셀 단위로 처리했는데, 그 방식은 커서가 창 위를 지날 때마다 OS가 우리 창의
    /// WndProc에 히트테스트를 묻고 응답을 기다려야 했다 - 이 앱이 FpsLimiter로 30fps 제한이라
    /// 응답 자체가 프레임 주기에 묶여 지연됐고, 결과적으로 마우스 반응성 저하의 한 원인이었다.
    /// 지금은 Update()에서 GetCursorPos로 커서 위치를 읽어 controlDockScreenRect(ControlDock의
    /// 화면 좌표, RecomputeControlDockScreenRect가 계산) 안인지만 비교한다: 안이면 WS_EX_TRANSPARENT
    /// OFF(클릭 가능), 밖이면 ON(클릭 관통) - 상태가 실제로 바뀔 때만 SetWindowLong을 호출해서
    /// 불필요한 시스템 콜을 피한다. 반응이 최대 한 프레임(~33ms) 늦을 수 있지만 OS가 우리 창의
    /// 응답을 기다리는 구간 자체가 없어져서 다른 프로그램 입력을 막지 않는다.
    ///
    /// 창 이동은 두 가지 경로로 시작될 수 있다:
    /// - MoveHandle(기본 UX): ControlDock 안의 버튼이 OnPointerDown에서 BeginManualDrag()를 호출한다.
    /// - F9 배치 모드(레거시, 제거하지 않음): placementModeToggleKey로 전환하면 isPlacementMode 동안
    ///   ControlDock 판정과 무관하게 WS_EX_TRANSPARENT를 강제로 끈다(창 전체 클릭 가능) - 아무 곳이나
    ///   눌러서 드래그할 수 있다. 모드 종료 시 커서 위치 기준으로 즉시 재평가된다.
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
        [Tooltip("이 RectTransform의 화면 영역 위에 커서가 있을 때만 클릭 가능하게 하고, 나머지는 클릭 관통 처리한다.")]
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
        /// <summary>hwnd를 찾기 위해 재시도하는 총 시간 한도. Unity 시작 타이밍에 따라 메인 창이 아직
        /// 활성화(포그라운드)되지 않았을 수 있어 즉시 실패시키지 않고 짧게 폴링한다.</summary>
        private const float HandleSearchTimeoutSeconds = 5f;
        private const float HandleSearchIntervalSeconds = 0.1f;

        private IntPtr hwnd;
        private bool isDragging;
        private Win32Interop.POINT dragStartCursor;
        private Win32Interop.RECT dragStartWindowRect;

        private Win32Interop.RECT controlDockScreenRect;
        private readonly Vector3[] controlDockWorldCorners = new Vector3[4];

        /// <summary>지금 실제로 적용되어 있는 WS_EX_TRANSPARENT 상태 캐시. 값이 바뀔 때만 SetWindowLong을 호출한다.</summary>
        private bool clickThroughApplied;
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
            StartCoroutine(FindWindowHandleAndInitialize());
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
            UpdateClickThroughState();

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
                SaveCurrentWindowPosition();
            }
        }
#endif

        private void TogglePlacementMode()
        {
            isPlacementMode = !isPlacementMode;

#if UNITY_STANDALONE_WIN
            // WS_EX_TRANSPARENT 자체는 여기서 건드리지 않는다 - 다음 Update()의
            // UpdateClickThroughState()가 isPlacementMode 값을 보고 바로 재평가한다(같은 프레임 안에서
            // 이 메서드 호출 직후 실행되므로 지연 없이 반영된다).
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

        /// <summary>
        /// GetActiveWindow()/GetForegroundWindow() 단발 호출에 의존하지 않는다 - 둘 다 호출 시점에
        /// 우리 창이 활성/포그라운드가 아니면(Unity 시작 타이밍 문제, 혹은 시작 직후 다른 프로그램
        /// 창이 앞에 있는 경우) IntPtr.Zero를 반환하거나 엉뚱한 창을 잡을 수 있다. 특히
        /// GetForegroundWindow만 단독으로 쓰면 시작 순간 우연히 앞에 있던 다른 프로그램 창의 스타일을
        /// 잘못 바꿔버릴 위험이 있어 사용하지 않는다. 대신 현재 프로세스 PID가 소유한 visible
        /// top-level 창을 EnumWindows로 직접 찾고, 창이 아직 생성되지 않았을 케이스에 대비해
        /// 100ms 간격으로 최대 5초까지 재시도한다. 핸들을 찾으면 InitializeWindow()가 정확히 한 번만
        /// 실행된다.
        /// </summary>
        private IEnumerator FindWindowHandleAndInitialize()
        {
            uint currentPid = Win32Interop.GetCurrentProcessId();
            int attempts = 0;
            int lastWin32Error = 0;
            float elapsed = 0f;

            while (elapsed < HandleSearchTimeoutSeconds)
            {
                attempts++;
                // 검색 제한 시간 동안에는 Unity 플레이어 클래스가 확인된 preferred 창만 채택한다.
                // 같은 PID의 보조/스플래시 창을 먼저 발견해 스타일을 잘못 바꾸는 첫 실행 레이스를 막는다.
                IntPtr found = FindOwnedTopLevelWindow(currentPid, false,
                    out lastWin32Error, out string className, out bool isPreferred);
                if (found != IntPtr.Zero)
                {
                    hwnd = found;
                    InitializeWindow(currentPid, attempts, className, isPreferred);
                    yield break;
                }

                yield return new WaitForSeconds(HandleSearchIntervalSeconds);
                elapsed += HandleSearchIntervalSeconds;
            }

            // Unity 버전별 클래스명 차이에 대비한 최후의 안전망. 5초 동안 preferred를 찾지 못한
            // 경우에만 같은 PID의 visible top-level 창을 fallback으로 허용하고 명확히 경고한다.
            attempts++;
            IntPtr fallback = FindOwnedTopLevelWindow(currentPid, true,
                out lastWin32Error, out string fallbackClassName, out bool fallbackIsPreferred);
            if (fallback != IntPtr.Zero)
            {
                Debug.LogWarning($"[TransparentWindowController] preferred Unity 창을 제한 시간 안에 찾지 못해 " +
                    $"fallback 창을 사용합니다. (hwnd: {fallback}, PID: {currentPid}, " +
                    $"class: {fallbackClassName}, attempts: {attempts})");
                hwnd = fallback;
                InitializeWindow(currentPid, attempts, fallbackClassName, fallbackIsPreferred);
                yield break;
            }

            Debug.LogError($"[TransparentWindowController] 윈도우 핸들을 가져오지 못했습니다. " +
                $"(시도 횟수: {attempts}, PID: {currentPid}, 마지막 Win32 오류 코드: {lastWin32Error})");
        }

        /// <summary>
        /// EnumWindows로 모든 최상위 창을 순회하며 현재 프로세스(PID) 소유의 visible 창을 찾는다.
        /// class name이 "UnityWndClass"를 포함하는 창을 우선으로 채택하고(Unity 메인 플레이어 창
        /// 확인), 없으면 같은 PID의 첫 visible 최상위 창을 대체 후보로 반환한다 - class name 규칙은
        /// Unity 버전/설정에 따라 달라질 수 있어 참고용 검증이지 필수 조건은 아니다.
        /// </summary>
        private static IntPtr FindOwnedTopLevelWindow(uint currentPid, bool allowFallback,
            out int lastWin32Error, out string className, out bool isPreferred)
        {
            lastWin32Error = 0;
            IntPtr fallbackCandidate = IntPtr.Zero;
            string fallbackClassName = string.Empty;
            IntPtr preferredCandidate = IntPtr.Zero;
            string preferredClassName = string.Empty;

            Win32Interop.EnumWindows((candidateHwnd, _) =>
            {
                if (!Win32Interop.IsWindowVisible(candidateHwnd)) return true;

                Win32Interop.GetWindowThreadProcessId(candidateHwnd, out uint pid);
                if (pid != currentPid) return true;

                string candidateClassName = GetWindowClassName(candidateHwnd);

                if (fallbackCandidate == IntPtr.Zero)
                {
                    fallbackCandidate = candidateHwnd;
                    fallbackClassName = candidateClassName;
                }

                // 실제 Windows 플레이어에서 "UnityWndClass"와 "Unity WndClass"가 모두 관찰됐다.
                // 공백 차이를 제거한 뒤 비교해 두 표기를 동일한 preferred 클래스로 취급한다.
                string normalizedClassName = candidateClassName.Replace(" ", string.Empty);
                if (normalizedClassName.IndexOf("UnityWndClass", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    preferredCandidate = candidateHwnd;
                    preferredClassName = candidateClassName;
                    return false; // 원하는 class name을 찾았으니 순회를 멈춘다
                }

                return true;
            }, IntPtr.Zero);

            if (preferredCandidate != IntPtr.Zero)
            {
                className = preferredClassName;
                isPreferred = true;
                return preferredCandidate;
            }

            if (allowFallback && fallbackCandidate != IntPtr.Zero)
            {
                className = fallbackClassName;
                isPreferred = false;
                return fallbackCandidate;
            }

            className = string.Empty;
            isPreferred = false;
            lastWin32Error = Marshal.GetLastWin32Error();
            return IntPtr.Zero;
        }

        private static string GetWindowClassName(IntPtr targetHwnd)
        {
            var buffer = new StringBuilder(256);
            int length = Win32Interop.GetClassName(targetHwnd, buffer, buffer.Capacity);
            return length > 0 ? buffer.ToString() : string.Empty;
        }

        /// <summary>
        /// FindWindowHandleAndInitialize()가 핸들을 찾은 직후 정확히 한 번만 호출된다. 스타일을
        /// 바꾸기 전에 IsWindow와 GetWindowThreadProcessId로 핸들이 여전히 유효하고 현재 프로세스
        /// 소유인지 다시 한번 검증한다(탐색과 초기화 사이 창이 파괴되는 극단적인 경우 대비).
        /// </summary>
        private void InitializeWindow(uint currentPid, int attempts, string className, bool isPreferredCandidate)
        {
            if (!Win32Interop.IsWindow(hwnd))
            {
                Debug.LogError("[TransparentWindowController] 찾은 윈도우 핸들이 더 이상 유효하지 않습니다.");
                hwnd = IntPtr.Zero;
                return;
            }

            Win32Interop.GetWindowThreadProcessId(hwnd, out uint ownerPid);
            if (ownerPid != currentPid)
            {
                Debug.LogError("[TransparentWindowController] 찾은 윈도우 핸들이 현재 프로세스 소유가 아닙니다.");
                hwnd = IntPtr.Zero;
                return;
            }

            string candidateType = isPreferredCandidate ? "preferred" : "fallback";
            Debug.Log($"[TransparentWindowController] 창 핸들 획득 성공 " +
                $"(hwnd: {hwnd}, PID: {currentPid}, ownerPID: {ownerPid}, class: {className}, " +
                $"attempts: {attempts}, candidate: {candidateType})");

            RemoveWindowBorder();
            EnableWindowTransparency();
            // 기본 상태는 클릭 관통(ON)이다 - Update()의 UpdateClickThroughState()가 매 프레임
            // 커서 위치를 보고 필요할 때만(ControlDock 위/배치 모드) 끈다.
            ApplyClickThrough(true);
            ApplyStartupPlacement();
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

        /// <summary>WS_EX_TRANSPARENT를 켜고 끈다. Always On Top(z-order)은 건드리지 않는다.</summary>
        private void ApplyClickThrough(bool passThrough)
        {
            int exStyle = Win32Interop.GetWindowLong(hwnd, Win32Interop.GWL_EXSTYLE);
            exStyle = passThrough
                ? exStyle | (int)Win32Interop.WS_EX_TRANSPARENT
                : exStyle & ~(int)Win32Interop.WS_EX_TRANSPARENT;
            Win32Interop.SetWindowLong(hwnd, Win32Interop.GWL_EXSTYLE, (uint)exStyle);
            clickThroughApplied = passThrough;
        }

        /// <summary>
        /// 매 프레임 커서 위치만 보고 클릭 관통 여부를 재평가한다. F9 배치 모드 중에는 ControlDock
        /// 판정과 무관하게 항상 클릭 가능(관통 OFF)하게 한다. 상태가 실제로 바뀔 때만 ApplyClickThrough
        /// (SetWindowLong 호출)를 실행해서 불필요한 시스템 콜을 피한다.
        /// </summary>
        private void UpdateClickThroughState()
        {
            bool shouldPassThrough;

            if (isPlacementMode)
            {
                shouldPassThrough = false;
            }
            else
            {
                Win32Interop.GetCursorPos(out Win32Interop.POINT cursor);
                bool insideDock = cursor.X >= controlDockScreenRect.Left && cursor.X <= controlDockScreenRect.Right &&
                                   cursor.Y >= controlDockScreenRect.Top && cursor.Y <= controlDockScreenRect.Bottom;
                shouldPassThrough = !insideDock;
            }

            if (shouldPassThrough == clickThroughApplied) return;

            ApplyClickThrough(shouldPassThrough);
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
