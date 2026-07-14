using UnityEngine;

namespace Common
{
    /// <summary>
    /// 이번 실행(세션) 동안 처치된 Target 총합을 센다. 저장하지 않으며 앱을 종료하면 초기화된다.
    /// 특정 몬스터가 아니라 Target.AnyTargetDefeated(정적 이벤트)를 구독하기 때문에,
    /// 이후 다른 몬스터가 추가돼도 별도 연결 없이 자동으로 같이 집계된다. 씬에 하나만 두면 된다.
    /// </summary>
    public class SessionKillCounter : MonoBehaviour
    {
        public static int SessionKillCount { get; private set; }

        private void OnEnable()
        {
            Target.AnyTargetDefeated += HandleAnyTargetDefeated;
        }

        private void OnDisable()
        {
            Target.AnyTargetDefeated -= HandleAnyTargetDefeated;
        }

        private void HandleAnyTargetDefeated(string targetId)
        {
            SessionKillCount++;
        }
    }
}
