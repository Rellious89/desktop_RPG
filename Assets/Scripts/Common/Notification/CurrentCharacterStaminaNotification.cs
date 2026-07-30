using Character;
using UnityEngine;

namespace Common
{
    /// <summary>
    /// "지금 소환된 캐릭터의 행동력이 0이 됐다"는 조건과 시스템 알림을 잇는 얇은 연결 컴포넌트.
    /// 행동력 규칙(소비/저장/교체)은 <see cref="CharacterRoster"/>가 그대로 소유하고, 이 컴포넌트는
    /// 판정 결과를 알림 요청으로 바꾸는 일만 한다.
    ///
    /// <b>Update에서 행동력을 검사하지 않는다.</b> 값이 실제로 바뀐 경우에만 발생하는
    /// <see cref="CharacterRoster.CharacterStateChanged"/>와 교체 시점의
    /// <see cref="CharacterRoster.CurrentCharacterChanged"/> 두 이벤트가 유일한 근거다.
    ///
    /// <b>알림 1회의 단위는 "행동력 0 상태로의 진입"이다.</b> 캐릭터 활성화 기간 전체가 아니라, 그
    /// 안에서 0에 진입할 때마다 다시 알린다.
    ///   - 행동력 0 유지 중에는 추가 알림이 없다(사용자가 알림을 닫아도 마찬가지 - UI를 닫은 것일
    ///     뿐 행동력 상태를 바꾸지 않는다).
    ///   - 1 이상으로 회복되면 다음 0 진입에서 다시 알릴 수 있는 상태로 풀리지만, 회복 시점 자체는
    ///     알리지 않는다.
    ///   - 회복 후 다시 0이 되면 새 알림 1회.
    ///   - 캐릭터가 교체되면 새 캐릭터의 현재 행동력 기준으로 다시 판정한다(0이면 즉시 1회).
    /// 컴포넌트가 잠시 꺼졌다 켜지는 것은 그 자체로 상태를 초기화하지 않지만, 꺼진 동안 캐릭터가
    /// 바뀌었거나 행동력이 회복됐을 수 있으므로 재활성화 시 현재 상태를 다시 평가한다.
    /// </summary>
    [DisallowMultipleComponent]
    public class CurrentCharacterStaminaNotification : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("알림을 만들 관리자. 비워두면 SystemNotificationManager.Instance를 쓴다.")]
        [SerializeField] private SystemNotificationManager notificationManager;

        [Tooltip("행동력 소진 알림 Definition(Notification ID: stamina_depleted).")]
        [SerializeField] private SystemNotificationDefinition staminaDepletedNotification;

        // 지금 알림 단위로 보고 있는 캐릭터.
        private CharacterDefinition observedCharacter;

        // "지금의 행동력 0 상태"에 대해 이미 알렸는가. 캐릭터 활성화 기간 전체를 잠그는 값이 아니라
        // 0 상태 하나에만 걸리는 값이다 - 1 이상으로 회복되면 Evaluate가 이 값을 풀어 다음 0 진입에서
        // 다시 알릴 수 있게 한다.
        private bool notifiedForCurrentDepletion;

        // Start에서 최초 동기화를 한 뒤에만 재활성화 동기화를 한다 - Awake/OnEnable 순서에 따라
        // CharacterRoster가 아직 시작 캐릭터를 정하지 않았을 수 있다.
        private bool initialSyncDone;

        private void OnEnable()
        {
            CharacterRoster.CurrentCharacterChanged += HandleCurrentCharacterChanged;
            CharacterRoster.CharacterStateChanged += HandleCharacterStateChanged;

            // 꺼져 있는 동안 캐릭터가 바뀌었을 수 있다. 같은 캐릭터라면 SyncToCurrentCharacter가
            // 아무것도 하지 않으므로 이번 기간의 알림 여부가 초기화되지 않는다.
            if (initialSyncDone)
            {
                SyncToCurrentCharacter();
            }
        }

        private void OnDisable()
        {
            CharacterRoster.CurrentCharacterChanged -= HandleCurrentCharacterChanged;
            CharacterRoster.CharacterStateChanged -= HandleCharacterStateChanged;
        }

        /// <summary>CharacterRoster는 Awake에서 시작 캐릭터를 정하므로 그 이벤트를 놓칠 수 있다 -
        /// 모든 Awake가 끝난 Start에서 현재 상태를 한 번 직접 평가한다. 시작 시점에 이미 0이면
        /// 이번 실행에서 한 번 알린다.</summary>
        private void Start()
        {
            if (staminaDepletedNotification == null)
            {
                Debug.LogError("[CurrentCharacterStaminaNotification] Stamina Depleted Notification Definition이 " +
                               "지정되지 않아 행동력 소진 알림을 만들 수 없습니다.", this);
            }
            if (ResolveManager() == null)
            {
                Debug.LogError("[CurrentCharacterStaminaNotification] SystemNotificationManager를 찾을 수 없습니다 - " +
                               "Notification Manager를 연결하거나 씬에 SystemNotificationManager를 두세요.", this);
            }
            if (CharacterRoster.Instance == null)
            {
                Debug.LogWarning("[CurrentCharacterStaminaNotification] 씬에 CharacterRoster가 없어 행동력 소진을 " +
                                 "감지할 수 없습니다.", this);
            }

            initialSyncDone = true;
            SyncToCurrentCharacter();
        }

        private void HandleCurrentCharacterChanged(CharacterDefinition next)
        {
            // 새 캐릭터의 행동력을 기준으로 다시 판정한다 - 0이면 즉시 1회, 1 이상이면 대기 상태다.
            observedCharacter = next;
            notifiedForCurrentDepletion = false;
            Evaluate();
        }

        private void HandleCharacterStateChanged(CharacterDefinition definition)
        {
            CharacterRoster roster = CharacterRoster.Instance;
            if (roster == null || definition == null) return;

            // 현재 캐릭터가 아닌 캐릭터의 레벨/행동력 변경은 알림 대상이 아니다.
            if (definition != roster.Current) return;

            if (definition != observedCharacter)
            {
                // 추적이 어긋난 경우(이 컴포넌트가 꺼져 있는 동안 교체됨) 새 캐릭터로 맞춘다.
                observedCharacter = definition;
                notifiedForCurrentDepletion = false;
            }

            // 증가/감소 모두 Evaluate로 넘긴다 - 회복(증가)은 잠금을 풀고, 소진(감소)은 알림을 낸다.
            Evaluate();
        }

        private void SyncToCurrentCharacter()
        {
            CharacterRoster roster = CharacterRoster.Instance;
            if (roster == null) return;

            CharacterDefinition current = roster.Current;

            if (current != observedCharacter)
            {
                observedCharacter = current;
                notifiedForCurrentDepletion = false;
            }

            // 같은 캐릭터라도 컴포넌트가 꺼져 있던 동안 행동력이 회복됐을 수 있으므로 항상 재평가한다.
            Evaluate();
        }

        /// <summary>현재 행동력에 따라 양방향으로 상태를 바꾼다 - 0 초과면 다음 소진을 위해 잠금을
        /// 풀고, 0 이하면 아직 이 소진 상태를 알리지 않았을 때만 알림을 요청한다.</summary>
        private void Evaluate()
        {
            if (observedCharacter == null) return;

            CharacterRoster roster = CharacterRoster.Instance;
            if (roster == null) return;

            if (roster.GetStamina(observedCharacter) > 0)
            {
                // 행동력이 회복됐으므로 다음 0 진입에서 다시 알릴 수 있다. 회복 시점 자체는 알리지 않는다.
                notifiedForCurrentDepletion = false;
                return;
            }

            if (notifiedForCurrentDepletion) return;

            // 알림 요청보다 먼저 잠가 같은 프레임의 중복 요청을 방지한다.
            notifiedForCurrentDepletion = true;
            RequestStaminaDepletedNotification();
        }

        private void RequestStaminaDepletedNotification()
        {
            if (staminaDepletedNotification == null) return;

            SystemNotificationManager manager = ResolveManager();
            if (manager == null) return;

            // 설정 오류로 실패해도 이번 활성화 기간의 표시는 되돌리지 않는다 - 매 이벤트마다 같은
            // 오류 로그가 반복되는 것을 막는다(원인은 Start의 오류 로그가 이미 가리키고 있다).
            manager.Show(staminaDepletedNotification);
        }

        private SystemNotificationManager ResolveManager()
        {
            return notificationManager != null ? notificationManager : SystemNotificationManager.Instance;
        }
    }
}
