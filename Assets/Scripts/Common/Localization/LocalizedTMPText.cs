using TMPro;
using UnityEngine;
using UnityEngine.Localization.Components;

namespace Common
{
    /// <summary>
    /// Arguments가 필요 없는 정적 UI 텍스트를 로컬라이징하는 최소 컴포넌트.
    /// 기본 Localize String Event의 복잡한 Inspector 대신
    /// 카테고리 번호 + 숫자 키만 지정하면 되도록 한다.
    ///
    /// 동적 문자열({0} 등 Arguments가 필요한 경우)은 이 컴포넌트를 쓰지 말고
    /// 전용 컴포넌트에서 <see cref="LocalizedTextReference"/>를 직접 다룬다.
    /// </summary>
    [AddComponentMenu("Localization/Localized TMP Text")]
    public class LocalizedTMPText : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI target;
        [SerializeField] private LocalizedTextReference text;

        private bool subscribed;

        private void Reset()
        {
            target = GetComponent<TextMeshProUGUI>();
        }

        private void OnEnable()
        {
            if (target == null)
            {
                target = GetComponent<TextMeshProUGUI>();
            }

            if (target == null)
            {
                Debug.LogWarning($"[LocalizedTMPText] '{name}': TextMeshProUGUI 참조가 없어 로컬라이징을 적용할 수 없습니다.", this);
                return;
            }

            if (text == null || !text.HasReference)
            {
                Debug.LogWarning($"[LocalizedTMPText] '{name}': Localization Table/Key 참조가 비어 있습니다. Inspector에서 Category와 Key를 지정하세요.", this);
                return;
            }

            // StringChanged 구독 자체가 최초 로드를 유발하고, 이후 Locale 변경 시 자동으로 재호출된다.
            text.StringChanged += ApplyLocalizedText;
            subscribed = true;
        }

        private void OnDisable()
        {
            if (!subscribed)
            {
                return;
            }

            text.StringChanged -= ApplyLocalizedText;
            subscribed = false;
        }

        private void ApplyLocalizedText(string localizedText)
        {
            if (target != null)
            {
                target.text = localizedText;
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (target == null)
            {
                target = GetComponent<TextMeshProUGUI>();
            }

            if (target != null && target.TryGetComponent(out LocalizeStringEvent _))
            {
                Debug.LogWarning(
                    $"[LocalizedTMPText] '{name}': 같은 오브젝트에 Localize String Event가 함께 붙어 있습니다. " +
                    "두 컴포넌트가 같은 TMP 텍스트를 번갈아 덮어쓰므로 하나만 남기세요.", this);
            }
        }
#endif
    }
}
