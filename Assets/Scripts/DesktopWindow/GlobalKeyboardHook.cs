using System;
using UnityEngine;

#if UNITY_STANDALONE_WIN
using System.Runtime.InteropServices;
using System.Threading;
#endif

namespace DesktopWindow
{
    /// <summary>
    /// Windows 전역 저수준 키보드 후크(WH_KEYBOARD_LL)를 걸어서,
    /// 이 앱 창이 비활성(포커스 없음) 상태여도 사용자가 다른 앱에서 입력한 키를 감지한다.
    /// 기본적으로는 어떤 키가 눌렸는지 구분하지 않고 "키가 눌렸다"는 신호(AnyKeyDownThisFrame)만 쓴다.
    /// Win32 API 기반이라 Windows 빌드에서만 전역으로 동작하며, 에디터/다른 플랫폼에서는
    /// 이 창에 포커스가 있을 때만 감지하는 Input.anyKeyDown으로 대체된다.
    ///
    /// 전용 백그라운드 스레드에서 훅을 설치/펌핑한다(중요): WH_KEYBOARD_LL 콜백은 "훅을 설치한
    /// 스레드가 메시지 루프를 계속 펌핑해야" 디스패치된다. 예전에는 Unity 메인 스레드에서 설치했는데,
    /// 이 앱은 FpsLimiter로 30fps(≈33ms 간격)로 제한되어 있어서 메인 스레드의 메시지 펌프 주기가
    /// 그만큼 느리다 - Windows는 저수준 훅 콜백이 응답할 때까지 시스템 전체의 해당 키 입력 디스패치를
    /// 순서대로 처리하므로, 이 지연이 다른 앱의 마우스/키보드 입력까지 끊기는 것처럼 보이게 만들었다.
    /// 전용 스레드는 GetMessage로 계속 블로킹 펌핑만 하므로 Unity 프레임레이트와 완전히 분리된다.
    ///
    /// 스레드 경계: 콜백은 훅 스레드에서 실행되므로 Unity API(Time, Debug.Log 포함)를 호출하면 안 된다.
    /// AnyKeyDownThisFrame/ExcludedKeyDownThisFrame에 직접 쓰지 않고, Interlocked로 보호되는
    /// pending 플래그에만 기록한다. Unity 메인 스레드는 매 프레임 Update()에서 그 플래그를
    /// Interlocked.Exchange로 원자적으로 읽고 리셋해서 프레임 값으로 반영한다(값을 잃어버리지 않음).
    ///
    /// ExcludedKey: 창 배치 모드 전환 같은 시스템 단축키는 공격/콤보 입력(AnyKeyDownThisFrame)으로
    /// 처리되면 안 된다. ExcludedKey에 등록한 키는 AnyKeyDownThisFrame에서 제외되고, 대신
    /// ExcludedKeyDownThisFrame으로만 감지된다. 훅 스레드는 KeyCode를 직접 다루지 않고, 메인
    /// 스레드가 매 프레임 미리 계산해둔 vkCode(volatile int)만 비교한다. 지원 범위는 A-Z / 0-9 /
    /// F1-F15로 한정한다(그 밖의 키를 등록하면 제외 처리가 되지 않고 그냥 AnyKeyDownThisFrame으로
    /// 흘러간다).
    /// </summary>
    [DisallowMultipleComponent]
    public class GlobalKeyboardHook : MonoBehaviour
    {
        [Header("진단용 (원인 이분탐색)")]
        [Tooltip("끄면 전역 저수준 후크(WH_KEYBOARD_LL)를 아예 설치하지 않고 Input.anyKeyDown으로만 감지한다 - " +
                 "이 창에 포커스가 있을 때만 입력을 받게 되지만, 마우스 끊김이 이 후크 때문인지 확인하는 용도.")]
        [SerializeField] private bool useGlobalHook = true;

        public static bool AnyKeyDownThisFrame { get; private set; }

        /// <summary>AnyKeyDownThisFrame에서 제외할 단축키. 기본값 None이면 아무 것도 제외하지 않는다.</summary>
        public static KeyCode ExcludedKey { get; set; } = KeyCode.None;

        /// <summary>ExcludedKey로 등록된 키가 이번 프레임에 눌렸는지.</summary>
        public static bool ExcludedKeyDownThisFrame { get; private set; }

#if UNITY_STANDALONE_WIN
        // 훅 스레드가 기록하고, 메인 스레드가 매 프레임 Interlocked.Exchange로 읽어서 비우는 pending
        // 플래그. 0/1만 쓰지만 Interlocked가 int만 지원해서 int로 둔다.
        private static int pendingAnyKey;
        private static int pendingExcludedKey;

        // 메인 스레드가 매 프레임 갱신하는, ExcludedKey에 대응하는 vkCode 캐시. 훅 스레드는 이 값만
        // 정수 비교하고, KeyCode 변환이나 프로퍼티 접근은 하지 않는다.
        private static volatile int cachedExcludedVkCode;

        private Thread hookThread;
        private ManualResetEventSlim hookReadyEvent;
        private volatile bool hookInstallFailed;
        private volatile uint hookThreadId;
        private Win32Interop.LowLevelKeyboardProc hookProc;
#endif

        private void OnEnable()
        {
#if UNITY_EDITOR
            Debug.LogWarning("[GlobalKeyboardHook] 전역 키보드 후크는 빌드된 Windows 실행 파일(.exe)에서만 동작합니다. Editor Play 모드에서는 이 창에 포커스가 있을 때만 감지됩니다.");
#elif UNITY_STANDALONE_WIN
            if (!useGlobalHook)
            {
                Debug.LogWarning("[GlobalKeyboardHook] useGlobalHook이 꺼져 있어 전역 후크를 설치하지 않습니다(진단 모드). 이 창에 포커스가 있을 때만 입력을 감지합니다.");
                return;
            }

            hookReadyEvent = new ManualResetEventSlim(false);
            hookThread = new Thread(HookThreadMain) { IsBackground = true, Name = "GlobalKeyboardHookThread" };
            hookThread.Start();

            // 시작 시 1회, 훅 스레드가 실제로 설치를 마칠 때까지만 짧게 대기한다(기존에도 SetWindowsHookEx
            // 자체가 메인 스레드에서 동기 호출이었으니 시작 지연은 늘지 않는다). 이후에는 절대 블로킹하지 않는다.
            if (!hookReadyEvent.Wait(2000))
            {
                Debug.LogError("[GlobalKeyboardHook] 후크 스레드가 시간 안에 시작되지 않았습니다.");
            }
            else if (hookInstallFailed)
            {
                Debug.LogError("[GlobalKeyboardHook] 전역 키보드 후크 설치 실패. 이 창에 포커스가 있을 때만 감지됩니다.");
            }
#else
            Debug.LogWarning("[GlobalKeyboardHook] 이 기능은 Win32 API 기반이라 Windows 빌드에서만 지원됩니다. 현재 플랫폼에서는 이 창에 포커스가 있을 때만 감지됩니다.");
#endif
        }

        private void OnDisable()
        {
#if UNITY_STANDALONE_WIN
            if (hookThread == null) return;

            if (hookThreadId != 0)
            {
                // 훅 스레드의 GetMessage 루프를 깨워서 빠져나오게 한다 - 그 직후 스레드 안에서
                // UnhookWindowsHookEx가 호출된다(HookThreadMain 참고).
                Win32Interop.PostThreadMessage(hookThreadId, Win32Interop.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
            }

            hookThread.Join(2000);
            hookThread = null;

            hookReadyEvent?.Dispose();
            hookReadyEvent = null;
            hookThreadId = 0;
#endif
        }

        private void Update()
        {
#if UNITY_EDITOR
            bool excluded = ExcludedKey != KeyCode.None && Input.GetKeyDown(ExcludedKey);
            ExcludedKeyDownThisFrame = excluded;
            AnyKeyDownThisFrame = Input.anyKeyDown && !excluded;
#elif UNITY_STANDALONE_WIN
            if (!useGlobalHook)
            {
                // 진단 모드: 후크를 설치하지 않았으니 포커스 있을 때만 감지되는 Input으로 대체한다.
                bool excludedFallback = ExcludedKey != KeyCode.None && Input.GetKeyDown(ExcludedKey);
                ExcludedKeyDownThisFrame = excludedFallback;
                AnyKeyDownThisFrame = Input.anyKeyDown && !excludedFallback;
                return;
            }

            // 훅 스레드가 참조할 수 있도록 이번 프레임 기준 제외 키의 vkCode를 미리 계산해둔다.
            cachedExcludedVkCode = KeyCodeToVirtualKey(ExcludedKey);

            // pending 플래그를 원자적으로 읽고 동시에 0으로 리셋한다 - 그 사이 훅 스레드가 새로 값을
            // 써도 Interlocked라 유실되지 않고 다음 프레임에 반영된다.
            AnyKeyDownThisFrame = Interlocked.Exchange(ref pendingAnyKey, 0) != 0;
            ExcludedKeyDownThisFrame = Interlocked.Exchange(ref pendingExcludedKey, 0) != 0;
#else
            bool excluded = ExcludedKey != KeyCode.None && Input.GetKeyDown(ExcludedKey);
            ExcludedKeyDownThisFrame = excluded;
            AnyKeyDownThisFrame = Input.anyKeyDown && !excluded;
#endif
        }

#if UNITY_STANDALONE_WIN
        /// <summary>
        /// 전용 스레드의 진입점. 이 스레드 안에서 훅을 설치하고, WM_QUIT을 받을 때까지 GetMessage로
        /// 블로킹 펌핑만 한다 - Unity 메인 스레드/프레임레이트와 완전히 분리되어 있어서, 콜백이
        /// 얼마나 빨리 디스패치되는지가 Unity 쪽 부하와 무관해진다.
        /// </summary>
        private void HookThreadMain()
        {
            // 이 메서드는 스레드 시작 시 1회만 실행되는 초기화 코드라 Debug.LogError를 써도 된다
            // (Unity의 Debug 로그 API는 어느 스레드에서 호출해도 안전하다 - 금지되는 건 HookCallback
            // 같은 매 키 입력마다 반복 호출되는 핫 패스뿐이다). try/catch로 감싸서 여기서 실패하면
            // "타임아웃"만 찍히고 진짜 원인이 안 보이는 상황을 막는다.
            try
            {
                hookProc = HookCallback; // 델리게이트를 필드에 rooted 유지 - 지역 변수면 GC가 수거해 콜백이 끊길 수 있다.

                // Process.GetCurrentProcess().MainModule 대신 GetModuleHandle(null)을 쓴다 - 전자는
                // .NET 프로세스 introspection이라 백그라운드 스레드/IL2CPP 조합에서 실패할 여지가 있고,
                // 후자는 "이 모듈(EXE) 자신의 핸들을 달라"는 단순 P/Invoke라 더 안전하다.
                IntPtr moduleHandle = Win32Interop.GetModuleHandle(null);
                IntPtr hookHandle = Win32Interop.SetWindowsHookEx(Win32Interop.WH_KEYBOARD_LL, hookProc, moduleHandle, 0);

                hookInstallFailed = hookHandle == IntPtr.Zero;
                hookThreadId = Win32Interop.GetCurrentThreadId();
                hookReadyEvent.Set(); // 메인 스레드의 OnEnable 대기를 풀어준다(성공/실패 여부는 hookInstallFailed로 전달).

                if (hookInstallFailed)
                {
                    Debug.LogError("[GlobalKeyboardHook] SetWindowsHookEx가 실패했습니다(핸들 0). 이 창에 포커스가 있을 때만 감지됩니다.");
                    return; // 설치 실패 - 메시지 루프를 돌 이유가 없다.
                }

                // WM_QUIT이 올 때까지 블로킹. 이 펌핑 자체가 있어야 저수준 훅 콜백이 실제로 호출된다.
                while (Win32Interop.GetMessage(out _, IntPtr.Zero, 0, 0) > 0)
                {
                }

                Win32Interop.UnhookWindowsHookEx(hookHandle);
            }
            catch (Exception e)
            {
                hookInstallFailed = true;
                Debug.LogError($"[GlobalKeyboardHook] 후크 스레드에서 예외가 발생했습니다: {e}");
                hookReadyEvent.Set(); // 예외로 죽더라도 OnEnable의 대기를 반드시 풀어준다(타임아웃으로 새는 것 방지).
            }
        }

        /// <summary>
        /// 훅 스레드에서 실행된다 - Unity API를 절대 호출하지 않는다(Time, Debug.Log 포함). 플래그만
        /// 세팅하고 즉시 반환한다: while 루프, Thread.Sleep, 대기, Unity 오브젝트 조작 전부 금지.
        /// </summary>
        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && (wParam == (IntPtr)Win32Interop.WM_KEYDOWN || wParam == (IntPtr)Win32Interop.WM_SYSKEYDOWN))
            {
                // KBDLLHOOKSTRUCT 전체를 마샬링하면 호출마다 박싱 할당이 생긴다. vkCode(구조체의 첫
                // DWORD 필드)만 필요하므로 Marshal.ReadInt32로 직접 읽는다.
                int vkCode = Marshal.ReadInt32(lParam);
                int excludedVkCode = cachedExcludedVkCode;

                if (excludedVkCode != 0 && vkCode == excludedVkCode)
                {
                    Interlocked.Exchange(ref pendingExcludedKey, 1);
                }
                else
                {
                    Interlocked.Exchange(ref pendingAnyKey, 1);
                }
            }

            // 이 스레드가 설치한 훅이므로 CallNextHookEx의 hhk 인자는 실제로 무시된다(다음 훅으로
            // 자동 전달됨) - 명시적으로 IntPtr.Zero를 넘겨도 안전하다.
            return Win32Interop.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
        }

        /// <summary>
        /// Unity KeyCode -> Win32 가상 키코드(vkCode) 변환. A-Z/0-9/F1-F15 구간은 Unity/Win32 둘 다
        /// 연속된 값으로 정의돼 있어서 오프셋 계산으로 충분하다. 그 밖의 키는 0(미지원)을 반환한다.
        /// </summary>
        private static int KeyCodeToVirtualKey(KeyCode keyCode)
        {
            if (keyCode >= KeyCode.F1 && keyCode <= KeyCode.F15)
            {
                return 0x70 + (keyCode - KeyCode.F1); // VK_F1 = 0x70
            }
            if (keyCode >= KeyCode.A && keyCode <= KeyCode.Z)
            {
                return 0x41 + (keyCode - KeyCode.A); // VK_A = 0x41
            }
            if (keyCode >= KeyCode.Alpha0 && keyCode <= KeyCode.Alpha9)
            {
                return 0x30 + (keyCode - KeyCode.Alpha0); // VK_0 = 0x30
            }
            return 0;
        }
#endif
    }
}
