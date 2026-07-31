using System;
using DesktopWindow;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Common
{
    /// <summary>
    /// 화면에 떠 있는 시스템 알림 <b>한 개</b>를 담당한다. NotificationSlot 루트에 붙이고, 내부
    /// item_notification의 <see cref="UITweenTransition"/>이 등장/종료 연출을, 슬롯 자신의 위치는
    /// 부모 Notification의 VerticalLayoutGroup이 담당한다 - 두 축을 나눠야 적층 배치와 Tween이 서로
    /// 위치를 덮어쓰지 않는다.
    ///
    /// <b>이 View는 정책을 판단하지 않는다.</b> 자신이 몇 번째인지, 같은 타입이 이미 떠 있는지,
    /// 누가 먼저 사라져야 하는지는 <see cref="SystemNotificationManager"/>만 결정한다. View는 요청받은
    /// Definition을 표시하고, 닫기 요청을 종료 연출로 바꾸고, 연출이 끝나면 "이제 제거해도 된다"고
    /// 한 번 알리는 데까지만 관여한다. 실제 제거(Destroy)도 Manager가 한다.
    ///
    /// 메시지는 Definition의 <see cref="LocalizedTextReference"/>를 직접 구독해서 표시한다. 알림마다
    /// 키가 다르므로 Inspector에 키를 박아두는 LocalizedTMPText로는 처리할 수 없다 - 같은 TMP 텍스트를
    /// 두 컴포넌트가 함께 갱신하면 안 되므로 lb_message의 LocalizedTMPText는 제거해야 하고,
    /// 남아 있으면 바인딩 시점에 경고를 남긴다.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    [DisallowMultipleComponent]
    public class SystemNotificationItemView : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("등장/종료 연출을 담당하는 item_notification의 UITweenTransition.")]
        [SerializeField] private UITweenTransition transition;

        [Tooltip("본문을 표시할 lb_message의 TextMeshProUGUI.")]
        [SerializeField] private TextMeshProUGUI messageText;

        [Tooltip("이 알림을 닫는 btn_close의 Button.")]
        [SerializeField] private Button closeButton;

        [Tooltip("이 알림 카드가 Windows 투명 창에서 마우스 입력을 받게 하는 영역(선택). " +
                 "지정하면 종료 연출이 시작될 때 입력 수신을 먼저 끈다.")]
        [SerializeField] private WindowInputRegion inputRegion;

        private SystemNotificationDefinition definition;
        private LocalizedTextReference boundMessage;
        private Action<SystemNotificationItemView> removalRequest;
        private bool exiting;

        // 이 카드가 소유한 포맷 인자 <b>사본</b>. 호출부가 넘긴 배열을 그대로 들고 있으면 그 배열을
        // 재사용하거나 고칠 때 이미 떠 있는 카드의 문구까지 바뀐다.
        private object[] messageArguments;

        // 포맷 실패 로그를 카드당 한 번만 남긴다(Locale이 바뀔 때마다 다시 시도되기 때문).
        private bool formatFailureLogged;

        /// <summary>이 알림의 타입 식별자. Manager가 같은 타입 판단에 쓴다.</summary>
        public string NotificationId => definition != null ? definition.NotificationId : null;

        public SystemNotificationDefinition Definition => definition;

        /// <summary>종료 연출이 이미 시작됐는지. 중복 닫기 방지의 근거다.</summary>
        public bool IsExiting => exiting;

        /// <summary>지금 화면에 그려져 있는 본문. 검증과 디버깅용 읽기 전용 값이다.</summary>
        public string CurrentMessageText => messageText != null ? messageText.text : null;

        /// <summary>이 카드가 들고 있는 포맷 인자 개수(사본 기준).</summary>
        public int MessageArgumentCount => messageArguments != null ? messageArguments.Length : 0;

        private void Reset()
        {
            transition = GetComponentInChildren<UITweenTransition>(true);
            messageText = GetComponentInChildren<TextMeshProUGUI>(true);
            closeButton = GetComponentInChildren<Button>(true);
            inputRegion = GetComponentInChildren<WindowInputRegion>(true);
        }

        private void Awake()
        {
            if (closeButton != null)
            {
                closeButton.onClick.AddListener(HandleCloseClicked);
            }
        }

        private void OnDestroy()
        {
            if (closeButton != null)
            {
                closeButton.onClick.RemoveListener(HandleCloseClicked);
            }
            UnbindMessage();
        }

        /// <summary>표시할 알림 종류와 "제거해도 된다"는 알림을 받을 콜백을 지정한다.
        /// <paramref name="onRemovalRequested"/>는 종료 연출이 끝난 뒤 정확히 한 번 호출된다.</summary>
        public void Bind(SystemNotificationDefinition definition, Action<SystemNotificationItemView> onRemovalRequested)
        {
            Bind(definition, null, onRemovalRequested);
        }

        /// <summary>
        /// 표시할 알림 종류와 <b>동적 포맷 인자</b>를 함께 지정한다. 문구의 {0}, {1}...에 채워지며,
        /// 인자를 넘기지 않으면(null 또는 빈 배열) 문구를 <b>그대로</b> 표시한다 - 인자 없는 기존
        /// 알림의 동작이 조금도 달라지지 않게 하기 위함이다.
        ///
        /// <paramref name="arguments"/>는 <b>사본으로 보관</b>한다. 호출부가 배열을 재사용하거나
        /// 내용을 바꿔도 이미 떠 있는 카드의 문구는 영향을 받지 않는다.
        ///
        /// Locale이 바뀌면 <see cref="LocalizedTextReference.StringChanged"/>가 다시 호출되는데,
        /// 그때도 이 사본으로 다시 포맷하므로 언어를 바꿔도 인자가 유지된다.
        /// </summary>
        public void Bind(SystemNotificationDefinition definition, object[] arguments,
                         Action<SystemNotificationItemView> onRemovalRequested)
        {
            this.definition = definition;
            removalRequest = onRemovalRequested;
            exiting = false;
            formatFailureLogged = false;
            messageArguments = arguments != null && arguments.Length > 0
                ? (object[])arguments.Clone()
                : null;

            if (closeButton != null)
            {
                closeButton.interactable = true;
            }

            if (definition == null)
            {
                Debug.LogError("[SystemNotificationItemView] Definition이 없어 알림 내용을 표시할 수 없습니다.", this);
                return;
            }

            WarnOnDuplicateLocalizer();
            BindMessage(definition.Message);
        }

        /// <summary>등장 연출을 재생한다.</summary>
        public void PlayEnter()
        {
            if (transition == null)
            {
                Debug.LogError($"[SystemNotificationItemView] '{name}': UITweenTransition 참조가 없어 등장 연출을 " +
                               "재생할 수 없습니다 - item_notification의 UITweenTransition을 연결하세요.", this);
                return;
            }

            transition.PlayEnter();
        }

        /// <summary>
        /// 종료 연출을 시작한다. 이미 시작된 뒤의 중복 호출은 무시하므로, 닫기 버튼 연타와 Manager의
        /// 교체 종료가 겹쳐도 제거 요청은 한 번만 발생한다.
        /// </summary>
        public void BeginExit()
        {
            if (exiting) return;
            exiting = true;

            if (closeButton != null)
            {
                closeButton.interactable = false;
            }
            if (inputRegion != null)
            {
                // 사라지는 중인 카드가 그 자리의 클릭을 계속 잡아먹지 않게 한다.
                inputRegion.ReceiveMouseInput = false;
            }

            if (transition == null)
            {
                RequestRemoval();
                return;
            }

            transition.PlayExit(RequestRemoval);
        }

        /// <summary>
        /// 연출 없이 즉시 정리한다(게임/씬 종료, 또는 같은 타입 카드가 쌓이지 않도록 즉시 버리는 경로).
        /// 제거 요청 콜백은 호출하지 않는다 - 이 경로를 쓰는 쪽이 이미 자기 목록을 정리하는 중이다.
        /// </summary>
        public void Release()
        {
            exiting = true;
            removalRequest = null;
            messageArguments = null;
            UnbindMessage();

            if (transition != null)
            {
                transition.Stop();
            }
        }

        private void HandleCloseClicked()
        {
            BeginExit();
        }

        private void RequestRemoval()
        {
            Action<SystemNotificationItemView> request = removalRequest;
            removalRequest = null;

            // 제거 직전에 구독을 끊는다 - Manager가 Destroy를 미루더라도 사라질 카드가 Locale 변경에
            // 계속 반응하지 않게 한다.
            UnbindMessage();
            request?.Invoke(this);
        }

        private void BindMessage(LocalizedTextReference reference)
        {
            UnbindMessage();

            if (messageText == null)
            {
                Debug.LogError($"[SystemNotificationItemView] '{name}': 본문 TextMeshProUGUI 참조가 없습니다 - " +
                               "lb_message를 연결하세요.", this);
                return;
            }

            if (reference == null || !reference.HasReference)
            {
                Debug.LogError($"[SystemNotificationItemView] 알림 '{NotificationId}'의 Message에 Localization " +
                               "Table/Key가 지정되지 않았습니다 - Definition 에셋에서 Category와 Key를 지정하세요.", this);
                messageText.text = string.Empty;
                return;
            }

            boundMessage = reference;

            // 구독 자체가 최초 로드를 유발하고, 이후 Locale이 바뀌면 자동으로 다시 호출된다
            // (LocalizedTMPText와 같은 방식이며, 여기서는 키가 알림마다 달라 코드가 직접 관리한다).
            boundMessage.StringChanged += ApplyMessage;
        }

        private void UnbindMessage()
        {
            if (boundMessage == null) return;

            boundMessage.StringChanged -= ApplyMessage;
            boundMessage = null;
        }

        private void ApplyMessage(string localizedText)
        {
            if (messageText != null)
            {
                messageText.text = FormatMessage(localizedText);
            }
        }

        /// <summary>
        /// 보관해 둔 인자로 문구를 포맷한다. 인자가 없으면 <b>string.Format을 부르지 않고</b> 원문을
        /// 그대로 돌려준다 - 인자 없이 표시하는 기존 알림이 {0} 같은 표기 때문에 예외를 맞지 않게 한다.
        ///
        /// 포맷에 실패하면(문구의 자리표시자 번호가 인자 개수를 넘거나 중괄호가 깨진 경우) 예외를
        /// 밖으로 던지지 않고 <b>원문을 그대로 표시</b>한다 - 알림 하나의 문구 오류 때문에 알림 자체가
        /// 사라지거나 다른 처리가 멈추면 안 된다. 원인은 오류 로그로 분명히 남긴다.
        /// </summary>
        private string FormatMessage(string localizedText)
        {
            if (messageArguments == null || string.IsNullOrEmpty(localizedText)) return localizedText;

            try
            {
                return string.Format(localizedText, messageArguments);
            }
            catch (FormatException e)
            {
                if (!formatFailureLogged)
                {
                    formatFailureLogged = true;
                    Debug.LogError($"[SystemNotificationItemView] 알림 '{NotificationId}'의 문구를 인자 " +
                                   $"{messageArguments.Length}개로 포맷하지 못했습니다 - 문구를 그대로 표시합니다. " +
                                   $"Localization 문구의 자리표시자를 확인하세요: {e.Message}", this);
                }
                return localizedText;
            }
        }

        /// <summary>같은 TMP 텍스트를 LocalizedTMPText가 함께 갱신하면 알림마다 다른 문구가 정적 키로
        /// 덮어써진다 - 조용히 두지 않는다.</summary>
        private void WarnOnDuplicateLocalizer()
        {
            if (messageText == null) return;
            if (!messageText.TryGetComponent(out LocalizedTMPText _)) return;

            Debug.LogWarning($"[SystemNotificationItemView] '{messageText.name}'에 LocalizedTMPText가 함께 붙어 " +
                             "있습니다 - 알림 본문은 이 컴포넌트가 런타임에 주입하므로 LocalizedTMPText를 제거하세요.",
                messageText);
        }
    }
}
