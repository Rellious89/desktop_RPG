using System;
using UnityEngine;

#if UNITY_STANDALONE_WIN
using System.Runtime.InteropServices;
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
    /// ExcludedKey: 창 배치 모드 전환 같은 시스템 단축키는 공격/콤보 입력(AnyKeyDownThisFrame)으로
    /// 처리되면 안 된다. ExcludedKey에 등록한 키는 AnyKeyDownThisFrame에서 제외되고, 대신
    /// ExcludedKeyDownThisFrame으로만 감지된다. Windows 전역 후크에서 실제 눌린 키를 구분하려면
    /// vkCode 변환이 필요해서, 지원 범위는 A-Z / 0-9 / F1-F15로 한정한다(그 밖의 키를 등록하면
    /// 제외 처리가 되지 않고 그냥 AnyKeyDownThisFrame으로 흘러간다).
    /// </summary>
    [DisallowMultipleComponent]
    public class GlobalKeyboardHook : MonoBehaviour
    {
        public static bool AnyKeyDownThisFrame { get; private set; }

        /// <summary>AnyKeyDownThisFrame에서 제외할 단축키. 기본값 None이면 아무 것도 제외하지 않는다.</summary>
        public static KeyCode ExcludedKey { get; set; } = KeyCode.None;

        /// <summary>ExcludedKey로 등록된 키가 이번 프레임에 눌렸는지.</summary>
        public static bool ExcludedKeyDownThisFrame { get; private set; }

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
            bool excluded = ExcludedKey != KeyCode.None && Input.GetKeyDown(ExcludedKey);
            ExcludedKeyDownThisFrame = excluded;
            AnyKeyDownThisFrame = Input.anyKeyDown && !excluded;
#elif UNITY_STANDALONE_WIN
            // 이번 프레임의 모든 Update가 끝난 뒤에만 초기화한다. 콜백에서 true로 세팅되는 시점이
            // 프레임 경계 어디든, 그 프레임 동안의 Update들은 항상 값을 읽을 수 있어야 하기 때문이다.
            AnyKeyDownThisFrame = false;
            ExcludedKeyDownThisFrame = false;
#else
            bool excluded = ExcludedKey != KeyCode.None && Input.GetKeyDown(ExcludedKey);
            ExcludedKeyDownThisFrame = excluded;
            AnyKeyDownThisFrame = Input.anyKeyDown && !excluded;
#endif
        }

#if UNITY_STANDALONE_WIN
        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && (wParam == (IntPtr)Win32Interop.WM_KEYDOWN || wParam == (IntPtr)Win32Interop.WM_SYSKEYDOWN))
            {
                var hookStruct = (Win32Interop.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Win32Interop.KBDLLHOOKSTRUCT));
                int excludedVkCode = KeyCodeToVirtualKey(ExcludedKey);

                if (excludedVkCode != 0 && (int)hookStruct.vkCode == excludedVkCode)
                {
                    // 등록된 제외 키(예: 창 배치 모드 전환)는 AnyKeyDownThisFrame으로 흘려보내지 않는다 -
                    // 그래야 이 키를 눌러도 공격/콤보가 반응하지 않는다.
                    ExcludedKeyDownThisFrame = true;
                }
                else
                {
                    // LateUpdate가 아니라 콜백에서 직접 세팅해야 이번 프레임의 Update들이 1프레임 지연 없이 즉시 읽는다.
                    AnyKeyDownThisFrame = true;
                }
            }

            return Win32Interop.CallNextHookEx(hookHandle, nCode, wParam, lParam);
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
