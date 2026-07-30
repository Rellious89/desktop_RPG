using System;
using System.Collections.Generic;
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
    /// 공격 제외 키: UI 단축키(ESC)나 시스템 단축키(창 배치 모드 전환)는 공격/콤보 입력
    /// (AnyKeyDownThisFrame)으로 처리되면 안 된다. 제외 대상은 두 곳에서 모인다 -
    /// <see cref="AttackInputExclusionTable"/> 에셋(데이터로 관리하는 UI 단축키)과
    /// <see cref="RegisterExcludedKey"/>로 등록하는 런타임 단축키(자기 키를 Inspector에 들고 있는
    /// 컴포넌트용). 제외 키는 <b>키를 식별하는 이 단계에서</b> 걸러지므로 애초에
    /// AnyKeyDownThisFrame에 포함되지 않고, 대신 <see cref="WasExcludedKeyDownThisFrame"/>으로만
    /// 감지된다 - 공격/콤보/누적 충전/행동력 쪽에는 어떤 예외 처리도 넣지 않는다.
    ///
    /// 제외 키는 <b>자동 반복(auto-repeat)을 걸러낸다</b>. Windows는 키를 누르고 있으면 WM_KEYDOWN을
    /// 반복해서 보내는데, UI 단축키가 그대로 반복되면 ESC를 한 번 길게 누르는 것으로 열린 패널이
    /// 전부 닫힌다. 그래서 제외 키만 눌림 상태를 따로 추적해서 "실제로 처음 눌린 순간" 한 번만
    /// 신호를 낸다. 일반 키(공격 입력)는 지금까지대로 반복 입력을 그대로 흘려보낸다.
    ///
    /// 훅 스레드는 KeyCode를 직접 다루지 않고, 메인 스레드가 미리 계산해둔 vkCode 배열만 비교한다.
    /// 지원 범위는 A-Z / 0-9 / F1-F15 / Escape다(그 밖의 키를 등록하면 제외되지 않고 그냥
    /// AnyKeyDownThisFrame으로 흘러간다 - 등록 시 경고를 남긴다).
    /// </summary>
    [DisallowMultipleComponent]
    public class GlobalKeyboardHook : MonoBehaviour
    {
        [Header("진단용 (원인 이분탐색)")]
        [Tooltip("끄면 전역 저수준 후크(WH_KEYBOARD_LL)를 아예 설치하지 않고 Input.anyKeyDown으로만 감지한다 - " +
                 "이 창에 포커스가 있을 때만 입력을 받게 되지만, 마우스 끊김이 이 후크 때문인지 확인하는 용도.")]
        [SerializeField] private bool useGlobalHook = true;

        [Header("공격 제외 키")]
        [Tooltip("공격 입력으로 쓰지 않을 키 목록 에셋. 비워두면 런타임에 등록된 키만 제외된다.")]
        [SerializeField] private AttackInputExclusionTable attackInputExclusions;

        public static bool AnyKeyDownThisFrame { get; private set; }

        // 제외 키 목록: 테이블 에셋에서 온 것 + RegisterExcludedKey로 등록된 것의 합집합.
        // 목록이 바뀌면 dirty 플래그만 올리고, 실제 스냅샷 재구성은 메인 스레드가 다음 Update에서 한다.
        private static readonly HashSet<KeyCode> runtimeExcludedKeys = new HashSet<KeyCode>();
        private static bool exclusionsDirty = true;

        // 이번 프레임에 눌린 제외 키. 인덱스는 resolvedExcludedKeys와 같다.
        private static KeyCode[] resolvedExcludedKeys = System.Array.Empty<KeyCode>();
        private static bool[] excludedKeyDownThisFrame = System.Array.Empty<bool>();

        /// <summary>
        /// 자기 단축키를 Inspector에 들고 있는 컴포넌트가 그 키를 공격 입력에서 빼달라고 등록하는
        /// 진입점(예: 창 배치 모드 전환 키). 데이터로 관리하는 UI 단축키는 이 메서드가 아니라
        /// <see cref="AttackInputExclusionTable"/> 에셋에 넣는다. 같은 키를 여러 번 등록해도 안전하다.
        /// </summary>
        public static void RegisterExcludedKey(KeyCode key)
        {
            if (key == KeyCode.None) return;
            if (!runtimeExcludedKeys.Add(key)) return;

            exclusionsDirty = true;
        }

        /// <summary>제외 키가 이번 프레임에 <b>처음</b> 눌렸는지(자동 반복 제외). 제외 목록에 없는
        /// 키를 물으면 항상 false다 - 그런 키는 공격 입력으로 흘러가기 때문에 여기서 감지하면
        /// 같은 입력이 두 곳에서 쓰인다.</summary>
        public static bool WasExcludedKeyDownThisFrame(KeyCode key)
        {
            for (int i = 0; i < resolvedExcludedKeys.Length; i++)
            {
                if (resolvedExcludedKeys[i] == key) return excludedKeyDownThisFrame[i];
            }
            return false;
        }

#if UNITY_STANDALONE_WIN
        // 훅 스레드가 기록하고, 메인 스레드가 매 프레임 Interlocked.Exchange로 읽어서 비우는 pending
        // 플래그. 0/1만 쓰지만 Interlocked가 int만 지원해서 int로 둔다.
        private static int pendingAnyKey;

        /// <summary>훅 스레드가 보는 제외 키 스냅샷. vkCode 배열과 그에 대응하는 pending/눌림 상태
        /// 배열을 <b>한 덩어리로</b> 묶어 두는 이유는, 목록이 바뀔 때 배열 두 개를 따로 교체하면 훅
        /// 스레드가 길이가 다른 두 배열을 섞어 읽을 수 있기 때문이다. 교체는 참조 하나만 바꾼다
        /// (목록 변경은 시작 시점의 몇 번뿐이라, 그 순간 눌린 키 하나를 놓칠 수 있는 것은 감수한다).</summary>
        private sealed class ExclusionSnapshot
        {
            public readonly int[] VkCodes;
            public readonly int[] PendingDown;
            public readonly int[] Held;

            public ExclusionSnapshot(int[] vkCodes)
            {
                VkCodes = vkCodes;
                PendingDown = new int[vkCodes.Length];
                Held = new int[vkCodes.Length];
            }
        }

        private static volatile ExclusionSnapshot exclusions = new ExclusionSnapshot(System.Array.Empty<int>());

        private Thread hookThread;
        private ManualResetEventSlim hookReadyEvent;
        private volatile bool hookInstallFailed;
        private volatile uint hookThreadId;
        private Win32Interop.LowLevelKeyboardProc hookProc;
#endif

        private void OnEnable()
        {
            // 제외 키 목록/스냅샷은 static이라 씬을 다시 로드해도 남는다 - 이 인스턴스의 테이블 에셋
            // 기준으로 한 번 다시 만들도록 표시한다(실제 재구성은 첫 Update에서 일어난다).
            exclusionsDirty = true;

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
            RebuildExclusionsIfDirty();

#if UNITY_EDITOR
            UpdateFromUnityInput();
#elif UNITY_STANDALONE_WIN
            if (!useGlobalHook)
            {
                // 진단 모드: 후크를 설치하지 않았으니 포커스 있을 때만 감지되는 Input으로 대체한다.
                UpdateFromUnityInput();
                return;
            }

            UpdateFromHook();
#else
            UpdateFromUnityInput();
#endif
        }

        /// <summary>제외 키 목록(테이블 + 런타임 등록)이 바뀌었을 때만 스냅샷을 다시 만든다 - 보통
        /// 시작 시점에 한두 번 실행되고 그 뒤에는 아무 일도 하지 않는다. 메인 스레드 전용이다.</summary>
        private void RebuildExclusionsIfDirty()
        {
            if (!exclusionsDirty) return;
            exclusionsDirty = false;

            var keys = new List<KeyCode>(runtimeExcludedKeys);
            if (attackInputExclusions != null)
            {
                IReadOnlyList<KeyCode> tableKeys = attackInputExclusions.ExcludedKeys;
                for (int i = 0; i < tableKeys.Count; i++)
                {
                    if (tableKeys[i] != KeyCode.None && !keys.Contains(tableKeys[i])) keys.Add(tableKeys[i]);
                }
            }

            resolvedExcludedKeys = keys.ToArray();
            excludedKeyDownThisFrame = new bool[resolvedExcludedKeys.Length];

#if UNITY_STANDALONE_WIN
            var vkCodes = new int[resolvedExcludedKeys.Length];
            for (int i = 0; i < resolvedExcludedKeys.Length; i++)
            {
                vkCodes[i] = KeyCodeToVirtualKey(resolvedExcludedKeys[i]);
                if (vkCodes[i] == 0)
                {
                    Debug.LogWarning($"[GlobalKeyboardHook] '{resolvedExcludedKeys[i]}'는 Virtual Key 변환을 " +
                                     "지원하지 않는 키라 Windows 빌드에서 공격 입력으로 그대로 흘러갑니다 " +
                                     "(지원 범위: A-Z / 0-9 / F1-F15 / Escape).", this);
                }
            }
            exclusions = new ExclusionSnapshot(vkCodes);
#endif
        }

        /// <summary>이 창에 포커스가 있을 때만 감지되는 Unity Input 경로(에디터/비Windows/진단 모드).
        /// Input.GetKeyDown은 자동 반복을 이미 걸러주므로 별도 처리가 필요 없다.</summary>
        private void UpdateFromUnityInput()
        {
            bool anyExcludedDown = false;
            for (int i = 0; i < resolvedExcludedKeys.Length; i++)
            {
                bool down = Input.GetKeyDown(resolvedExcludedKeys[i]);
                excludedKeyDownThisFrame[i] = down;
                anyExcludedDown |= down;
            }

            // 제외 키가 눌린 프레임에는 공격 입력을 만들지 않는다. Input.anyKeyDown은 어떤 키가
            // 눌렸는지 구분하지 못하므로, 제외 키와 일반 키를 같은 프레임에 누르면 둘 다 무시된다
            // (기존 동작과 같은 한계다 - 훅 경로에서는 키별로 정확히 구분된다).
            AnyKeyDownThisFrame = Input.anyKeyDown && !anyExcludedDown;
        }

#if UNITY_STANDALONE_WIN
        /// <summary>훅 스레드가 쌓아둔 pending 플래그를 원자적으로 읽고 비운다 - 읽는 사이에 훅이
        /// 새 값을 써도 Interlocked라 유실되지 않고 다음 프레임에 반영된다.</summary>
        private void UpdateFromHook()
        {
            AnyKeyDownThisFrame = Interlocked.Exchange(ref pendingAnyKey, 0) != 0;

            ExclusionSnapshot snapshot = exclusions;
            for (int i = 0; i < excludedKeyDownThisFrame.Length; i++)
            {
                excludedKeyDownThisFrame[i] = i < snapshot.PendingDown.Length
                                              && Interlocked.Exchange(ref snapshot.PendingDown[i], 0) != 0;
            }
        }
#endif

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
            if (nCode >= 0)
            {
                bool isKeyDown = wParam == (IntPtr)Win32Interop.WM_KEYDOWN || wParam == (IntPtr)Win32Interop.WM_SYSKEYDOWN;
                bool isKeyUp = wParam == (IntPtr)Win32Interop.WM_KEYUP || wParam == (IntPtr)Win32Interop.WM_SYSKEYUP;

                if (isKeyDown || isKeyUp)
                {
                    // KBDLLHOOKSTRUCT 전체를 마샬링하면 호출마다 박싱 할당이 생긴다. vkCode(구조체의 첫
                    // DWORD 필드)만 필요하므로 Marshal.ReadInt32로 직접 읽는다.
                    int vkCode = Marshal.ReadInt32(lParam);

                    // 스냅샷 참조를 한 번만 읽어 둔다 - 메인 스레드가 목록을 교체해도 이 호출 안에서는
                    // 같은 배열 쌍을 일관되게 본다.
                    ExclusionSnapshot snapshot = exclusions;
                    int excludedIndex = IndexOfVkCode(snapshot, vkCode);

                    if (excludedIndex >= 0)
                    {
                        if (isKeyUp)
                        {
                            // 손을 뗐다 - 다음 눌림을 다시 "처음 눌림"으로 인정한다.
                            Interlocked.Exchange(ref snapshot.Held[excludedIndex], 0);
                        }
                        else if (Interlocked.Exchange(ref snapshot.Held[excludedIndex], 1) == 0)
                        {
                            // 자동 반복이 아니라 실제로 처음 눌린 경우에만 신호를 낸다.
                            Interlocked.Exchange(ref snapshot.PendingDown[excludedIndex], 1);
                        }
                    }
                    else if (isKeyDown)
                    {
                        // 일반 키는 지금까지대로 반복 입력까지 그대로 공격 입력으로 흘려보낸다.
                        Interlocked.Exchange(ref pendingAnyKey, 1);
                    }
                }
            }

            // 이 스레드가 설치한 훅이므로 CallNextHookEx의 hhk 인자는 실제로 무시된다(다음 훅으로
            // 자동 전달됨) - 명시적으로 IntPtr.Zero를 넘겨도 안전하다.
            return Win32Interop.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
        }

        /// <summary>훅 스레드에서 호출된다 - 배열 순회뿐이고 Unity API를 부르지 않는다. 제외 키
        /// 목록은 매우 짧아(현재 2개) 선형 검색으로 충분하다.</summary>
        private static int IndexOfVkCode(ExclusionSnapshot snapshot, int vkCode)
        {
            int[] vkCodes = snapshot.VkCodes;
            for (int i = 0; i < vkCodes.Length; i++)
            {
                if (vkCodes[i] != 0 && vkCodes[i] == vkCode) return i;
            }
            return -1;
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
            if (keyCode == KeyCode.Escape) return 0x1B; // VK_ESCAPE
            return 0;
        }
#endif
    }
}
