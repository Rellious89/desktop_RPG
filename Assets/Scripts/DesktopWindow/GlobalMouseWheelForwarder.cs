using System;
using UnityEngine;

#if UNITY_STANDALONE_WIN
using System.Runtime.InteropServices;
using System.Threading;
#endif

namespace DesktopWindow
{
    /// <summary>
    /// 전체 모니터를 덮는 투명 Overlay 위에서도 뒤에 있는 다른 프로그램(브라우저, 문서 뷰어 등)의
    /// 마우스 휠 스크롤이 정상 동작하게 한다.
    ///
    /// WS_EX_TRANSPARENT는 클릭류 메시지(WM_LBUTTONDOWN 등)는 OS가 알아서 뒤 창으로 넘겨주지만,
    /// WM_MOUSEWHEEL/WM_MOUSEHWHEEL은 그 경로를 타지 않는다 - 기본적으로 포그라운드/활성 창으로
    /// 전달되며, Unity 플레이어가 자체적으로 이 메시지를 "처리됨"으로 소비해버리면(Input.mouseScrollDelta
    /// 갱신을 위해) OS의 "처리 안 된 휠 메시지는 커서 아래 창으로 넘긴다" 폴백도 기대할 수 없다.
    /// 그래서 클릭 관통과 별개로, 휠 이벤트만 직접 가로채 진짜 대상 창에 다시 전달해야 한다.
    ///
    /// GlobalKeyboardHook과 같은 이유로 전용 백그라운드 스레드에서 WH_MOUSE_LL을 설치하고 펌핑한다 -
    /// 훅 콜백은 설치한 스레드가 계속 GetMessage로 메시지 큐를 펌핑해야 디스패치되고, Unity 메인
    /// 스레드에서 설치하면 FpsLimiter의 30fps 주기에 응답이 묶여 시스템 전체 마우스 입력이 끊기는
    /// 문제가 재발한다(GlobalKeyboardHook 클래스 상단 설명 참고). 콜백 자체는 Unity API를 호출하지
    /// 않고 TransparentWindowController의 순수 필드 읽기 메서드(IsScreenPointClickThrough)만 참조한다.
    ///
    /// 처리 흐름: 휠 메시지의 화면 좌표가 우리 창의 클릭 관통 영역(ControlDock 밖) 안이면, Z-order를
    /// 따라 우리 창 바로 아래에 있는 진짜 대상 창을 찾아 PostMessage로 같은 메시지를 다시 보내고,
    /// 원본은 훅에서 반환값 1로 삼켜서 우리 창으로도, 시스템의 다른 기본 처리로도 흘러가지 않게 한다
    /// (그대로 두면 우리 창과 대상 창 양쪽에 이벤트가 중복 전달되는 문제가 생긴다).
    ///
    /// 알려진 한계: UIPI(User Interface Privilege Isolation) 때문에 대상 창이 우리보다 높은 권한으로
    /// 실행 중이면(관리자 권한 프로그램 등) PostMessage가 조용히 실패한다 - OS 보안 정책이라 이 앱
    /// 쪽에서 우회할 수 없다.
    /// </summary>
    [DisallowMultipleComponent]
    public class GlobalMouseWheelForwarder : MonoBehaviour
    {
        [Tooltip("끄면 훅을 설치하지 않는다(진단용) - 클릭 관통 영역에서 휠 스크롤이 뒤 프로그램에 전달되지 않게 된다.")]
        [SerializeField] private bool useWheelForwarding = true;

#if UNITY_STANDALONE_WIN
        private Thread hookThread;
        private ManualResetEventSlim hookReadyEvent;
        private volatile bool hookInstallFailed;
        private volatile uint hookThreadId;
        private Win32Interop.LowLevelKeyboardProc hookProc; // WH_MOUSE_LL도 같은 델리게이트 시그니처를 쓴다.
#endif

        private void OnEnable()
        {
#if UNITY_EDITOR
            Debug.LogWarning("[GlobalMouseWheelForwarder] 휠 전달 훅은 빌드된 Windows 실행 파일(.exe)에서만 동작합니다.");
#elif UNITY_STANDALONE_WIN
            if (!useWheelForwarding)
            {
                Debug.LogWarning("[GlobalMouseWheelForwarder] useWheelForwarding이 꺼져 있어 훅을 설치하지 않습니다(진단 모드).");
                return;
            }

            hookReadyEvent = new ManualResetEventSlim(false);
            hookThread = new Thread(HookThreadMain) { IsBackground = true, Name = "GlobalMouseWheelHookThread" };
            hookThread.Start();

            if (!hookReadyEvent.Wait(2000))
            {
                Debug.LogError("[GlobalMouseWheelForwarder] 후크 스레드가 시간 안에 시작되지 않았습니다.");
            }
            else if (hookInstallFailed)
            {
                Debug.LogError("[GlobalMouseWheelForwarder] 마우스 훅 설치 실패. 클릭 관통 영역에서 휠 스크롤이 뒤 프로그램에 전달되지 않습니다.");
            }
#else
            Debug.LogWarning("[GlobalMouseWheelForwarder] 이 기능은 Win32 API 기반이라 Windows 빌드에서만 지원됩니다.");
#endif
        }

        private void OnDisable()
        {
#if UNITY_STANDALONE_WIN
            if (hookThread == null) return;

            if (hookThreadId != 0)
            {
                Win32Interop.PostThreadMessage(hookThreadId, Win32Interop.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
            }

            hookThread.Join(2000);
            hookThread = null;

            hookReadyEvent?.Dispose();
            hookReadyEvent = null;
            hookThreadId = 0;
#endif
        }

#if UNITY_STANDALONE_WIN
        private void HookThreadMain()
        {
            try
            {
                hookProc = HookCallback; // 델리게이트를 필드에 rooted 유지 - 지역 변수면 GC가 수거해 콜백이 끊길 수 있다.

                IntPtr moduleHandle = Win32Interop.GetModuleHandle(null);
                IntPtr hookHandle = Win32Interop.SetWindowsHookEx(Win32Interop.WH_MOUSE_LL, hookProc, moduleHandle, 0);

                hookInstallFailed = hookHandle == IntPtr.Zero;
                hookThreadId = Win32Interop.GetCurrentThreadId();
                hookReadyEvent.Set();

                if (hookInstallFailed)
                {
                    Debug.LogError("[GlobalMouseWheelForwarder] SetWindowsHookEx가 실패했습니다(핸들 0).");
                    return;
                }

                while (Win32Interop.GetMessage(out _, IntPtr.Zero, 0, 0) > 0)
                {
                }

                Win32Interop.UnhookWindowsHookEx(hookHandle);
            }
            catch (Exception e)
            {
                hookInstallFailed = true;
                Debug.LogError($"[GlobalMouseWheelForwarder] 후크 스레드에서 예외가 발생했습니다: {e}");
                hookReadyEvent.Set();
            }
        }

        /// <summary>
        /// 훅 스레드에서 실행된다 - Unity API를 호출하지 않는다(Debug.Log 제외 - 훅 설치/해제처럼
        /// 드물게만 호출되는 경로에서는 써도 되지만, 이 콜백은 스크롤할 때마다 반복 호출되는 핫
        /// 패스이므로 여기서는 절대 로그를 남기지 않는다). TransparentWindowController.Instance의
        /// NativeWindowHandle/IsScreenPointClickThrough는 순수 필드 읽기라 스레드 경계를 넘어도 안전하다.
        /// </summary>
        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && (wParam == (IntPtr)Win32Interop.WM_MOUSEWHEEL || wParam == (IntPtr)Win32Interop.WM_MOUSEHWHEEL))
            {
                TransparentWindowController controller = TransparentWindowController.Instance;
                IntPtr ourHwnd = controller != null ? controller.NativeWindowHandle : IntPtr.Zero;

                if (controller != null && ourHwnd != IntPtr.Zero)
                {
                    var hookData = (Win32Interop.MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Win32Interop.MSLLHOOKSTRUCT));
                    Win32Interop.POINT pt = hookData.pt;

                    if (controller.IsScreenPointClickThrough(pt.X, pt.Y))
                    {
                        IntPtr target = FindWindowBelowOverlay(pt, ourHwnd);
                        if (target != IntPtr.Zero)
                        {
                            // mouseData의 상위 16비트에 휠 델타가 이미 WM_MOUSEWHEEL의 wParam과 같은
                            // 자리(HIWORD)로 들어있다 - 키 상태 플래그(LOWORD)는 저수준 훅에서 알 수
                            // 없어 0으로 둔다(Ctrl+휠 같은 조합만 영향, 스크롤 자체는 정상 전달됨).
                            IntPtr wheelWParam = new IntPtr((int)(hookData.mouseData & 0xFFFF0000));
                            IntPtr wheelLParam = MakeLParam(pt.X, pt.Y);
                            Win32Interop.PostMessage(target, (uint)wParam, wheelWParam, wheelLParam);
                        }

                        // 원본은 삼킨다 - 그대로 두면 우리 창과 대상 창 양쪽에 중복 전달될 수 있다.
                        return (IntPtr)1;
                    }
                }
            }

            return Win32Interop.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
        }

        /// <summary>
        /// WindowFromPoint는 확장 스타일과 무관하게 기하학적 최상단 창을 돌려주므로(WS_EX_TRANSPARENT
        /// 여도) 클릭 관통 영역에서는 보통 우리 자신(ourHwnd)이 반환된다. GetWindow(..., GW_HWNDNEXT)로
        /// Z-order를 한 단계씩 내려가며, 우리 창이 아니고 화면에 보이며 그 점을 포함하는 첫 창을
        /// 진짜 대상으로 판정한다. 무한 루프 방지를 위해 순회 횟수를 제한한다.
        /// </summary>
        private static IntPtr FindWindowBelowOverlay(Win32Interop.POINT screenPoint, IntPtr ourHwnd)
        {
            IntPtr candidate = Win32Interop.WindowFromPoint(screenPoint);
            if (candidate == IntPtr.Zero) return IntPtr.Zero;

            const int maxWindowsToWalk = 64;
            for (int i = 0; i < maxWindowsToWalk && candidate != IntPtr.Zero; i++)
            {
                bool isCandidateValid = candidate != ourHwnd &&
                    Win32Interop.IsWindowVisible(candidate) &&
                    Win32Interop.GetWindowRect(candidate, out Win32Interop.RECT rect) &&
                    screenPoint.X >= rect.Left && screenPoint.X <= rect.Right &&
                    screenPoint.Y >= rect.Top && screenPoint.Y <= rect.Bottom;

                if (isCandidateValid) return candidate;

                candidate = Win32Interop.GetWindow(candidate, Win32Interop.GW_HWNDNEXT);
            }

            return IntPtr.Zero;
        }

        private static IntPtr MakeLParam(int x, int y)
        {
            int lo = x & 0xFFFF;
            int hi = y & 0xFFFF;
            return new IntPtr((hi << 16) | lo);
        }
#endif
    }
}
