#if UNITY_STANDALONE_WIN
using System;
using UnityEngine;

namespace DesktopWindow
{
    /// <summary>
    /// 프로세스 DPI 인식을 Per-Monitor V2로 설정한다. TransparentWindowController가 다른 모니터의
    /// 실제 DPI를 GetDpiForMonitor로 정확히 조회하려면, 그보다 먼저 프로세스가 모니터별 DPI 인식
    /// 상태여야 한다(그렇지 않으면 OS가 창을 비트맵으로 자동 확대/축소해서 흐릿하게 만들 수 있음).
    ///
    /// RuntimeInitializeLoadType.SubsystemRegistration은 Unity가 제공하는 가장 이른 관리 코드
    /// 진입점이라 네이티브 창 생성 이전에 실행될 가능성이 가장 높다 - DPI 인식은 창 생성 전에
    /// 설정해야 의미가 있다(생성 후 호출은 OS가 무시하거나 실패할 수 있음).
    ///
    /// Per-Monitor V2(Win10 1703+) -> Per-Monitor V1(Win8.1+) -> System DPI Aware(Vista+) 순으로
    /// 폴백한다. 이미 매니페스트 등으로 DPI 인식이 고정돼 있어 전부 실패해도 예외를 밖으로 던지지
    /// 않는다 - 이 경우 모니터 간 이동 시 크기 보정 정확도가 떨어질 수 있지만 앱이 죽지는 않는다.
    /// </summary>
    internal static class DpiAwarenessBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void SetPerMonitorDpiAwareness()
        {
            if (TrySetPerMonitorV2()) return;
            if (TrySetPerMonitorV1()) return;
            TrySetSystemDpiAware();
        }

        private static bool TrySetPerMonitorV2()
        {
            try
            {
                return Win32Interop.SetProcessDpiAwarenessContext(Win32Interop.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
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

        private static bool TrySetPerMonitorV1()
        {
            try
            {
                // S_OK(0) 또는 이미 설정됨(E_ACCESSDENIED)이 아니면 실패로 간주하고 다음 폴백으로 넘어간다.
                return Win32Interop.SetProcessDpiAwareness(Win32Interop.PROCESS_PER_MONITOR_DPI_AWARE) == 0;
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

        private static void TrySetSystemDpiAware()
        {
            try
            {
                Win32Interop.SetProcessDPIAware();
            }
            catch (EntryPointNotFoundException)
            {
            }
            catch (DllNotFoundException)
            {
            }
        }
    }
}
#endif
