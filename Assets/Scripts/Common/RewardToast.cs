using UnityEngine;

namespace Common
{
    /// <summary>
    /// PlayerProgress.OnExpGained/OnLevelUp을 구독해 ToastManager 스택으로 넘겨주는 얇은 연결
    /// 컴포넌트. 실제 표시/애니메이션/풀링은 ToastManager + ToastInstance가 담당한다 - 이 클래스는
    /// 별도 연결 코드 없이 동작하는 진입점 역할만 유지한다.
    /// </summary>
    public class RewardToast : MonoBehaviour
    {
        [Tooltip("비워두면 같은 오브젝트에서 ToastManager를 찾는다.")]
        [SerializeField] private ToastManager toastManager;

        private void Awake()
        {
            if (toastManager == null)
            {
                toastManager = GetComponent<ToastManager>();
            }
        }

        private void OnEnable()
        {
            PlayerProgress.OnExpGained += HandleExpGained;
            PlayerProgress.OnLevelUp += HandleLevelUp;
        }

        private void OnDisable()
        {
            PlayerProgress.OnExpGained -= HandleExpGained;
            PlayerProgress.OnLevelUp -= HandleLevelUp;
        }

        private void HandleExpGained(int amount)
        {
            ShowToast($"+{amount} EXP");
        }

        private void HandleLevelUp(int newLevel)
        {
            ShowToast("LEVEL UP!");
        }

        /// <summary>다른 보상 토스트에도 재사용할 수 있는 진입점.</summary>
        public void ShowToast(string message)
        {
            if (toastManager == null)
            {
                Debug.LogWarning("[RewardToast] ToastManager를 찾을 수 없습니다.", this);
                return;
            }
            toastManager.Show(message);
        }
    }
}
