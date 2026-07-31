using System.Collections.Generic;
using Common;
using UnityEngine;

namespace Recovery
{
    /// <summary>
    /// "회복이 끝났다"는 도메인 사실과 시스템 알림을 잇는 <b>얇은 연결 컴포넌트</b>. 회복 규칙과 저장은
    /// <see cref="RecoveryStation"/>이 그대로 소유하고, 여기서는 그 결과를
    /// <see cref="SystemNotificationManager"/> 요청으로 바꾸는 일만 한다
    /// (<see cref="CurrentCharacterStaminaNotification"/>과 같은 역할 분담이다).
    ///
    /// <b>회복소 패널과 무관하게 동작한다.</b> 패널이 닫혀 있어도, 한 번도 열지 않았어도 알림은 뜬다 -
    /// 이 컴포넌트는 씬에 상주하며 도메인만 본다.
    ///
    /// <b>"몇 번 알렸는가"를 이벤트로 세지 않는다.</b> 판단 근거는 저장된 per-cycle marker
    /// (RecoverySlotSaveState.completionNotified) 하나뿐이다. 그래서 다음이 전부 자연히 보장된다.
    /// <code>
    /// 매 프레임 반복      -> marker가 서 있으면 대상에서 빠진다
    /// 재시작 후 반복      -> marker는 저장되므로 유지된다
    /// 이벤트 중복 구독    -> 요청 대상은 marker로 정해지므로 두 번 뜨지 않는다
    /// 앱이 꺼진 사이 완료 -> 이벤트가 없어도 켤 때 스캔해서 알린다
    /// 새 회복 주기        -> 시작할 때 marker가 초기화된다
    /// </code>
    ///
    /// <b>marker는 알림이 실제로 수락된 뒤에만 남긴다.</b> 알림 매니저나 회복소가 아직 준비되지 않은
    /// 초기화 순서에서는 아무 표시도 남기지 않고 다음 기회에 다시 시도한다 - 미리 표시를 남기면 그
    /// 주기의 알림을 영원히 잃는다. 반대로 요청 직후 저장에 실패하거나 그 사이에 앱이 죽으면 같은
    /// 알림이 한 번 더 뜰 수는 있는데, 알림을 잃는 것보다 그쪽이 안전하다고 보고 그 방향으로 정했다.
    /// </summary>
    [DisallowMultipleComponent]
    public class RecoveryCompletionNotifier : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("알림을 만들 관리자. 비워두면 SystemNotificationManager.Instance를 쓴다.")]
        [SerializeField] private SystemNotificationManager notificationManager;

        [Tooltip("회복 완료 알림 Definition(Notification ID: recovery_completed). " +
                 "문구는 캐릭터 이름을 {0}으로 받는다.")]
        [SerializeField] private SystemNotificationDefinition recoveryCompletedNotification;

        [Header("Retry")]
        [Tooltip("아직 요청하지 못한 완료 알림이 남아 있을 때 다시 시도하는 간격(초, 실제 시간). " +
                 "요청할 것이 없으면 아무 일도 하지 않는다.")]
        [Min(0.05f)]
        [SerializeField] private float retryIntervalSeconds = 0.5f;

        // 도메인에서 받아 오는 대기 목록(완료 시각 → 슬롯 번호 오름차순).
        private readonly List<RecoveryCompletionNotice> pendingNotices = new List<RecoveryCompletionNotice>();

        // 이번 시도에서 실제로 표시에 성공한 슬롯 번호. 저장은 이 목록으로 한 번만 한다.
        private readonly List<int> acceptedSlots = new List<int>();

        // 확인할 것이 있는지. 켜질 때와 완료 이벤트가 올 때 true가 되고, 남김없이 처리하면 false가 된다.
        private bool flushRequested;

        private float retryTimer;
        private bool missingDefinitionLogged;
        private bool missingManagerLogged;

        private void OnEnable()
        {
            RecoveryService.RecoveryCompleted += HandleRecoveryCompleted;

            // 꺼져 있는 동안(또는 앱이 꺼져 있는 동안) 완료된 슬롯이 있을 수 있다. 이벤트를 기다리지
            // 않고 저장 상태를 직접 확인한다 - 오프라인 완료 알림의 근거가 이 한 줄이다.
            RequestFlush();
        }

        private void OnDisable()
        {
            RecoveryService.RecoveryCompleted -= HandleRecoveryCompleted;
        }

        private void Start()
        {
            if (recoveryCompletedNotification == null)
            {
                Debug.LogError("[RecoveryCompletionNotifier] Recovery Completed Notification Definition이 " +
                               "지정되지 않아 회복 완료 알림을 만들 수 없습니다 - Notification ID가 " +
                               "'recovery_completed'인 Definition을 연결하세요.", this);
            }
        }

        private void Update()
        {
            if (!flushRequested) return;

            retryTimer += Time.unscaledDeltaTime;
            if (retryTimer < retryIntervalSeconds) return;
            retryTimer = 0f;

            Flush();
        }

        /// <summary>완료 이벤트가 오면 즉시 한 번 시도한다. 이벤트를 놓쳐도 <see cref="OnEnable"/>의
        /// 스캔이 같은 일을 하므로, 이 이벤트는 "빨리 반응하기 위한 신호"일 뿐 유일한 근거가 아니다.</summary>
        private void HandleRecoveryCompleted(int slotIndex, Character.CharacterDefinition character)
        {
            RequestFlush();
            Flush();
        }

        private void RequestFlush()
        {
            flushRequested = true;
            retryTimer = retryIntervalSeconds; // 다음 Update에서 곧바로 시도하게 한다.
        }

        /// <summary>
        /// 아직 알리지 않은 완료 슬롯을 순서대로 요청한다. 하나라도 요청하지 못하면 표시를 남기지 않고
        /// 다음 기회에 다시 시도한다.
        /// </summary>
        private void Flush()
        {
            RecoveryStation station = RecoveryService.Station;
            if (station == null) return; // 회복소가 아직 준비되지 않았다 - 표시를 남기지 않고 재시도한다.

            if (station.CollectPendingCompletionNotices(pendingNotices) == 0)
            {
                flushRequested = false;
                return;
            }

            SystemNotificationManager manager = ResolveManager();
            if (manager == null || recoveryCompletedNotification == null)
            {
                // 알림 쪽이 아직 없다. 요청할 것이 남아 있으므로 재시도 상태를 반드시 켜 둔다 -
                // 여기서 끄면 그 주기의 알림을 영영 잃는다(완료 이벤트는 이미 지나갔을 수 있다).
                flushRequested = true;
                WarnMissingDependenciesOnce(manager);
                return;
            }

            acceptedSlots.Clear();
            for (int i = 0; i < pendingNotices.Count; i++)
            {
                RecoveryCompletionNotice notice = pendingNotices[i];
                string characterName = notice.Character != null ? notice.Character.DisplayName : string.Empty;

                SystemNotificationItemView view = manager.Show(recoveryCompletedNotification, characterName);
                if (view == null)
                {
                    // 매니저가 요청을 거절했다(설정 오류 등). 여기까지 성공한 것만 표시로 남기고 멈춘다.
                    break;
                }

                acceptedSlots.Add(notice.SlotIndex);
            }

            // 저장은 성공분에 대해 한 번만.
            if (acceptedSlots.Count > 0) station.MarkCompletionNotified(acceptedSlots);

            // 남은 것이 있으면 계속 재시도한다.
            flushRequested = acceptedSlots.Count < pendingNotices.Count;
        }

        private SystemNotificationManager ResolveManager()
        {
            return notificationManager != null ? notificationManager : SystemNotificationManager.Instance;
        }

        /// <summary>준비되지 않은 의존성은 알려야 하지만, 재시도마다 로그가 쏟아지면 안 된다 -
        /// 종류별로 한 번씩만 남긴다.</summary>
        private void WarnMissingDependenciesOnce(SystemNotificationManager manager)
        {
            if (recoveryCompletedNotification == null && !missingDefinitionLogged)
            {
                missingDefinitionLogged = true;
                Debug.LogError("[RecoveryCompletionNotifier] Recovery Completed Notification Definition이 없어 " +
                               "완료 알림을 표시하지 못했습니다 - 요청은 취소되지 않고 대기합니다.", this);
            }
            if (manager == null && !missingManagerLogged)
            {
                missingManagerLogged = true;
                Debug.LogWarning("[RecoveryCompletionNotifier] SystemNotificationManager를 아직 찾지 못해 완료 " +
                                 "알림을 미뤘습니다 - 준비되면 자동으로 다시 시도합니다.", this);
            }
        }
    }
}
