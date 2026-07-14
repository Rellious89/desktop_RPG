using TMPro;
using UnityEngine;

namespace Common
{
    /// <summary>
    /// SessionKillCounter.SessionKillCount를 UI 텍스트(Canvas > lb_KillCount)에 표시하는 최소 디버그 UI.
    /// </summary>
    [RequireComponent(typeof(TextMeshProUGUI))]
    public class SessionKillCounterDisplay : MonoBehaviour
    {
        [SerializeField] private string label = "Kill Count";

        private TextMeshProUGUI text;

        private void Awake()
        {
            text = GetComponent<TextMeshProUGUI>();
        }

        private void OnEnable()
        {
            Target.AnyTargetDefeated += HandleAnyTargetDefeated;
            Refresh();
        }

        private void OnDisable()
        {
            Target.AnyTargetDefeated -= HandleAnyTargetDefeated;
        }

        private void HandleAnyTargetDefeated(string targetId)
        {
            Refresh();
        }

        private void Refresh()
        {
            text.text = $"{label} : {SessionKillCounter.SessionKillCount}";
        }
    }
}
