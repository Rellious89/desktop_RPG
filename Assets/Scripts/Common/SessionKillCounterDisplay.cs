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
        private const string MissingReferenceDisplay = "<Missing Localization>";

        [SerializeField] private LocalizedTextReference killCountFormat;

        private TextMeshProUGUI text;
        private readonly object[] formatArguments = new object[1];
        private bool subscribed;
        private bool missingReferenceLogged;

        private void Awake()
        {
            text = GetComponent<TextMeshProUGUI>();
        }

        private void OnEnable()
        {
            Target.AnyTargetDefeated += HandleAnyTargetDefeated;

            if (killCountFormat != null && killCountFormat.HasReference)
            {
                // 구독 즉시 최초 로드가 일어나므로 Arguments를 먼저 채워 둔다.
                formatArguments[0] = SessionKillCounter.SessionKillCount;
                killCountFormat.Arguments = formatArguments;

                killCountFormat.StringChanged += ApplyLocalizedText;
                subscribed = true;
            }

            Refresh();
        }

        private void OnDisable()
        {
            Target.AnyTargetDefeated -= HandleAnyTargetDefeated;

            if (subscribed)
            {
                killCountFormat.StringChanged -= ApplyLocalizedText;
                subscribed = false;
            }
        }

        private void HandleAnyTargetDefeated(string targetId)
        {
            Refresh();
        }

        private void Refresh()
        {
            int count = SessionKillCounter.SessionKillCount;

            if (killCountFormat == null || !killCountFormat.HasReference)
            {
                // 번역 값 누락은 Unity Localization의 fallback이 처리한다.
                // 여기로 오는 경우는 Table/Key 참조 자체가 없는 설정 오류이므로 조용히 영어로 대체하지 않는다.
                if (!missingReferenceLogged)
                {
                    missingReferenceLogged = true;
                    Debug.LogError(
                        $"[SessionKillCounterDisplay] '{name}': Localization Table/Key 참조가 비어 있습니다. " +
                        "Inspector에서 Category 01 UI / Key 1을 지정하세요.", this);
                }

                text.text = $"{MissingReferenceDisplay} {count}";
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

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (TryGetComponent(out LocalizedTMPText _))
            {
                Debug.LogWarning(
                    $"[SessionKillCounterDisplay] '{name}': 같은 오브젝트의 LocalizedTMPText가 " +
                    "킬카운트 포맷을 인자 없이 덮어씁니다. 동적 포맷은 이 컴포넌트가 담당하므로 " +
                    "LocalizedTMPText를 제거하세요.", this);
            }
        }
#endif
    }
}
