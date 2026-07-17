#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
using System;
using System.Threading;
using UnityEngine;

namespace DesktopWindow
{
    /// <summary>
    /// Windows 빌드에서 KeyBuddy가 동시에 두 프로세스로 실행되는 것을 막는다. 씬이 로드되기 전에
    /// (RuntimeInitializeOnLoadMethod(BeforeSceneLoad)) 판정한다 - 두 번째 인스턴스는
    /// TransparentWindowController/GlobalKeyboardHook 등 어떤 씬 오브젝트도 초기화되지 않은 채
    /// 곧바로 종료되므로, 화면에 아무것도 남기지 않는다.
    ///
    /// 파일 락 대신 Named Mutex를 쓴다 - OS가 프로세스 종료 시(정상/비정상 모두) 자동으로 정리해주므로
    /// "이전 실행이 비정상 종료돼서 락이 남아있는" 상황을 별도로 처리할 필요가 없다.
    ///
    /// Local\ 접두사로 세션 범위 Mutex를 쓴다 - Global\은 시스템 전역 네임스페이스라 별도 권한이
    /// 필요할 수 있고, 이 앱은 같은 사용자 세션 안에서 중복 실행만 막으면 충분하다.
    ///
    /// 이름은 ProductName만 쓰지 않는다 - 다른 프로그램과 우연히 겹칠 수 있어 CompanyName +
    /// ProductName + 고정 접미사를 조합한다.
    ///
    /// #if UNITY_STANDALONE_WIN && !UNITY_EDITOR: UNITY_STANDALONE_WIN은 활성 빌드 타겟이 Windows
    /// Standalone이면 에디터 안에서도 함께 정의된다 - UNITY_EDITOR를 명시적으로 제외하지 않으면
    /// Editor Play Mode에서 이 Mutex 판정이 실행돼, 두 번째 Play를 시작했을 때(또는 빌드가 이미 떠
    /// 있을 때) Application.Quit()가 Play Mode를 예기치 않게 중단시킬 수 있다.
    /// </summary>
    internal static class SingleInstanceGuard
    {
        private const string MutexNameSuffix = "KeyBuddy.SingleInstanceGuard";

        // 프로세스가 살아있는 동안 계속 들고 있어야 한다 - 지역 변수로 두면 이 메서드가 끝나는 순간
        // GC 대상이 되어 Mutex가 조기에 해제될 수 있다(OS 핸들 자체는 프로세스 종료 시 자동 정리되지만,
        // 그 전에 GC로 인해 소유권을 잃으면 두 번째 인스턴스가 우리보다 먼저 Mutex를 획득해버릴 수 있다).
        private static Mutex ownedMutex;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void EnsureSingleInstance()
        {
            string mutexName = $@"Local\{Application.companyName}.{Application.productName}.{MutexNameSuffix}";

            try
            {
                var mutex = new Mutex(initiallyOwned: true, name: mutexName, createdNew: out bool createdNew);

                if (createdNew)
                {
                    ownedMutex = mutex;
                    Debug.Log("[SingleInstanceGuard] Primary instance acquired");
                    return;
                }

                Debug.Log("[SingleInstanceGuard] Existing instance detected; quitting duplicate");

                // 이 프로세스가 새로 만든 게 아니므로 소유권이 없다 - 굳이 들고 있을 필요 없이 바로 정리한다.
                mutex.Close();
                Application.Quit();
            }
            catch (Exception e)
            {
                // Mutex 생성/조회 자체가 실패해도(권한, 이름 충돌 등 드문 환경 문제) 앱이 죽으면 안 된다 -
                // 이 경우 중복 실행 방지는 포기하고 평소대로 계속 진행한다(fail-open).
                Debug.LogWarning($"[SingleInstanceGuard] Mutex 확인 실패, 중복 실행 방지 없이 계속 진행합니다: {e.Message}");
            }
        }
    }
}
#endif
