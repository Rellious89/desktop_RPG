using TMPro;
using UnityEngine;
using UnityEngine.Localization;

namespace Common
{
    /// <summary>
    /// SessionKillCounter.SessionKillCount를 UI 텍스트(Canvas > lb_KillCount)에 표시하는 최소 디버그 UI.
    /// </summary>
    [RequireComponent(typeof(TextMeshProUGUI))]
    public class SessionKillCounterDisplay : MonoBehaviour
    {
        [SerializeField] private LocalizedString killCountFormat;

        private TextMeshProUGUI text;
        private readonly object[] formatArguments = new object[1];

        private void Awake()
        {
            text = GetComponent<TextMeshProUGUI>();
        }

        private void OnEnable()
        {
            Target.AnyTargetDefeated += HandleAnyTargetDefeated;

            if (killCountFormat != null && !killCountFormat.IsEmpty)
            {
                killCountFormat.StringChanged += ApplyLocalizedText;
            }

            Refresh();
        }

        private void OnDisable()
        {
            Target.AnyTargetDefeated -= HandleAnyTargetDefeated;

            if (killCountFormat != null)
            {
                killCountFormat.StringChanged -= ApplyLocalizedText;
            }
        }

        private void HandleAnyTargetDefeated(string targetId)
        {
            Refresh();
        }

        private void Refresh()
        {
            int count = SessionKillCounter.SessionKillCount;

            if (killCountFormat == null || killCountFormat.IsEmpty)
            {
                // Inspector 연결이 빠진 상태에서도 기존 HUD가 사라지지 않도록 영어 기본값을 유지한다.
                text.text = $"Kill Count : {count}";
                return;
            }

            formatArguments[0] = count;
            killCountFormat.Arguments = formatArguments;
            killCountFormat.RefreshString();
        }

        private void ApplyLocalizedText(string localizedText)
        {
            if (text != null)
            {
                text.text = localizedText;
            }
        }
    }
}
