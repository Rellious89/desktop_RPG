using System;
using System.Collections;
using Common;
using UnityEngine;

#if UNITY_STANDALONE_WIN
using System.Collections.Generic;
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
    /// 화면 호스트 구조: 이 창은 항상 "현재 선택된 모니터의 Work Area 전체"를 덮는 전체 화면 투명
    /// Overlay다(작업 표시줄은 Work Area가 이미 제외하므로 따로 가리지 않는다). 그 안에서
    /// 캐릭터/적/이펙트(StageVisualRoot)와 UI 그룹(GameHUDGroup/ControlDockGroup)만 화면 구석에
    /// 작게 배치된다 - 배율/이동은 모두 Common 네임스페이스의 StageVisualRootController/
    /// UiGroupDraggable/LayoutModeController가 담당한다. 이 클래스는 "어느 모니터를 쓸지"와 "그
    /// 모니터의 Work Area 전체를 덮는 것", 그리고 클릭 관통/드래그 폴링(Win32 API)만 책임진다.
    ///
    /// 클릭 관통은 WS_EX_TRANSPARENT 켜고 끄기로 처리한다(OS 레벨). 예전에는 WM_NCHITTEST를
    /// 서브클래싱해서 픽셀 단위로 처리했는데, 그 방식은 커서가 창 위를 지날 때마다 OS가 우리 창의
    /// WndProc에 히트테스트를 묻고 응답을 기다려야 했다 - 이 앱이 FpsLimiter로 30fps 제한이라
    /// 응답 자체가 프레임 주기에 묶여 지연됐고, 결과적으로 마우스 반응성 저하의 한 원인이었다.
    /// 지금은 Update()에서 GetCursorPos로 커서 위치를 읽어 controlDockScreenRect(ControlDock의
    /// 화면 좌표, RecomputeControlDockScreenRect가 계산) 안인지만 비교한다: 안이면 WS_EX_TRANSPARENT
    /// OFF(클릭 가능), 밖이면 ON(클릭 관통) - 상태가 실제로 바뀔 때만 SetWindowLong을 호출해서
    /// 불필요한 시스템 콜을 피한다. 창이 모니터 전체로 커진 지금은 이 "그 외 전 영역은 클릭 관통"
    /// 원칙이 오히려 더 중요해졌다 - GlobalMouseWheelForwarder가 마우스 휠도 같은 원칙으로 별도 처리한다.
    ///
    /// Layout Mode: StageVisualRoot/GameHUDGroup/ControlDockGroup 세 그룹을 직접 드래그로 배치하는
    /// 모드다(Common.LayoutModeController가 상태와 세 그룹을 소유). 진입 경로는 두 가지다:
    /// - ControlDock의 배치 버튼(LayoutModeToggleButton, 크로스 플랫폼 스크립트) 클릭
    /// - F9 키(placementModeToggleKey, 레거시 - 제거하지 않음)
    /// 둘 다 LayoutModeController.ToggleLayoutMode()를 호출할 뿐이다. Layout Mode 중에는
    /// UpdateClickThroughState가 ControlDock 판정 대신 세 그룹의 화면 영역(LayoutModeController.AllGroups)
    /// 중 하나 안에 있는지로 클릭 관통 여부를 정한다. StageVisualRoot는 UI가 아니라서 자체 영역
    /// 안에서 마우스 버튼이 눌리면 이 클래스가 직접 드래그를 시작하고(TryStartStageDragInLayoutMode),
    /// GameHUDGroup/ControlDockGroup은 UiGroupDraggable의 OnPointerDown(Unity UI 이벤트)이 드래그를
    /// 시작한다 - 어느 쪽이든 실제 드래그 진행/종료 폴링(GetCursorPos/GetAsyncKeyState)은 이 클래스가
    /// 소유하고, 매 프레임 커서 델타를 LayoutModeController.ApplyActiveDragDeltaPixels로 넘겨서 지금
    /// 드래그 중인 그룹에만 반영한다(네이티브 창 자체는 절대 움직이지 않는다).
    ///
    /// 배치 모드 전환 키는 GlobalKeyboardHook.ExcludedKey로 등록되어, 이 키를 눌러도
    /// AnyKeyDownThisFrame(공격/콤보가 구독하는 신호)에는 포함되지 않는다.
    ///
    /// DefaultExecutionOrder(-100): SizeToggleButton 등 다른 GameObject의 Awake가 이 컴포넌트의
    /// Awake보다 먼저 실행되면 Instance가 아직 null이라 시작 시점 호출을 놓친다(AudioManager와 같은
    /// 이유). Unity는 서로 다른 GameObject 간 Awake 순서를 보장하지 않으므로 이 컴포넌트의 Awake가
    /// 항상 먼저 실행되도록 명시적으로 앞당긴다. StageVisualRootController/LayoutModeController도
    /// 같은 이유로 같은 실행 순서를 쓴다.
    ///
    /// 모니터 선택: 현재 창이 위치한 모니터는 항상 MonitorFromWindow로 판정한다(커서 기준 아님).
    /// 모니터를 바꾸는 유일한 방법은 명시적으로 MoveOverlayToNextMonitor()를 호출하는 것뿐이다(드래그로
    /// 다른 모니터에 걸치는 개념 자체가 없다 - 창은 항상 정확히 한 모니터의 Work Area 전체와 같다).
    /// 매 프레임(Update, 드래그 중이 아닐 때) CheckForMonitorWorkAreaChange가 (1) 선택된 모니터가
    /// 여전히 연결돼 있는지, (2) 그 모니터의 Work Area 사각형이 바뀌었는지(작업 표시줄 크기 변경,
    /// 해상도 변경 등)만 확인해서 필요할 때만 창을 다시 맞춘다 - WM_DPICHANGED/WM_DISPLAYCHANGE
    /// 같은 메시지를 받으려면 커스텀 WndProc 서브클래싱이 필요한데, 위에 적었듯 이 앱은 서브클래싱을
    /// 의도적으로 피한다. 폴링 비용은 GetCursorPos/GetWindowRect와 동급의 저렴한 syscall이라 기존
    /// 폴링 루프에 자연스럽게 얹을 수 있다.
    ///
    /// DPI는 크기 계산에 쓰지 않는다(창 크기 = Work Area 픽셀 크기 그 자체). 다만 GetMonitorInfo류
    /// API가 모니터 좌표/Work Area를 올바르게(가상화되지 않은 실제 값으로) 돌려주려면 프로세스가
    /// Per-Monitor DPI 인식 상태여야 하므로, DpiAwarenessBootstrap이 앱 시작 시 한 번 Per-Monitor V2로
    /// 설정하고, isPerMonitorDpiAware는 그게 실제로 적용됐는지 진단용으로만 로그에 남긴다.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public class TransparentWindowController : MonoBehaviour
    {
        public static TransparentWindowController Instance { get; private set; }

        [Header("Window")]
        [SerializeField] private bool alwaysOnTop = true;

        [Header("Control Dock (선택적 클릭 관통)")]
        [Tooltip("이 RectTransform의 화면 영역 위에 커서가 있을 때만 클릭 가능하게 하고, 나머지는 클릭 관통 처리한다.")]
        [SerializeField] private RectTransform controlDockRect;

        [Header("Layout Mode (레거시 키보드 단축키 - ControlDock 배치 버튼 사용을 기본으로 한다)")]
        [Tooltip("이 키로 Layout Mode On/Off를 전환한다(LayoutModeController.ToggleLayoutMode). 공격/콤보 입력으로 처리되지 않도록 GlobalKeyboardHook에서 자동으로 제외 등록된다. 지원 범위: A-Z / 0-9 / F1-F15.")]
        [SerializeField] private KeyCode placementModeToggleKey = KeyCode.F9;

        [Header("Rendering")]
        [SerializeField] private Camera targetCamera;

        [Header("진단용 (Layout Mode 클릭 판정 로그)")]
        [Tooltip("켜면 클릭 관통 상태가 바뀔 때마다 [LayoutHitTest] 로그로 현재 모드/커서 좌표/세 그룹 판정 결과를 남긴다.")]
        [SerializeField] private bool logLayoutHitTests = true;

#if UNITY_STANDALONE_WIN
        /// <summary>hwnd를 찾기 위해 재시도하는 총 시간 한도. Unity 시작 타이밍에 따라 메인 창이 아직
        /// 활성화(포그라운드)되지 않았을 수 있어 즉시 실패시키지 않고 짧게 폴링한다.</summary>
        private const float HandleSearchTimeoutSeconds = 5f;
        private const float HandleSearchIntervalSeconds = 0.1f;

        private IntPtr hwnd;
        private bool isDragging;
        private Win32Interop.POINT dragLastCursor;

        private Win32Interop.RECT controlDockScreenRect;
        private readonly Vector3[] controlDockWorldCorners = new Vector3[4];

        /// <summary>지금 실제로 적용되어 있는 WS_EX_TRANSPARENT 상태 캐시. 값이 바뀔 때만 SetWindowLong을 호출한다.</summary>
        private bool clickThroughApplied;

        /// <summary>Overlay가 지금 표시된 모니터. IntPtr.Zero면 "아직 확인 안 됨".</summary>
        private IntPtr lastMonitor = IntPtr.Zero;

        /// <summary>lastMonitor의 저장용 식별자(MONITORINFOEX.szDevice, 예: "\\.\DISPLAY1"). HMONITOR
        /// 핸들은 세션마다 바뀔 수 있어 재시작 후 복원에는 이 이름을 쓴다.</summary>
        private string lastMonitorDeviceName = string.Empty;

        /// <summary>lastMonitor의 마지막으로 확인한 Work Area. 값이 바뀌면(해상도/작업 표시줄 변경)
        /// CheckForMonitorWorkAreaChange가 창을 다시 맞춘다.</summary>
        private Win32Interop.RECT lastWorkArea;

        // Layout Mode에 등록된 모든 그룹의 네이티브 화면 좌표 캐시. Update()에서 메인 스레드가 매
        // 프레임 갱신하고, GlobalMouseWheelForwarder의 후크 스레드는 이 값만 읽는다 -
        // Screen.height/RectTransformUtility 등 Unity API는 메인 스레드에서만 안전하게 호출할 수
        // 있어서, 후크 스레드가 부를 수 있는 IsScreenPointClickThrough 경로에서는 절대 라이브로
        // 재계산하지 않고 이 캐시만 참조한다. 배열 크기는 LayoutModeController.AllGroups.Count로
        // 한 번만 정해지고(Awake 이후 그룹 수가 바뀌지 않음) 그 뒤로는 원소 값만 갱신한다 - 후크
        // 스레드가 아주 드물게 한 프레임 오래된 값을 읽을 수 있지만 스크롤/클릭 이벤트 하나의 관통
        // 여부에만 영향을 주므로 충분히 안전하다.
        private Win32Interop.RECT[] cachedGroupScreenRects;
        private bool[] hasCachedGroupScreenRect;

        // Stage는 Canvas UI가 아니라서 TryStartStageDragInLayoutMode가 폴링으로 직접 드래그 시작을
        // 판정해야 한다 - 그 판정 전용으로 별도 캐싱한다(배열 인덱스에 의존하지 않기 위함).
        private Win32Interop.RECT cachedStageScreenRect;
        private bool hasCachedStageScreenRect;

        /// <summary>
        /// 프로세스가 실제로 Per-Monitor DPI 인식 상태인지(매니페스트로 고정됐든 API 호출로
        /// 설정됐든, "지금 실제로 적용된" 상태). InitializeWindow에서 한 번만 확인해 로그로 남긴다 -
        /// 크기 계산에는 더 이상 쓰이지 않는다(창 크기는 항상 Work Area 픽셀 크기 그대로).
        /// </summary>
        private bool isPerMonitorDpiAware;
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
                LayoutModeController.Instance?.ToggleLayoutMode();
            }

#if UNITY_STANDALONE_WIN
            if (hwnd == IntPtr.Zero) return;

            RecomputeControlDockScreenRect();
            RecomputeLayoutGroupScreenRects();
            UpdateClickThroughState();

            bool isLayoutMode = LayoutModeController.Instance != null && LayoutModeController.Instance.IsLayoutMode;
            if (isLayoutMode && !isDragging)
            {
                TryStartStageDragInLayoutMode();
            }

            if (isDragging)
            {
                ContinueOrEndDrag();
            }
            else
            {
                // 드래그 중에는 모니터/Work Area 보정을 건드리지 않는다 - 드래그 종료 시점에 저장만 한다.
                CheckForMonitorWorkAreaChange();
            }
#endif
        }

#if UNITY_STANDALONE_WIN
        private void OnApplicationQuit()
        {
            if (hwnd != IntPtr.Zero)
            {
                SaveOverlayPlacement();
            }
        }
#endif

        /// <summary>
        /// LayoutModeController.BeginGroupDrag가 호출하는 진입점(UiGroupDraggable.OnPointerDown 또는
        /// TryStartStageDragInLayoutMode에서 시작됨). 이 메서드 자체는 #if 밖에 있어야 컴파일이 깨지지
        /// 않는다 - 실제 Win32 동작만 내부에서 플랫폼 가드로 감싼다. 네이티브 창은 움직이지 않고,
        /// ContinueOrEndDrag가 매 프레임 측정한 커서 델타를 LayoutModeController를 통해 지금 활성화된
        /// 그룹에만 전달한다.
        /// </summary>
        public void BeginManualDrag()
        {
#if UNITY_STANDALONE_WIN
            if (hwnd == IntPtr.Zero || isDragging) return;

            isDragging = true;
            Win32Interop.GetCursorPos(out dragLastCursor);
#endif
        }

        /// <summary>
        /// ControlDock에 붙일 수 있는 public 진입점 - 현재 모니터 다음 모니터로 Overlay를 옮긴다.
        /// 모니터가 하나뿐이면 아무 일도 하지 않는다. 이동 후에는 Stage/HUD/Dock 세 그룹 모두 기본
        /// 배치로 되돌린다 - 이전 모니터 기준 배치를 새 모니터에 그대로 적용하면 해상도 차이에 따라
        /// 화면 밖으로 나갈 수 있어서다. 이번 작업 범위에서는 별도 UI를 만들지 않지만, 나중에 버튼이나
        /// 모니터 선택 UI를 이 메서드에 연결하면 된다.
        /// </summary>
        public void MoveOverlayToNextMonitor()
        {
#if UNITY_STANDALONE_WIN
            if (hwnd == IntPtr.Zero) return;

            List<(IntPtr handle, string device, Win32Interop.RECT work)> monitors = EnumMonitorsOrdered();
            if (monitors.Count <= 1)
            {
                Debug.Log("[TransparentWindowController] 연결된 모니터가 하나뿐이라 이동할 곳이 없습니다.");
                return;
            }

            int currentIndex = monitors.FindIndex(m => m.handle == lastMonitor);
            int nextIndex = currentIndex >= 0 ? (currentIndex + 1) % monitors.Count : 0;
            (IntPtr handle, string device, Win32Interop.RECT work) target = monitors[nextIndex];

            ApplyOverlayToMonitor(target.handle, target.device);
            ResetAllGroupsToDefaultPlacement();
            SaveOverlayPlacement();

            Debug.Log($"[TransparentWindowController] Overlay를 다음 모니터로 이동했습니다. (device: {target.device})");
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

            isPerMonitorDpiAware = CheckIsPerMonitorDpiAware();
            Debug.Log($"[TransparentWindowController][DPI] 프로세스 Per-Monitor DPI 인식 상태: " +
                $"{(isPerMonitorDpiAware ? "적용됨" : "미적용(모니터 좌표/Work Area가 부정확할 수 있음)")}");

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
        /// 매 프레임 커서 위치만 보고 클릭 관통 여부를 재평가한다. 상태가 실제로 바뀔 때만
        /// ApplyClickThrough(SetWindowLong 호출)를 실행해서 불필요한 시스템 콜을 피한다.
        /// </summary>
        private void UpdateClickThroughState()
        {
            Win32Interop.GetCursorPos(out Win32Interop.POINT cursor);
            bool shouldPassThrough = IsScreenPointClickThrough(cursor.X, cursor.Y);

            if (shouldPassThrough == clickThroughApplied) return;

            LogLayoutHitTestTransition(cursor, shouldPassThrough);
            ApplyClickThrough(shouldPassThrough);
        }

        /// <summary>
        /// 클릭 관통 상태가 실제로 바뀌는 순간(=커스텀 WndProc이 있었다면 WM_NCHITTEST 결과가 바뀌는
        /// 순간과 동등한 시점)마다 진단 로그를 남긴다. 매 프레임 로그를 남기면 스팸이 되므로 상태
        /// 전환 시점에만 남긴다 - "마우스가 그 영역에 들어오거나 나갈 때"로 클릭 자체보다 더 이른
        /// 시점에 잡히지만, 어차피 클릭 전에 커서가 그 영역에 먼저 들어와 있어야 하므로 진단 목적에는
        /// 동일하게 유효하다.
        /// </summary>
        private void LogLayoutHitTestTransition(Win32Interop.POINT cursor, bool shouldPassThrough)
        {
            if (!logLayoutHitTests) return;

            bool isLayoutMode = LayoutModeController.Instance != null && LayoutModeController.Instance.IsLayoutMode;
            bool anyGroupHit = IsPointInsideAnyLayoutGroup(cursor.X, cursor.Y);

            Debug.Log($"[LayoutHitTest] mode={(isLayoutMode ? "On" : "Off")} screenPoint=({cursor.X},{cursor.Y}) " +
                $"anyGroupHit={anyGroupHit} result={(shouldPassThrough ? "HTTRANSPARENT" : "HTCLIENT")}");

            if (LayoutModeController.Instance == null) return;

            // 그룹 이름을 나열하지 않는다 - 등록된 그룹 전체를 그대로 순회해서 새 그룹이 추가돼도
            // 이 로그가 자동으로 포함한다.
            foreach (ILayoutDraggable group in LayoutModeController.Instance.AllGroups)
            {
                Debug.Log($"[LayoutHitTest] {group.GroupId} {group.GetDebugState()}");
            }
        }

        /// <summary>
        /// 주어진 네이티브 화면 좌표가 지금 클릭 관통 영역인지 반환한다. GlobalMouseWheelForwarder가
        /// 후크 스레드에서도 호출한다 - controlDockScreenRect/캐시 배열은 메인 스레드가 매 프레임
        /// 갱신하는 값이라 아주 드물게 한 프레임 정도 오래된 값을 읽을 수 있지만, 결과가 스크롤/클릭
        /// 이벤트 하나의 관통 여부에만 영향을 주므로 별도 동기화 없이 충분히 안전하다.
        ///
        /// 일반 모드: ControlDock의 실제 버튼 영역(dock_btn) 밖이면 관통.
        /// Layout Mode: 등록된 그룹 중 어느 영역에도 들어있지 않으면 관통 - ControlDock의 일반 클릭
        /// 판정은 Layout Mode 중에는 쓰지 않는다(그룹 드래그가 우선이라 UiGroupDraggable의 드래그
        /// 캐처가 그 영역을 대신 담당한다).
        /// </summary>
        public bool IsScreenPointClickThrough(int x, int y)
        {
            if (LayoutModeController.Instance != null && LayoutModeController.Instance.IsLayoutMode)
            {
                return !IsPointInsideAnyLayoutGroup(x, y);
            }

            bool insideDock = x >= controlDockScreenRect.Left && x <= controlDockScreenRect.Right &&
                               y >= controlDockScreenRect.Top && y <= controlDockScreenRect.Bottom;
            return !insideDock;
        }

        /// <summary>캐싱된 그룹들의 네이티브 화면 좌표 중 어느 하나에라도 이 점이 포함되는지 확인한다.
        /// 후크 스레드에서도 호출되므로 Unity API를 절대 부르지 않는다 - 순수 배열/필드 비교뿐이다.</summary>
        private bool IsPointInsideAnyLayoutGroup(int nativeX, int nativeY)
        {
            if (cachedGroupScreenRects == null) return false;

            for (int i = 0; i < cachedGroupScreenRects.Length; i++)
            {
                if (IsPointInCachedRect(nativeX, nativeY, hasCachedGroupScreenRect[i], cachedGroupScreenRects[i])) return true;
            }

            return false;
        }

        private static bool IsPointInCachedRect(int x, int y, bool hasRect, Win32Interop.RECT rect)
        {
            return hasRect && x >= rect.Left && x <= rect.Right && y >= rect.Top && y <= rect.Bottom;
        }

        /// <summary>
        /// 등록된 모든 그룹의 화면 영역을 메인 스레드에서 매 프레임 다시 계산해 캐시에 남긴다(그룹
        /// 이름을 나열하지 않음 - LayoutModeController.AllGroups를 그대로 순회). Layout Mode가 꺼져
        /// 있어도 항상 갱신한다 - RecomputeControlDockScreenRect와 같은 방식(단순 매 프레임 재계산,
        /// 별도 dirty 플래그 없음)으로 켜지는 순간 바로 최신 값을 쓸 수 있게 한다. Stage는
        /// TryStartStageDragInLayoutMode 전용으로 별도 필드에도 함께 캐싱한다.
        /// </summary>
        private void RecomputeLayoutGroupScreenRects()
        {
            if (LayoutModeController.Instance == null) return;

            IReadOnlyList<ILayoutDraggable> groups = LayoutModeController.Instance.AllGroups;
            EnsureGroupScreenRectCacheSize(groups.Count);

            for (int i = 0; i < groups.Count; i++)
            {
                bool hasRect = groups[i].TryGetUnityScreenRect(out Rect unityRect);
                Win32Interop.RECT nativeRect = hasRect ? ToNativeScreenRect(unityRect) : default;

                cachedGroupScreenRects[i] = nativeRect;
                hasCachedGroupScreenRect[i] = hasRect;

                if (groups[i].GroupId == LayoutModeController.StageGroupId)
                {
                    cachedStageScreenRect = nativeRect;
                    hasCachedStageScreenRect = hasRect;
                }
            }
        }

        /// <summary>그룹 개수는 LayoutModeController.Awake() 이후 바뀌지 않으므로, 캐시 배열은
        /// 최초 한 번만 그 크기로 만들고 이후에는 원소 값만 덮어쓴다(매 프레임 재할당 없음).</summary>
        private void EnsureGroupScreenRectCacheSize(int count)
        {
            if (cachedGroupScreenRects != null && cachedGroupScreenRects.Length == count) return;

            cachedGroupScreenRects = new Win32Interop.RECT[count];
            hasCachedGroupScreenRect = new bool[count];
        }

        /// <summary>Unity 스크린 좌표(좌하단 원점) 사각형을 네이티브 화면 픽셀 좌표(좌상단 원점,
        /// Win32 RECT 규약)로 변환한다. RecomputeControlDockScreenRect와 IsPointInsideAnyLayoutGroup이
        /// 공유하는 단일 변환 공식이다.</summary>
        private Win32Interop.RECT ToNativeScreenRect(Rect unityScreenRect)
        {
            var clientOrigin = new Win32Interop.POINT { X = 0, Y = 0 };
            Win32Interop.ClientToScreen(hwnd, ref clientOrigin);
            int screenHeight = Screen.height;

            return new Win32Interop.RECT
            {
                Left = clientOrigin.X + Mathf.RoundToInt(unityScreenRect.xMin),
                Right = clientOrigin.X + Mathf.RoundToInt(unityScreenRect.xMax),
                Top = clientOrigin.Y + (screenHeight - Mathf.RoundToInt(unityScreenRect.yMax)),
                Bottom = clientOrigin.Y + (screenHeight - Mathf.RoundToInt(unityScreenRect.yMin)),
            };
        }

        /// <summary>GlobalMouseWheelForwarder가 "이 창이 우리 자신인지" 비교할 때 쓴다.</summary>
        public IntPtr NativeWindowHandle => hwnd;

        /// <summary>
        /// 시작 시 모니터 선택 규칙(우선순위 순):
        /// 1. 저장된 monitorDeviceName과 이름이 일치하는, 지금 연결된 모니터
        /// 2. (레거시 마이그레이션) 저장된 소형 창 좌표가 있으면, 그 좌표에서 가장 가까운 모니터 -
        ///    좌표 자체는 새 구조에 맞지 않으므로 위치가 아니라 "어느 모니터였는지"만 참고한다.
        /// 3. 커서가 있는 모니터
        /// 어느 경우든 그 모니터의 Work Area 전체로 창을 맞추고, LayoutModeController에 등록된 모든
        /// 그룹 각각에 대해 저장된 배치가 있으면 복원하고 없으면 그 그룹의 기본 배치를 적용한다.
        /// </summary>
        private void ApplyStartupPlacement()
        {
            WindowPlacementData saved = WindowPlacementSaveSystem.Load();

            IntPtr monitor = IntPtr.Zero;
            string deviceName = null;

            if (saved != null && saved.hasMonitorSelection && !string.IsNullOrEmpty(saved.monitorDeviceName))
            {
                monitor = FindMonitorByDeviceName(saved.monitorDeviceName);
                if (monitor != IntPtr.Zero) deviceName = saved.monitorDeviceName;
            }

            if (monitor == IntPtr.Zero && saved != null && saved.hasSavedPosition)
            {
                var point = new Win32Interop.POINT { X = saved.positionX, Y = saved.positionY };
                monitor = Win32Interop.MonitorFromPoint(point, Win32Interop.MONITOR_DEFAULTTONEAREST);
            }

            if (monitor == IntPtr.Zero)
            {
                monitor = GetCursorMonitorOrPrimary();
            }

            if (deviceName == null)
            {
                deviceName = GetMonitorDeviceName(monitor);
            }

            ApplyOverlayToMonitor(monitor, deviceName);
            RestoreAllGroupPlacements(saved);
        }

        /// <summary>
        /// LayoutModeController에 등록된 모든 그룹(그룹 이름을 나열하지 않음 - 새 그룹이 추가돼도
        /// 이 메서드는 그대로 동작한다)을 순회하며 우선순위대로 배치를 복원한다:
        /// 1. 새 groupPlacements 목록에 그 groupId의 저장값이 있으면 그대로 적용
        /// 2. (일회성 마이그레이션) 예전 Stage/HUD/Dock 전용 필드에 값이 있으면 그걸로 적용 - HUD는
        ///    옛날에 Combo/Progress/KillCount가 하나로 합쳐진 그룹이었으므로 세 그룹 모두 같은 값을
        ///    초기값으로 받는다.
        /// 3. 둘 다 없으면(첫 실행이거나 새로 추가된 그룹) 그 그룹의 기본 배치(씬 authoring 위치)를 쓴다.
        /// </summary>
        private static void RestoreAllGroupPlacements(WindowPlacementData saved)
        {
            if (LayoutModeController.Instance == null) return;

            foreach (ILayoutDraggable group in LayoutModeController.Instance.AllGroups)
            {
                if (TryFindSavedGroupPlacement(saved, group.GroupId, out float x, out float y))
                {
                    Debug.Log($"[LayoutLoad] group={group.GroupId} found=true source=groupPlacements pos=({x:F4},{y:F4})");
                    group.SetPlacement(x, y);
                }
                else if (TryMigrateLegacyGroupPlacement(saved, group.GroupId, out float legacyX, out float legacyY))
                {
                    Debug.Log($"[LayoutLoad] group={group.GroupId} found=true source=legacyMigration pos=({legacyX:F4},{legacyY:F4})");
                    group.SetPlacement(legacyX, legacyY);
                }
                else
                {
                    Debug.Log($"[LayoutLoad] group={group.GroupId} found=false source=sceneDefault");
                    group.ResetToDefaultPlacement();
                }
            }
        }

        private static bool TryFindSavedGroupPlacement(WindowPlacementData saved, string groupId, out float x, out float y)
        {
            if (saved?.groupPlacements != null)
            {
                foreach (LayoutGroupPlacement entry in saved.groupPlacements)
                {
                    if (entry.groupId == groupId)
                    {
                        x = entry.normalizedPositionX;
                        y = entry.normalizedPositionY;
                        return true;
                    }
                }
            }

            x = 0f;
            y = 0f;
            return false;
        }

        private static bool TryMigrateLegacyGroupPlacement(WindowPlacementData saved, string groupId, out float x, out float y)
        {
            x = 0f;
            y = 0f;
            if (saved == null) return false;

            if (groupId == LayoutModeController.StageGroupId && saved.hasStagePlacement)
            {
                x = saved.stageRightMarginFraction;
                y = saved.stageBottomMarginFraction;
                return true;
            }

            if (groupId == LayoutModeController.DockGroupId && saved.hasDockPlacement)
            {
                x = saved.dockRightMarginFraction;
                y = saved.dockBottomMarginFraction;
                return true;
            }

            bool isMigratedHudGroup = groupId == LayoutModeController.ComboGroupId ||
                                      groupId == LayoutModeController.ProgressGroupId ||
                                      groupId == LayoutModeController.KillCountGroupId;
            if (isMigratedHudGroup && saved.hasHudPlacement)
            {
                x = saved.hudRightMarginFraction;
                y = saved.hudBottomMarginFraction;
                return true;
            }

            return false;
        }

        private static void ResetAllGroupsToDefaultPlacement()
        {
            if (LayoutModeController.Instance == null) return;

            foreach (ILayoutDraggable group in LayoutModeController.Instance.AllGroups)
            {
                group.ResetToDefaultPlacement();
            }
        }

        /// <summary>
        /// 네이티브 창을 지정한 모니터의 Work Area 전체로 맞춘다(위치=work.Left/Top, 크기=work의
        /// 가로/세로) - 사용자 배율이나 DPI를 곱하지 않는다. LayoutModeController를 통해 Stage/HUD/Dock
        /// 세 그룹 모두에게 새 Work Area 픽셀 크기를 알려줘서 각자의 배치 환산 기준이 같이 갱신되게 한다.
        /// </summary>
        private void ApplyOverlayToMonitor(IntPtr monitor, string deviceName)
        {
            if (!TryGetWorkArea(monitor, out Win32Interop.RECT work))
            {
                // 모니터 정보를 전혀 못 가져오는 극단적인 경우에만 기존 방식(주 모니터 작업 영역)으로 대체한다.
                work = default;
                Win32Interop.SystemParametersInfo(Win32Interop.SPI_GETWORKAREA, 0, ref work, 0);
            }

            int width = work.Right - work.Left;
            int height = work.Bottom - work.Top;

            Win32Interop.SetWindowPos(hwnd, IntPtr.Zero, work.Left, work.Top, width, height, Win32Interop.SWP_NOZORDER);

            lastMonitor = monitor;
            lastMonitorDeviceName = deviceName ?? string.Empty;
            lastWorkArea = work;

            LayoutModeController.Instance?.NotifyWorkAreaChanged(width, height);

            uint dpi = GetMonitorDpi(monitor);
            Debug.Log($"[TransparentWindowController] Overlay 배치 적용 (device: {lastMonitorDeviceName}, " +
                $"rect: [{work.Left},{work.Top},{work.Right},{work.Bottom}], size: {width}x{height}, monitorDPI: {dpi})");
        }

        /// <summary>커서가 있는 모니터를 반환한다. 커서 위치를 못 가져와도 (0,0) 기준 NEAREST 보정으로
        /// 항상 연결된 모니터 중 하나를 반환한다(모니터가 하나라도 있는 한 IntPtr.Zero가 나오지 않음).</summary>
        private static IntPtr GetCursorMonitorOrPrimary()
        {
            if (Win32Interop.GetCursorPos(out Win32Interop.POINT cursor))
            {
                IntPtr monitor = Win32Interop.MonitorFromPoint(cursor, Win32Interop.MONITOR_DEFAULTTONEAREST);
                if (monitor != IntPtr.Zero) return monitor;
            }

            return Win32Interop.MonitorFromPoint(new Win32Interop.POINT { X = 0, Y = 0 }, Win32Interop.MONITOR_DEFAULTTONEAREST);
        }

        private static bool TryGetWorkArea(IntPtr monitor, out Win32Interop.RECT workArea)
        {
            if (monitor != IntPtr.Zero)
            {
                var info = new Win32Interop.MONITORINFO { cbSize = Marshal.SizeOf(typeof(Win32Interop.MONITORINFO)) };
                if (Win32Interop.GetMonitorInfo(monitor, ref info))
                {
                    workArea = info.rcWork; // 작업 표시줄을 제외한 작업 영역
                    return true;
                }
            }

            workArea = default;
            return false;
        }

        /// <summary>연결된 모든 모니터를 EnumDisplayMonitors로 순회해서 (핸들, 장치 이름, Work Area)
        /// 목록으로 돌려준다. OS가 정하는 순서지만 한 세션 안에서는 안정적이라 "다음 모니터" 순환에
        /// 쓸 수 있다.</summary>
        private static List<(IntPtr handle, string device, Win32Interop.RECT work)> EnumMonitorsOrdered()
        {
            var result = new List<(IntPtr, string, Win32Interop.RECT)>();

            Win32Interop.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
                (IntPtr hMonitor, IntPtr hdc, ref Win32Interop.RECT rect, IntPtr data) =>
                {
                    var infoEx = new Win32Interop.MONITORINFOEX { cbSize = Marshal.SizeOf(typeof(Win32Interop.MONITORINFOEX)) };
                    if (Win32Interop.GetMonitorInfoEx(hMonitor, ref infoEx))
                    {
                        result.Add((hMonitor, infoEx.szDevice, infoEx.rcWork));
                    }
                    return true;
                }, IntPtr.Zero);

            return result;
        }

        private static IntPtr FindMonitorByDeviceName(string deviceName)
        {
            foreach ((IntPtr handle, string device, Win32Interop.RECT _) in EnumMonitorsOrdered())
            {
                if (string.Equals(device, deviceName, StringComparison.OrdinalIgnoreCase)) return handle;
            }
            return IntPtr.Zero;
        }

        private static string GetMonitorDeviceName(IntPtr monitor)
        {
            if (monitor == IntPtr.Zero) return string.Empty;

            var infoEx = new Win32Interop.MONITORINFOEX { cbSize = Marshal.SizeOf(typeof(Win32Interop.MONITORINFOEX)) };
            return Win32Interop.GetMonitorInfoEx(monitor, ref infoEx) ? infoEx.szDevice : string.Empty;
        }

        /// <summary>
        /// 모니터의 실제 DPI를 조회한다(진단 로그 전용 - 창 크기 계산에는 쓰이지 않는다). 호출
        /// 프로세스의 DPI 인식 설정과 무관하게 모니터 원본 값을 읽는 GetDpiForMonitor(Shcore, Win8.1+)를
        /// 쓴다. API가 없는 구버전이거나 조회에 실패하면 96(100%)으로 안전하게 대체한다.
        /// </summary>
        private static uint GetMonitorDpi(IntPtr monitor)
        {
            if (monitor != IntPtr.Zero)
            {
                try
                {
                    if (Win32Interop.GetDpiForMonitor(monitor, Win32Interop.MDT_EFFECTIVE_DPI, out uint dpiX, out _) == 0 && dpiX > 0)
                    {
                        return dpiX;
                    }
                }
                catch (EntryPointNotFoundException)
                {
                }
                catch (DllNotFoundException)
                {
                }
            }

            return Win32Interop.USER_DEFAULT_SCREEN_DPI;
        }

        /// <summary>GetThreadDpiAwarenessContext + GetAwarenessFromDpiAwarenessContext로 "지금 이
        /// 순간 실제로 적용된" DPI 인식 상태를 직접 확인한다(진단 로그 전용). API 자체가 없는 구버전
        /// Windows는 Per-Monitor 인식 자체가 불가능하므로 false로 안전하게 처리한다.</summary>
        private static bool CheckIsPerMonitorDpiAware()
        {
            try
            {
                IntPtr context = Win32Interop.GetThreadDpiAwarenessContext();
                int awareness = Win32Interop.GetAwarenessFromDpiAwarenessContext(context);
                return awareness == Win32Interop.DPI_AWARENESS_PER_MONITOR_AWARE;
            }
            catch (EntryPointNotFoundException)
            {
                return false;
            }
            catch (DllNotFoundException)
            {
                return false;
            }
        }

        /// <summary>
        /// 드래그 중이 아닐 때 매 프레임 호출된다. (1) 선택된 모니터가 여전히 연결돼 있는지,
        /// (2) 연결돼 있다면 그 Work Area 사각형이 마지막으로 확인한 값과 다른지(해상도 변경, 작업
        /// 표시줄 크기 변경 등)만 확인한다 - 모니터가 사라졌으면 안전한 모니터로 대체하고, Work
        /// Area만 바뀌었으면 같은 모니터 기준으로 창을 다시 맞춘다. 창은 항상 정확히 한 모니터
        /// 전체이므로(드래그로 다른 모니터에 걸치는 경우가 없으므로) MonitorFromWindow로 "다른
        /// 모니터로 넘어갔는지"를 매 프레임 다시 판정할 필요는 없다.
        /// </summary>
        private void CheckForMonitorWorkAreaChange()
        {
            if (!TryGetWorkArea(lastMonitor, out Win32Interop.RECT work))
            {
                IntPtr fallback = GetCursorMonitorOrPrimary();
                if (fallback == IntPtr.Zero || !TryGetWorkArea(fallback, out work)) return;

                Debug.LogWarning("[TransparentWindowController] 선택된 모니터를 더 이상 사용할 수 없어 다른 모니터로 전환합니다.");
                ApplyOverlayToMonitor(fallback, GetMonitorDeviceName(fallback));
                ResetAllGroupsToDefaultPlacement();
                SaveOverlayPlacement();
                return;
            }

            if (RectEquals(work, lastWorkArea)) return;

            Debug.Log("[TransparentWindowController] 현재 모니터의 Work Area가 변경되어 Overlay 크기를 다시 맞춥니다.");
            ApplyOverlayToMonitor(lastMonitor, lastMonitorDeviceName);
            SaveOverlayPlacement();
        }

        private static bool RectEquals(Win32Interop.RECT a, Win32Interop.RECT b) =>
            a.Left == b.Left && a.Top == b.Top && a.Right == b.Right && a.Bottom == b.Bottom;

        /// <summary>
        /// Layout Mode 중 StageVisualRoot 영역 드래그 시작 판정. StageVisualRoot는 Canvas UI가 아니라
        /// Unity 이벤트(OnPointerDown)를 받을 수 없으므로, GameHUDGroup/ControlDockGroup(UiGroupDraggable)과
        /// 달리 이 클래스가 직접 "클릭이 눌린 순간 커서가 Stage 화면 영역 안에 있었는지"를 폴링으로
        /// 판정해서 드래그를 시작한다. RecomputeLayoutGroupScreenRects가 매 프레임 갱신해둔
        /// cachedStageScreenRect를 그대로 쓴다.
        /// </summary>
        private void TryStartStageDragInLayoutMode()
        {
            if (!hasCachedStageScreenRect) return;

            if (!Win32Interop.GetCursorPos(out Win32Interop.POINT cursor)) return;
            bool leftDown = (Win32Interop.GetAsyncKeyState(Win32Interop.VK_LBUTTON) & 0x8000) != 0;
            if (!leftDown || !IsPointInCachedRect(cursor.X, cursor.Y, hasCachedStageScreenRect, cachedStageScreenRect)) return;

            if (LayoutModeController.Instance == null || !LayoutModeController.Instance.TryGetGroup(LayoutModeController.StageGroupId, out ILayoutDraggable stage)) return;
            LayoutModeController.Instance.BeginGroupDrag(stage);
        }

        /// <summary>
        /// 왼쪽 마우스 버튼 상태와 커서의 절대 화면 좌표를 폴링해서 드래그를 진행/종료한다
        /// (GetAsyncKeyState + GetCursorPos) - Unity의 Input 이벤트에 의존하지 않고 전역 상태를 직접
        /// 폴링하는 이유는 클래스 상단 설명 참고. 매 프레임 커서 델타를 측정해서
        /// LayoutModeController.ApplyActiveDragDeltaPixels로 넘긴다(네이티브 창 위치는 절대 바꾸지
        /// 않는다) - Stage 폴링 시작/UiGroupDraggable의 OnPointerDown 시작 어느 쪽이든 공유한다.
        /// </summary>
        private void ContinueOrEndDrag()
        {
            if (!Win32Interop.GetCursorPos(out Win32Interop.POINT cursor)) return;
            bool leftDown = (Win32Interop.GetAsyncKeyState(Win32Interop.VK_LBUTTON) & 0x8000) != 0;

            if (leftDown)
            {
                int deltaX = cursor.X - dragLastCursor.X;
                int deltaY = cursor.Y - dragLastCursor.Y;
                dragLastCursor = cursor;

                LayoutModeController.Instance?.ApplyActiveDragDeltaPixels(deltaX, deltaY);
            }
            else
            {
                isDragging = false;
                LayoutModeController.Instance?.EndActiveDrag();
                SaveOverlayPlacement(); // 이동 완료 후 저장
            }
        }

        /// <summary>모니터 선택(장치 이름)과 LayoutModeController에 등록된 모든 그룹의 배치를 함께
        /// 저장한다. 그룹 이름을 나열하지 않고 AllGroups를 그대로 순회하므로, 새 UI 그룹을 추가해도
        /// 이 메서드는 수정할 필요가 없다(groupId와 UiGroupDraggable 등록만으로 저장까지 자동 포함됨).
        /// 레거시 positionX/Y/hasSavedPosition, stageXxx/hudXxx/dockXxx 필드는 더 이상 새로 쓰지
        /// 않는다(마이그레이션 읽기 전용) - hasMonitorSelection이 한 번 true로 저장되면 다음
        /// 로드부터는 groupPlacements 목록이 항상 우선이라 레거시 필드가 다시 읽히는 일이 없다.
        /// LayoutModeController.SetLayoutMode(false)도 이 메서드를 호출한다(Layout Mode 종료 시
        /// 한 번 더 저장하라는 요구사항).</summary>
        public void SaveOverlayPlacement()
        {
            var data = new WindowPlacementData
            {
                hasMonitorSelection = !string.IsNullOrEmpty(lastMonitorDeviceName),
                monitorDeviceName = lastMonitorDeviceName,
            };

            if (LayoutModeController.Instance != null)
            {
                foreach (ILayoutDraggable group in LayoutModeController.Instance.AllGroups)
                {
                    (float rightMarginFraction, float bottomMarginFraction) placement = group.GetPlacement();
                    Debug.Log($"[LayoutSave] group={group.GroupId} pos=({placement.rightMarginFraction:F4},{placement.bottomMarginFraction:F4})");
                    data.groupPlacements.Add(new LayoutGroupPlacement
                    {
                        groupId = group.GroupId,
                        normalizedPositionX = placement.rightMarginFraction,
                        normalizedPositionY = placement.bottomMarginFraction,
                    });
                }
            }

            WindowPlacementSaveSystem.Save(data);
        }

        /// <summary>
        /// controlDockRect(Canvas UI, Overlay 기준)의 월드 좌표 4모서리를 Unity 스크린 좌표로 바꾼 뒤
        /// ToNativeScreenRect로 네이티브 화면 좌표 사각형으로 캐싱한다. 매 프레임 다시 계산해서 Canvas
        /// 레이아웃이 바뀌어도 항상 최신 좌표를 쓴다.
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

            controlDockScreenRect = ToNativeScreenRect(new Rect(minScreen.x, minScreen.y, maxScreen.x - minScreen.x, maxScreen.y - minScreen.y));
        }
#endif
    }
}
