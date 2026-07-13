using System;
using UnityEngine;

namespace DesktopWindow
{
    /// <summary>
    /// Windows 전역 저수준 키보드 후크(WH_KEYBOARD_LL)를 걸어서,
    /// 이 앱 창이 비활성(포커스 없음) 상태여도 사용자가 다른 앱에서 입력한 키를 감지한다.
    /// 어떤 키가 눌렸는지는 구분하지 않고 "키가 눌렸다"는 신호만 사용한다.
    /// Win32 API 기반이라 Windows 빌드에서만 전역으로 동작하며, 에디터/다른 플랫폼에서는
    /// 이 창에 포커스가 있을 때만 감지하는 Input.anyKeyDown으로 대체된다.
    /// </summary>
    [DisallowMultipleComponent]
    public class GlobalKeyboardHook : MonoBehaviour
    {
        public static bool AnyKeyDownThisFrame { get; private set; }

#if UNITY_STANDALONE_WIN
        private IntPtr hookHandle = IntPtr.Zero;
        private Win32Interop.LowLevelKeyboardProc hookProc;
#endif

        private void OnEnable()
        {
#if UNITY_EDITOR
            Debug.LogWarning("[GlobalKeyboardHook] 전역 키보드 후크는 빌드된 Windows 실행 파일(.exe)에서만 동작합니다. Editor Play 모드에서는 이 창에 포커스가 있을 때만 감지됩니다.");
#elif UNITY_STANDALONE_WIN
            hookProc = HookCallback;
            using (var curProcess = System.Diagnostics.Process.GetCurrentProcess())
            using (var curModule = curProcess.MainModule)
            {
                hookHandle = Win32Interop.SetWindowsHookEx(Win32Interop.WH_KEYBOARD_LL, hookProc,
                    Win32Interop.GetModuleHandle(curModule.ModuleName), 0);
            }

            if (hookHandle == IntPtr.Zero)
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
            if (hookHandle != IntPtr.Zero)
            {
                Win32Interop.UnhookWindowsHookEx(hookHandle);
                hookHandle = IntPtr.Zero;
            }
#endif
        }

        private void LateUpdate()
        {
#if UNITY_EDITOR
            AnyKeyDownThisFrame = Input.anyKeyDown;
#elif UNITY_STANDALONE_WIN
            // 이번 프레임의 모든 Update가 끝난 뒤에만 초기화한다. 콜백에서 true로 세팅되는 시점이
            // 프레임 경계 어디든, 그 프레임 동안의 Update들은 항상 값을 읽을 수 있어야 하기 때문이다.
            AnyKeyDownThisFrame = false;
#else
            AnyKeyDownThisFrame = Input.anyKeyDown;
#endif
        }

#if UNITY_STANDALONE_WIN
        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && (wParam == (IntPtr)Win32Interop.WM_KEYDOWN || wParam == (IntPtr)Win32Interop.WM_SYSKEYDOWN))
            {
                // LateUpdate가 아니라 콜백에서 직접 세팅해야 이번 프레임의 Update들이 1프레임 지연 없이 즉시 읽는다.
                AnyKeyDownThisFrame = true;
            }

            return Win32Interop.CallNextHookEx(hookHandle, nCode, wParam, lParam);
        }
#endif
    }
}
