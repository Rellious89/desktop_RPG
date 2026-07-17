#if UNITY_STANDALONE_WIN
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace DesktopWindow
{
    internal static class Win32Interop
    {
        public const int GWL_STYLE = -16;
        public const int GWL_EXSTYLE = -20;

        public const uint WS_CAPTION = 0x00C00000;
        public const uint WS_THICKFRAME = 0x00040000;
        public const uint WS_MINIMIZEBOX = 0x00020000;
        public const uint WS_MAXIMIZEBOX = 0x00010000;
        public const uint WS_SYSMENU = 0x00080000;

        public const uint WS_EX_LAYERED = 0x00080000;
        public const uint WS_EX_TOPMOST = 0x00000008;
        public const uint WS_EX_TRANSPARENT = 0x00000020;

        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOZORDER = 0x0004;
        public const uint SWP_FRAMECHANGED = 0x0020;
        public const uint SWP_NOACTIVATE = 0x0010;

        public const uint SPI_GETWORKAREA = 0x0030;

        public const int WH_KEYBOARD_LL = 13;
        public const int WM_KEYDOWN = 0x0100;
        public const int WM_SYSKEYDOWN = 0x0104;

        public const int VK_LBUTTON = 0x01;

        public const uint MONITOR_DEFAULTTONULL = 0x00000000;
        public const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

        public const uint WM_QUIT = 0x0012;

        public const int WH_MOUSE_LL = 14;
        public const int WM_MOUSEWHEEL = 0x020A;
        public const int WM_MOUSEHWHEEL = 0x020E;

        /// <summary>GetWindow의 uCmd 인자 - Z-order상 바로 다음(뒤) 창.</summary>
        public const uint GW_HWNDNEXT = 2;

        /// <summary>모니터/DPI 조회 API 기준값. 사용자가 Windows 디스플레이 설정에서 100% 배율일 때의 DPI.</summary>
        public const uint USER_DEFAULT_SCREEN_DPI = 96;

        /// <summary>GetDpiForMonitor의 dpiType 인자 - 실제 픽셀 밀도(앱 스케일링 계산에 쓰는 값).</summary>
        public const uint MDT_EFFECTIVE_DPI = 0;

        /// <summary>SetProcessDpiAwareness(Shcore)의 값 - 모니터별(Per-Monitor V1) DPI 인식.</summary>
        public const int PROCESS_PER_MONITOR_DPI_AWARE = 2;

        /// <summary>SetProcessDpiAwarenessContext(User32, Win10 1703+)에 넘기는 상수 - Per-Monitor V2.
        /// 실제 IntPtr 값이 아니라 부호 있는 상수를 특수 sentinel로 캐스팅한 값(문서화된 매크로).</summary>
        public static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = new IntPtr(-4);

        /// <summary>GetAwarenessFromDpiAwarenessContext가 반환하는 DPI_AWARENESS 열거값.
        /// PER_MONITOR_AWARE(V1/V2 공통 분류값)일 때만 "우리가 직접 모니터별 물리 픽셀 크기를
        /// 책임진다"고 신뢰할 수 있다 - UNAWARE/SYSTEM_AWARE면 OS가 자체적으로 창을 비트맵
        /// 확대/축소하므로, 그 위에 우리가 또 DPI 배율을 곱하면 이중 적용된다.</summary>
        public const int DPI_AWARENESS_PER_MONITOR_AWARE = 2;

        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);

        public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        /// <summary>EnumWindows 콜백. true를 반환하면 다음 창으로 계속 진행하고, false를 반환하면 즉시 순회를 멈춘다.</summary>
        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        public struct MARGINS
        {
            public int cxLeftWidth;
            public int cxRightWidth;
            public int cyTopHeight;
            public int cyBottomHeight;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        /// <summary>MONITORINFO에 장치 이름(예: "\\.\DISPLAY1")이 추가된 버전. 모니터 선택을 재시작
        /// 후에도 복원하려면 HMONITOR 핸들(세션마다 바뀔 수 있음) 대신 이 이름으로 저장해야 한다.</summary>
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct MONITORINFOEX
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szDevice;
        }

        /// <summary>EnumDisplayMonitors 콜백. true를 반환하면 다음 모니터로 계속 진행한다.</summary>
        public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

        /// <summary>GetMessage가 채우는 스레드 메시지 큐 항목. 저수준 훅 전용 스레드의 메시지 루프에서 쓴다.</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public POINT pt;
        }

        /// <summary>
        /// WH_KEYBOARD_LL 콜백의 lParam이 가리키는 구조체 레이아웃 문서화용. vkCode(첫 필드, DWORD)만
        /// 필요하면 GlobalKeyboardHook에서 이 구조체로 마샬링하지 않고 Marshal.ReadInt32(lParam)로
        /// 직접 읽는다 - 매 키 입력마다 박싱 할당이 생기는 걸 피하기 위함(전역 후크라 이 앱과 무관한
        /// 입력에도 호출됨).
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public UIntPtr dwExtraInfo;
        }

        /// <summary>
        /// WH_MOUSE_LL 콜백의 lParam이 가리키는 구조체. mouseData의 상위 16비트에 휠 회전량(부호 있는
        /// WHEEL_DELTA 배수)이 들어있다 - GlobalMouseWheelForwarder에서 원본 wParam/lParam을 그대로
        /// 다른 창에 전달할 때는 이 값을 다시 조립할 필요 없이 원래 메시지의 wParam을 그대로 재사용한다.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public UIntPtr dwExtraInfo;
        }

        /// <summary>
        /// 최상위(top-level) 창만 순회한다(EnumWindows 자체가 자식 창은 건너뛴다). 콜백이 false를
        /// 반환하면 순회를 즉시 중단한다.
        /// </summary>
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentProcessId();

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int SetWindowLong(IntPtr hWnd, int nIndex, uint dwNewLong);

        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        public static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref RECT pvParam, uint fWinIni);

        [DllImport("dwmapi.dll")]
        public static extern int DwmExtendFrameIntoClientArea(IntPtr hWnd, ref MARGINS pMarInset);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        public static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

        /// <summary>창 자체가 지금 걸쳐 있는 모니터를 판정한다(마우스 커서 위치와 무관) - 창이 여러
        /// 모니터에 걸쳐 있으면 면적이 가장 넓게 겹치는 모니터를 반환한다.</summary>
        [DllImport("user32.dll")]
        public static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        /// <summary>GetMonitorInfo와 같은 네이티브 함수(GetMonitorInfoW)를 MONITORINFOEX 구조체로
        /// 받는 오버로드 - szDevice(장치 이름)까지 필요할 때 쓴다. EntryPoint를 명시해야 위의
        /// GetMonitorInfo(MONITORINFO 버전)와 이름이 겹쳐도 서로 다른 시그니처로 공존할 수 있다.</summary>
        [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode)]
        public static extern bool GetMonitorInfoEx(IntPtr hMonitor, ref MONITORINFOEX lpmi);

        /// <summary>연결된 모든 모니터를 순회한다. lprcClip=IntPtr.Zero면 전체 가상 데스크톱을 대상으로 한다.</summary>
        [DllImport("user32.dll")]
        public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

        [DllImport("user32.dll")]
        public static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

        /// <summary>지정한 화면 좌표를 덮고 있는 최상위 창을 반환한다(Z-order 최상단, 확장 스타일과
        /// 무관하게 기하학적으로만 판정 - WS_EX_TRANSPARENT여도 그대로 반환됨에 주의). 클릭 관통
        /// 영역에서 "우리 창 자신"이 반환되는 게 정상이므로, 그 아래의 진짜 대상 창을 찾으려면
        /// GetWindow(hwnd, GW_HWNDNEXT)로 Z-order를 한 단계씩 내려가며 별도로 판정해야 한다.</summary>
        [DllImport("user32.dll")]
        public static extern IntPtr WindowFromPoint(POINT p);

        /// <summary>Z-order상 인접한 창을 가져온다(GW_HWNDNEXT = 바로 뒤).</summary>
        [DllImport("user32.dll")]
        public static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        /// <summary>대상 창의 메시지 큐에 메시지를 넣고 즉시 반환한다(SendMessage와 달리 대상 창의
        /// 처리를 기다리지 않음) - 훅 콜백처럼 빠르게 반환해야 하는 핫 패스에서 반드시 이 쪽을 쓴다.</summary>
        [DllImport("user32.dll")]
        public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        /// <summary>
        /// Windows 10 1703(Creators Update)+ 전용. 프로세스 전체의 DPI 인식을 런타임에 한 번 설정한다
        /// (호출 시점이 창 생성보다 늦으면 실패/무시될 수 있음 - 가능한 한 이르게 호출해야 함).
        /// 이미 매니페스트 등으로 DPI 인식이 고정된 경우 false를 반환한다. 이 API 자체가 없는 구버전
        /// Windows에서는 EntryPointNotFoundException이 발생하므로 호출부에서 반드시 try/catch로 감싼다.
        /// </summary>
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetProcessDpiAwarenessContext(IntPtr value);

        /// <summary>Windows 8.1~10(1703 미만) 대비 폴백. Shcore.dll이 없는 구버전에서는
        /// DllNotFoundException이 발생하므로 호출부에서 반드시 try/catch로 감싼다.</summary>
        [DllImport("shcore.dll")]
        public static extern int SetProcessDpiAwareness(int value);

        /// <summary>Windows Vista~8 대비 최후 폴백(System DPI Aware, 모니터별 대응은 안 됨).</summary>
        [DllImport("user32.dll")]
        public static extern bool SetProcessDPIAware();

        /// <summary>
        /// 지정한 모니터의 실제 DPI를 반환한다(호출 프로세스의 DPI 인식 설정과 무관하게 모니터의 원본
        /// 값을 읽는 저수준 API). Windows 8.1+ 필요 - 없는 구버전에서는 DllNotFoundException이
        /// 발생하므로 호출부에서 반드시 try/catch로 감싼다. 성공 시 0(S_OK)을 반환한다.
        /// </summary>
        [DllImport("shcore.dll")]
        public static extern int GetDpiForMonitor(IntPtr hMonitor, uint dpiType, out uint dpiX, out uint dpiY);

        /// <summary>
        /// 현재 스레드에 실제로 적용된 DPI_AWARENESS_CONTEXT를 반환한다(매니페스트로 고정됐든,
        /// API 호출로 설정됐든 관계없이 "지금 실제 상태"). Windows 10 1607+ 필요 - 없는 구버전에서는
        /// EntryPointNotFoundException이 발생하므로 호출부에서 반드시 try/catch로 감싼다.
        /// </summary>
        [DllImport("user32.dll")]
        public static extern IntPtr GetThreadDpiAwarenessContext();

        /// <summary>
        /// DPI_AWARENESS_CONTEXT를 DPI_AWARENESS 열거값(UNAWARE=0/SYSTEM_AWARE=1/PER_MONITOR_AWARE=2)으로
        /// 변환한다. Windows 10 1607+ 필요 - 없는 구버전에서는 EntryPointNotFoundException이 발생하므로
        /// 호출부에서 반드시 try/catch로 감싼다.
        /// </summary>
        [DllImport("user32.dll")]
        public static extern int GetAwarenessFromDpiAwarenessContext(IntPtr dpiContext);

        /// <summary>
        /// 저수준 훅 전용 스레드의 메시지 루프. WH_KEYBOARD_LL 콜백은 훅을 설치한 스레드가 이 함수로
        /// 메시지 큐를 계속 펌핑해야 실제로 디스패치된다 - 그래서 GlobalKeyboardHook은 이 함수를
        /// Unity 메인 스레드가 아닌 전용 백그라운드 스레드에서 블로킹 호출한다.
        /// </summary>
        [DllImport("user32.dll")]
        public static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

        /// <summary>훅 스레드의 메시지 큐에 WM_QUIT을 넣어 GetMessage 루프를 빠져나오게 한다(스레드 종료 신호).</summary>
        [DllImport("user32.dll")]
        public static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();
    }
}
#endif
