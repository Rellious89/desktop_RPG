using System;
using Character;
using Common;
using Inventory;
using UnityEngine;

namespace Recovery
{
    /// <summary>
    /// 회복소를 씬에 붙이는 <b>얇은 껍데기</b>. 규칙은 전부 <see cref="RecoveryStation"/>이 갖고 있고,
    /// 이 컴포넌트는 (1) 밸런스 에셋 연결, (2) CharacterRoster/InventoryManager와의 연결,
    /// (3) 시간 경과 확인 호출, (4) 정적 이벤트 전달만 한다.
    ///
    /// 씬에 하나만 둔다(CharacterRoster/InventoryManager/ToastManager와 같은 패턴). UI는
    /// <see cref="Instance"/>가 아니라 <see cref="Station"/>을 통해 도메인 API를 쓴다.
    ///
    /// 밸런스 에셋이 없거나 값이 잘못되면 조용히 기본값으로 대체하지 않고 오류를 남긴 뒤 스스로
    /// 비활성화한다 - 잘못된 값으로 0초 즉시 회복 같은 동작이 일어나지 않게 하기 위함이다.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(100)] // CharacterRoster/InventoryManager의 Awake가 먼저 끝난 뒤에 연결한다.
    public class RecoveryService : MonoBehaviour
    {
        [Header("Balance")]
        [Tooltip("회복 비용/시간/슬롯 수의 단일 원천. 비워두면 회복소가 동작하지 않는다.")]
        [SerializeField] private RecoveryBalanceTable balanceTable;

        [Header("Tick")]
        [Tooltip("회복 진행을 확인하는 간격(초, 실제 시간). 진행은 저장된 UTC 시각으로만 계산하므로 " +
                 "이 값은 화면 갱신 주기일 뿐이며 회복 속도에 영향을 주지 않는다.")]
        [Min(0.05f)]
        [SerializeField] private float tickIntervalSeconds = 0.25f;

        /// <summary>씬에 하나만 둔다.</summary>
        public static RecoveryService Instance { get; private set; }

        /// <summary>회복소 규칙 전체. 씬에 컴포넌트가 없거나 밸런스 값이 잘못됐으면 null이다 -
        /// 호출부는 null을 확인하고 회복 UI를 열지 않는다.</summary>
        public static RecoveryStation Station => Instance != null ? Instance.station : null;

        /// <summary>슬롯 구성/상태가 바뀌었다. 회복소 패널이 이 신호로 전체를 다시 그린다.</summary>
        public static event Action SlotsChanged;

        /// <summary>회복 중인 캐릭터의 행동력이 한 단계 올랐다. 인자는 (캐릭터, 현재, 최대).</summary>
        public static event Action<CharacterDefinition, int, int> StaminaStepChanged;

        /// <summary>슬롯 하나의 회복이 끝났다(Recovering → RecoveryComplete). 인자는 (슬롯 번호, 캐릭터).
        /// 같은 시점에 여러 슬롯이 끝나면 (완료 시각, 슬롯 번호) 오름차순으로 발생한다.</summary>
        public static event Action<int, CharacterDefinition> RecoveryCompleted;

        private RecoveryStation station;
        private CharacterRosterRecoveryAdapter recoveryRoster;
        private PassiveStaminaRecoveryService passiveStaminaRecovery;
        private float tickTimer;

        /// <summary>
        /// 이 캐릭터가 회복 슬롯에 들어 있는지(Recovering 또는 RecoveryComplete).
        /// CharacterRoster가 캐릭터 교체와 외부 행동력 변경, 그리고 <b>시작 캐릭터 선택</b>을 막을 때
        /// 보는 판정이다.
        ///
        /// <b>회복소가 없어도 저장 데이터로 답한다.</b> 이 질문이 던져지는 시점은 세 가지인데,
        /// 그중 둘은 회복소가 아직/영영 없는 상황이다.
        ///   - CharacterRoster.Awake: RecoveryService보다 먼저 실행되므로 Station이 아직 null이다.
        ///   - 밸런스 에셋 누락/값 오류: RecoveryService가 스스로 비활성화해 Station이 계속 null이다.
        ///   - 정상 실행 중: Station이 있다.
        /// 이 셋이 서로 다른 답을 내면 "회복 중인데 전투에 투입되는" 상태가 만들어진다. 그래서 두 경로
        /// 모두 결국 같은 근거(저장된 recoverySlots)를 보도록 <see cref="RecoveryStation.IndexOfSavedSlot"/>
        /// 하나로 모았다.
        ///
        /// 회복 기록이 아예 없는 저장 파일(회복소를 쓰지 않는 구성)에서는 언제나 false이므로 기존
        /// 동작을 막지 않는다.
        /// </summary>
        public static bool IsCharacterInRecovery(CharacterDefinition definition)
        {
            if (definition == null) return false;

            RecoveryStation current = Station;
            if (current != null) return current.IsInRecoverySlot(definition);

            // 회복소가 없을 때의 폴백. Station이 있을 때와 같은 정적 판정을 쓰므로 답이 갈리지 않는다.
            return RecoveryStation.IsCharacterIdInSavedSlot(SaveSystem.Data, definition.CharacterId);
        }

        /// <summary>
        /// 보유 캐릭터가 외부 저장 트랜잭션으로 달라졌음을 회복소 UI에 알린다. 슬롯 기록은 바꾸지 않고,
        /// 열려 있는 선택 목록만 다시 읽게 하므로 저장이나 회복 진행을 추가로 발생시키지 않는다.
        /// </summary>
        public static void NotifyRosterChangedAfterExternalSave()
        {
            SlotsChanged?.Invoke();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[RecoveryService] 씬에 RecoveryService가 이미 있습니다. 이 인스턴스는 무시합니다.", this);
                enabled = false;
                return;
            }
            Instance = this;
        }

        private void OnEnable()
        {
            // CharacterRoster/InventoryManager는 자기 Awake에서 Instance를 세운다. 실행 순서가
            // 보장되도록 이 컴포넌트는 DefaultExecutionOrder를 뒤로 미뤄 두었다.
            if (!TryBuildStation()) enabled = false;
        }

        private void OnDisable()
        {
            DetachStation();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            if (station == null) return;

            // 진행은 UTC 시각으로만 계산하므로 매 프레임 확인할 필요가 없다. 확인 간격이 길어져도
            // 회복 속도는 달라지지 않고, 앱이 꺼져 있던 시간도 그대로 반영된다.
            tickTimer += Time.unscaledDeltaTime;
            if (tickTimer < tickIntervalSeconds) return;
            tickTimer = 0f;

            station.Tick();
            passiveStaminaRecovery?.Tick();
        }

        private bool TryBuildStation()
        {
            if (station != null) return true;

            if (balanceTable == null)
            {
                Debug.LogError("[RecoveryService] Balance Table이 비어 있어 회복소를 시작할 수 없습니다 - " +
                               "Recovery Balance Table 에셋을 연결하세요.", this);
                return false;
            }

            RecoveryBalance balance = balanceTable.ToBalance();
            if (!balance.IsValid)
            {
                Debug.LogError($"[RecoveryService] Balance Table('{balanceTable.name}')의 값이 잘못돼 회복소를 " +
                               $"시작하지 않습니다 - {balance.DescribeInvalid()}", this);
                return false;
            }

            CharacterRoster roster = CharacterRoster.Instance;
            if (roster == null)
            {
                Debug.LogError("[RecoveryService] 씬에 CharacterRoster가 없어 회복소를 시작할 수 없습니다.", this);
                return false;
            }

            InventoryManager inventory = InventoryManager.Instance;
            if (inventory == null)
            {
                Debug.LogError("[RecoveryService] 씬에 InventoryManager가 없어 회복 비용을 지불할 수 없습니다.", this);
                return false;
            }

            recoveryRoster = new CharacterRosterRecoveryAdapter(roster);
            station = new RecoveryStation(
                balance,
                recoveryRoster,
                new InventoryRecoveryWallet(inventory, balance.CurrencyId),
                () => SaveSystem.Data,
                SaveSystem.Save,
                () => DateTime.UtcNow);

            passiveStaminaRecovery = new PassiveStaminaRecoveryService(
                balance,
                recoveryRoster,
                () => SaveSystem.Data,
                SaveSystem.Save,
                () => DateTime.UtcNow);

            station.SlotsChanged += HandleSlotsChanged;
            station.StaminaStepChanged += HandleStaminaStepChanged;
            station.RecoveryCompleted += HandleRecoveryCompleted;
            passiveStaminaRecovery.StaminaChanged += HandlePassiveStaminaChanged;

            // 앱을 꺼 둔 동안 흐른 시간을 켜자마자 반영한다 - 첫 Tick을 기다리지 않는다.
            // 회복소를 먼저 처리해 슬롯 상태를 확정한 뒤 자연 회복을 실행하므로 같은 캐릭터가
            // 두 경로에서 회복되지 않는다.
            station.Tick();
            passiveStaminaRecovery.Tick();
            return true;
        }

        private void DetachStation()
        {
            if (station == null) return;

            station.SlotsChanged -= HandleSlotsChanged;
            station.StaminaStepChanged -= HandleStaminaStepChanged;
            station.RecoveryCompleted -= HandleRecoveryCompleted;
            if (passiveStaminaRecovery != null) passiveStaminaRecovery.StaminaChanged -= HandlePassiveStaminaChanged;
            station = null;
            passiveStaminaRecovery = null;
            recoveryRoster = null;
        }

        private void HandleSlotsChanged()
        {
            SlotsChanged?.Invoke();
        }

        private void HandleStaminaStepChanged(CharacterDefinition character, int current, int max)
        {
            StaminaStepChanged?.Invoke(character, current, max);
        }

        private void HandleRecoveryCompleted(int slotIndex, CharacterDefinition character)
        {
            RecoveryCompleted?.Invoke(slotIndex, character);
        }

        private void HandlePassiveStaminaChanged(CharacterDefinition character)
        {
            if (recoveryRoster == null || character == null) return;
            // 자연 회복도 회복소와 같은 CharacterStateChanged 경로를 써야 HUD, 교체 목록,
            // 회복소 목록, 명부가 각자 별도 연결 없이 즉시 다시 그려진다.
            recoveryRoster.RaiseCharacterStateChanged(character);
            StaminaStepChanged?.Invoke(character, recoveryRoster.GetStamina(character), recoveryRoster.GetMaxStamina(character));
        }
    }
}
