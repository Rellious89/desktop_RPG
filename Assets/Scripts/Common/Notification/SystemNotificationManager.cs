using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Common
{
    /// <summary>
    /// 시스템 알림 스택 전체를 소유하는 관리자. Notification(VerticalLayoutGroup이 붙은 오브젝트)에
    /// 하나만 두고, 알림 요청이 있을 때 에디터에서 만든 NotificationSlot 프리팹을 복제한다 - 런타임에
    /// 코드로 슬롯 계층을 조립하지 않는다.
    ///
    /// <b>타입(notificationId) 단위로 하나만 유지한다.</b> 서로 다른 타입은 동시에 쌓이고, 같은 타입은
    /// 최종적으로 최신 하나만 남는다. 같은 타입의 새 요청이 오면 기존 카드의 문구만 조용히 바꾸지 않고
    /// 새 카드를 등장시킨 뒤(사용자가 "새 알림이 왔다"는 것을 알 수 있도록) 이전 카드를 잠시 뒤에
    /// 종료시킨다. 순서가 중요하다 - 먼저 지우고 새로 만들면 등장 연출이 끊겨 보인다.
    ///
    /// <b>currentByType 교체는 새 카드를 만든 즉시</b> 일어난다. 그래서 이전 카드의 지연 종료 콜백이
    /// 도착했을 때 그 카드는 이미 "최신"이 아니고, Dictionary에서 새 카드의 등록을 지우지 않는다
    /// (제거할 때 반드시 등록된 인스턴스와 같은지 확인한다).
    ///
    /// 같은 타입 요청이 매우 빠르게 반복돼도 카드가 누적되지 않는다 - 타입마다 현재 1개, 물러난 1개까지만
    /// 두고, 그보다 오래된 카드는 연출 없이 즉시 버린다.
    /// </summary>
    [DisallowMultipleComponent]
    public class SystemNotificationManager : MonoBehaviour
    {
        /// <summary>씬에 하나만 둔다(ToastManager/CharacterRoster와 같은 패턴).</summary>
        public static SystemNotificationManager Instance { get; private set; }

        [Header("References")]
        [Tooltip("알림 한 개의 프리팹. 루트(NotificationSlot)에 SystemNotificationItemView가 붙어 있어야 한다.")]
        [SerializeField] private SystemNotificationItemView slotPrefab;

        [Tooltip("생성된 Slot의 부모. 비워두면 이 컴포넌트가 붙은 오브젝트를 쓴다 " +
                 "(VerticalLayoutGroup이 적층 위치를 담당하는 Notification).")]
        [SerializeField] private RectTransform notificationRoot;

        [Header("Stacking")]
        [Tooltip("새 알림을 첫 번째 sibling으로 넣는다. 현재 Notification의 VerticalLayoutGroup " +
                 "(Reverse Arrangement 꺼짐)에서는 첫 sibling이 시각적으로 가장 위다. 레이아웃 설정을 " +
                 "바꿔 순서가 뒤집히면 이 값을 끈다.")]
        [SerializeField] private bool newestAsFirstSibling = true;

        [Header("Same Type Replacement")]
        [Tooltip("같은 타입 알림이 새로 오면 새 카드가 등장한 뒤 이 시간만큼 기다린 다음 이전 카드를 " +
                 "종료한다. TimeScale 영향을 받지 않는 실제 시간이다.")]
        [Min(0f)]
        [SerializeField] private float sameTypeReplacementDelay = 0.25f;

        // 타입마다 "논리적으로 지금 활성인" 알림 하나.
        private readonly Dictionary<string, SystemNotificationItemView> currentByType =
            new Dictionary<string, SystemNotificationItemView>();

        // 타입마다 교체로 물러나 종료를 기다리는(또는 종료 연출 중인) 알림 하나. 그 이상은 두지 않는다.
        private readonly Dictionary<string, SystemNotificationItemView> retiringByType =
            new Dictionary<string, SystemNotificationItemView>();

        private readonly Dictionary<string, Coroutine> retireRoutines = new Dictionary<string, Coroutine>();

        // 종료 경로와 무관하게 살아 있는 모든 인스턴스 - 게임/씬 종료 시 일괄 정리용.
        private readonly List<SystemNotificationItemView> alive = new List<SystemNotificationItemView>();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[SystemNotificationManager] 씬에 SystemNotificationManager가 이미 있습니다. " +
                                 "이 인스턴스는 무시합니다.", this);
                enabled = false;
                return;
            }
            Instance = this;

            if (slotPrefab == null)
            {
                Debug.LogError("[SystemNotificationManager] Slot Prefab이 지정되지 않아 알림을 만들 수 없습니다 - " +
                               "NotificationSlot 프리팹을 연결하세요.", this);
            }
        }

        private void OnDestroy()
        {
            if (Instance != this) return;

            Instance = null;
            ClearAll();
        }

        /// <summary>
        /// 알림을 표시한다. 같은 타입이 이미 떠 있으면 새 카드를 등장시킨 뒤 이전 카드를 지연 종료한다.
        /// 만들어진 인스턴스를 돌려주며, 설정이 잘못돼 만들 수 없으면 null이다.
        /// </summary>
        public SystemNotificationItemView Show(SystemNotificationDefinition definition)
        {
            if (definition == null)
            {
                Debug.LogError("[SystemNotificationManager] Definition이 null이라 알림을 만들 수 없습니다.", this);
                return null;
            }
            if (slotPrefab == null)
            {
                Debug.LogError("[SystemNotificationManager] Slot Prefab이 지정되지 않았습니다.", this);
                return null;
            }

            string id = definition.NotificationId;
            if (string.IsNullOrWhiteSpace(id))
            {
                Debug.LogError($"[SystemNotificationManager] '{definition.name}'의 Notification ID가 비어 있어 " +
                               "타입을 구분할 수 없습니다.", definition);
                return null;
            }

            RectTransform parent = ResolveRoot();
            if (parent == null)
            {
                Debug.LogError("[SystemNotificationManager] 알림을 붙일 RectTransform이 없습니다 - Notification Root를 " +
                               "연결하거나 이 컴포넌트를 RectTransform 오브젝트에 두세요.", this);
                return null;
            }

            // 이미 물러나 있던 같은 타입 카드는 여기서 버린다 - 요청이 빠르게 반복돼도 같은 타입이
            // (현재 1개 + 물러난 1개)를 넘지 않는다.
            DiscardRetiring(id);

            currentByType.TryGetValue(id, out SystemNotificationItemView previous);

            SystemNotificationItemView view = Instantiate(slotPrefab, parent);
            view.name = $"{slotPrefab.name}_{id}";
            if (newestAsFirstSibling)
            {
                view.transform.SetAsFirstSibling();
            }

            alive.Add(view);
            view.Bind(definition, HandleRemovalRequested);

            // 이전 카드의 종료보다 먼저 최신 인스턴스를 등록한다 - 이 순서가 "이전 카드의 지연 종료가
            // 새 등록을 지우지 않는다"는 보장의 근거다.
            currentByType[id] = view;

            // Slot Prefab이 비활성 상태로 저장돼 있어도(수동 테스트용 원본을 그대로 연결한 경우 등)
            // 복제본은 알림으로 표시돼야 한다 - 활성/비활성 어느 프리팹을 연결해도 동작하게 한다.
            if (!view.gameObject.activeSelf)
            {
                view.gameObject.SetActive(true);
            }

            view.PlayEnter();

            if (previous != null && previous != view)
            {
                retiringByType[id] = previous;
                retireRoutines[id] = StartCoroutine(RetireAfterDelay(id, previous));
            }

            return view;
        }

        /// <summary>이 알림의 종료 연출을 시작한다. 실제 제거는 연출이 끝난 뒤에 일어난다.</summary>
        public void Close(SystemNotificationItemView item)
        {
            if (item == null) return;

            item.BeginExit();
        }

        /// <summary>이 타입의 알림을 모두 닫는다(현재 카드 + 교체로 물러나 대기 중인 카드).</summary>
        public void CloseByType(string notificationId)
        {
            if (string.IsNullOrWhiteSpace(notificationId)) return;

            if (currentByType.TryGetValue(notificationId, out SystemNotificationItemView current) && current != null)
            {
                current.BeginExit();
            }

            StopRetireRoutine(notificationId);
            if (retiringByType.TryGetValue(notificationId, out SystemNotificationItemView retiring) && retiring != null)
            {
                retiring.BeginExit();
            }
        }

        /// <summary>이 타입에서 논리적으로 지금 활성인 알림. 없으면 null이다.</summary>
        public SystemNotificationItemView GetCurrent(string notificationId)
        {
            if (string.IsNullOrWhiteSpace(notificationId)) return null;
            return currentByType.TryGetValue(notificationId, out SystemNotificationItemView view) ? view : null;
        }

        /// <summary>새 카드가 등장할 시간을 준 뒤 이전 카드를 종료시킨다. 기다리는 동안 사용자가 그 카드를
        /// 직접 닫아 이미 제거됐을 수 있으므로 다시 확인한다.</summary>
        private IEnumerator RetireAfterDelay(string id, SystemNotificationItemView previous)
        {
            if (sameTypeReplacementDelay > 0f)
            {
                // 일시정지 중에도 교체가 진행되도록 TimeScale과 무관한 시간을 쓴다
                // (UITweenTransition의 Ignore Time Scale과 같은 이유다).
                yield return new WaitForSecondsRealtime(sameTypeReplacementDelay);
            }

            retireRoutines.Remove(id);
            if (previous == null) yield break;

            previous.BeginExit();
        }

        /// <summary>같은 타입에서 이미 물러난 카드를 연출 없이 즉시 버린다.</summary>
        private void DiscardRetiring(string id)
        {
            StopRetireRoutine(id);

            if (!retiringByType.TryGetValue(id, out SystemNotificationItemView older)) return;
            retiringByType.Remove(id);

            if (older == null) return;
            alive.Remove(older);
            older.Release();
            Destroy(older.gameObject);
        }

        /// <summary>종료 연출이 끝난 카드를 목록에서 지우고 제거한다.</summary>
        private void HandleRemovalRequested(SystemNotificationItemView view)
        {
            if (view == null) return;

            string id = view.NotificationId;
            if (!string.IsNullOrWhiteSpace(id))
            {
                // 닫히는 인스턴스가 지금 등록된 최신 인스턴스와 같은지 반드시 확인한다 - 교체로 물러난
                // 이전 카드의 종료가 새 카드의 등록을 지우면 그 타입이 "떠 있지 않은 것"이 된다.
                if (currentByType.TryGetValue(id, out SystemNotificationItemView current) && current == view)
                {
                    currentByType.Remove(id);
                }
                if (retiringByType.TryGetValue(id, out SystemNotificationItemView retiring) && retiring == view)
                {
                    retiringByType.Remove(id);
                    StopRetireRoutine(id);
                }
            }

            alive.Remove(view);
            Destroy(view.gameObject);
        }

        private void StopRetireRoutine(string id)
        {
            if (!retireRoutines.TryGetValue(id, out Coroutine routine)) return;

            retireRoutines.Remove(id);
            if (routine != null)
            {
                StopCoroutine(routine);
            }
        }

        private RectTransform ResolveRoot()
        {
            return notificationRoot != null ? notificationRoot : transform as RectTransform;
        }

        /// <summary>게임/씬 종료 경로. 알림을 하나씩 Exit 연출할 필요는 없으므로 지연 작업을 취소하고
        /// 로컬라이징 구독과 Tween만 정리한다(오브젝트 파괴는 씬 종료가 처리한다).</summary>
        private void ClearAll()
        {
            StopAllCoroutines();
            retireRoutines.Clear();
            currentByType.Clear();
            retiringByType.Clear();

            for (int i = 0; i < alive.Count; i++)
            {
                SystemNotificationItemView view = alive[i];
                if (view == null) continue;

                view.Release();
            }
            alive.Clear();
        }
    }
}
