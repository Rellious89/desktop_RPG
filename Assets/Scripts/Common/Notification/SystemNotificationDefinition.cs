using UnityEngine;

namespace Common
{
    /// <summary>
    /// 시스템 알림 한 <b>종류</b>의 정의. 알림 인스턴스(카드)가 아니라 "어떤 알림인지"의 원천이다.
    ///
    /// <see cref="NotificationId"/>가 알림 타입의 고유 식별자이며, 같은 id를 가진 알림은 같은 타입으로
    /// 판단해 <see cref="SystemNotificationManager"/>가 최종적으로 하나만 유지한다(중복 대신 교체).
    /// 서로 다른 id는 동시에 쌓인다.
    ///
    /// 메시지는 코드에 문자열로 넣지 않고 이 에셋의 <see cref="LocalizedTextReference"/>가 소유한다 -
    /// 현재 Locale 적용과 언어 전환은 Unity Localization이 담당하고, 실제 표시는
    /// <see cref="SystemNotificationItemView"/>가 이 참조를 구독해서 한다.
    ///
    /// 레이드명 같은 동적 인자({0})가 필요한 알림은 나중에 이 에셋에 인자 정의를 추가하고 View가
    /// 그것을 채우는 방향으로 확장한다 - 그래서 View와 Definition의 역할을 지금부터 나눠 둔다.
    /// </summary>
    [CreateAssetMenu(fileName = "SystemNotificationDefinition", menuName = "Notification/System Notification Definition")]
    public class SystemNotificationDefinition : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("알림 타입의 고유 식별자(stamina_depleted, raid_opened 등). 비워두면 에셋 파일 이름을 쓴다. " +
                 "같은 id를 가진 알림은 같은 타입으로 취급해 최신 하나만 남는다.")]
        [SerializeField] private string notificationId;

        [Header("Message")]
        [Tooltip("알림 본문. 카테고리 번호 + 숫자 키로 지정한다. 코드에는 문구를 넣지 않는다.")]
        [SerializeField] private LocalizedTextReference message = new LocalizedTextReference();

        /// <summary>알림 타입 식별자. 비어 있으면 에셋 이름을 쓴다 - id를 빼먹은 에셋이 빈 문자열 타입
        /// 하나로 뭉쳐서 서로를 교체하는 상황을 막는다.</summary>
        public string NotificationId => string.IsNullOrWhiteSpace(notificationId) ? name : notificationId;

        /// <summary>알림 본문 참조. View가 이 참조를 구독해 현재 Locale 문자열을 받는다.</summary>
        public LocalizedTextReference Message => message;

        /// <summary>Table/Key가 지정되어 있는지 여부(번역 값이 채워져 있는지는 보장하지 않는다).</summary>
        public bool HasMessage => message != null && message.HasReference;
    }
}
