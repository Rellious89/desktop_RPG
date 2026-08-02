using Character;
using Common;
using Dungeon;
using Enemy;
using Recovery;
using UnityEngine;

namespace Field
{
    /// <summary>
    /// <see cref="FieldModeManager"/>가 "지금 어디인가"를 바꾸면, 그 말대로 <b>런타임을 실제로 그 모습으로
    /// 만드는</b> 단 하나의 구독자. 모드 판정은 전부 매니저가 소유하고 이 컴포넌트는 판정하지 않는다 -
    /// 여기서 하는 일은 "마을이면 이렇게, 던전이면 이렇게"라는 <b>순서가 고정된 적용</b>뿐이다.
    ///
    /// <b>연결은 전부 Inspector 명시 참조다.</b> Find/이름 탐색/GetChild/암묵적 싱글턴 조회를 하나도
    /// 쓰지 않는다 - 씬 계층이 바뀌어도 이 코드가 조용히 다른 오브젝트를 잡는 경로가 없어야 하고,
    /// 무엇이 빠졌는지는 실행 즉시 로그로 드러나야 하기 때문이다. 필수 참조가 하나라도 비어 있으면
    /// <b>전투를 열지 않는 쪽으로</b> 실패한다(fail closed).
    ///
    /// <b>마을 적용 순서</b>(공격이 성립할 수 있는 것부터 닫는다):
    ///   1. 플레이어 전투/입력을 <b>동기적으로</b> 끈다 - 진행 중이던 공격/충전/복귀/발사체/이동과
    ///      남은 입력이 그 자리에서 전부 취소된다.
    ///   2. 몬스터 대기열을 접는다 - Current/Standby가 이벤트 없이 정리되고 화면에서 사라진다.
    ///   3. 콤보를 끊는다(타임아웃으로 끊긴 것과 같은 경로).
    ///   4. 회복소 입구 버튼을 보이게 한다.
    ///
    /// <b>던전 적용 순서</b>(성공이 확인되기 전에는 절대 열지 않는다):
    ///   1. 전투/입력은 계속 꺼둔 채로 시작한다.
    ///   2. 남아 있던 이전 전투를 먼저 접는다.
    ///   3. 열려 있는 회복소 패널을 닫고 입구 버튼을 숨긴다.
    ///   4. 선택한 던전의 몬스터로 대기열을 연다.
    ///   5. <b>대기열이 성공했고, 매니저가 여전히 그 던전을 가리킬 때만</b> 전투를 켠다.
    ///
    /// <b>초기 동기화는 첫 Update보다 먼저 끝난다.</b> 플레이어 입력은 Update에서 읽히고 대기열은
    /// Start에서 슬롯을 잡으므로, 이 컴포넌트는 OnEnable에서 전투를 먼저 끄고(입력 경로 차단) Start에서
    /// 나머지 상태를 맞춘다 - 대기열이 우리보다 먼저 Start를 지나든 나중에 지나든 결과가 같다.
    ///
    /// <b>껐다 켜는 것도 전환의 일부다.</b> 꺼져 있는 동안에는 <see cref="FieldModeManager.FieldModeChanged"/>를
    /// 받지 못하므로, 다시 켜질 때 구독을 건 뒤 매니저가 <b>지금</b> 말하는 상태를 그대로 다시 적용한다 -
    /// 그러지 않으면 던전인데도 전투가 영영 닫힌 채로 남고 대기열/회복소가 옛 모드로 굳는다. 반대로
    /// 꺼질 때는 구독을 짝지어 해제하고 전투를 반드시 닫는다 - 따라갈 수 없는 컨트롤러가 던전 입력을
    /// 열어둔 채로 사라지지 않게 하기 위함이다.
    ///
    /// <b>여기서 하지 않는 일</b>: 저장/로드, 전환 연출, 필드 표시 UI, NPC/순찰/월드 진행, 입장 비용/
    /// 클리어/보스/레이드. 인벤토리와 캐릭터 교체는 어느 모드에서도 건드리지 않는다(항상 그대로 쓸 수 있다).
    /// </summary>
    [DisallowMultipleComponent]
    public class FieldModeRuntimeController : MonoBehaviour
    {
        [Header("Field Mode (필수)")]
        [Tooltip("모드 전환의 단일 소유자. 이 컴포넌트는 여기서 나오는 FieldModeChanged만 듣고 따라간다.")]
        [SerializeField] private FieldModeManager fieldModeManager;

        [Header("Player (필수)")]
        [Tooltip("전투/입력을 켜고 끌 플레이어 애니메이터(PlayerCharacter). 하나의 런타임 액터가 프로필만 " +
                 "갈아끼우는 구조라 캐릭터를 바꿔도 이 참조는 그대로 유효하다.")]
        [SerializeField] private PlayerCharacterAnimator playerAnimator;

        [Header("Encounter (필수)")]
        [Tooltip("몬스터 대기열. 마을에서는 접고(Suspend), 던전에서는 그 던전의 몬스터로 다시 연다.")]
        [SerializeField] private MonsterEncounterQueue encounterQueue;

        [Header("Recovery Station (선택)")]
        [Tooltip("회복소 패널(pn_RecoveryStation). 던전에 들어갈 때 열려 있으면 닫는다 - 비워두면 닫지 않는다.")]
        [SerializeField] private RecoveryStationPanel recoveryStationPanel;

        [Tooltip("회복소 입구 버튼(btn_RecoveryStation) 오브젝트. 마을에서 보이고 던전에서 숨는다 - " +
                 "이름으로 찾지 않으므로 반드시 직접 연결한다. 비워두면 버튼을 숨기지 못한다는 경고만 남는다.")]
        [SerializeField] private GameObject recoveryStationButton;

        /// <summary>가장 마지막으로 <b>적용을 끝낸</b> 모드. 아직 한 번도 적용하지 않았어도 마을이다
        /// (매니저의 시작값과 같다).</summary>
        public FieldMode AppliedMode { get; private set; } = FieldMode.Town;

        /// <summary>가장 마지막으로 적용한 던전. 마을이거나 던전 준비에 실패했으면 null이다.</summary>
        public DungeonDefinition AppliedDungeon { get; private set; }

        /// <summary>지금 이 컨트롤러가 전투를 열어 둔 상태인지(읽기 전용 런타임 상태). 던전 준비가
        /// 실패하면 던전 모드여도 false다 - "던전에 있다"와 "전투가 열렸다"는 다른 사실이다.</summary>
        public bool CombatEnabled { get; private set; }

        /// <summary>필수 참조가 전부 연결돼 있는지. false면 어떤 모드에서도 전투를 열지 않는다.</summary>
        public bool HasRequiredReferences =>
            fieldModeManager != null && playerAnimator != null && encounterQueue != null;

        private bool subscribed;

        /// <summary>최초 동기화(<see cref="Start"/>)를 이미 지났는지. 이 값이 켜진 뒤의 활성화는
        /// "런타임 중에 껐다 켠 것"이므로, 꺼져 있는 동안 놓친 전환이 있을 수 있어 매니저의 현재 상태를
        /// 다시 적용해야 한다. 참조 검증에 실패했더라도 켜둔다 - 재적용 경로가 스스로 참조를 다시 보고
        /// 판단하므로, 여기서 막으면 나중에 참조가 채워져도 영영 따라가지 못한다.</summary>
        private bool startCompleted;

        /// <summary>
        /// <b>입력 차단부터</b> 하고 구독을 건다. 플레이어 입력은 Update에서 읽히고 OnEnable은 어떤
        /// Update보다도 먼저 실행되므로, 이 시점에 전투를 꺼두면 "첫 프레임에 한 대 때리는" 경로가 아예
        /// 생기지 않는다. 다시 여는 것은 아래 재적용(또는 최초 활성화라면 <see cref="Start"/>)만이 한다.
        ///
        /// <b>최초 활성화와 재활성화는 다르다.</b> 최초에는 몬스터 슬롯의 Awake가 전부 끝난 뒤여야
        /// 대기열을 건드릴 수 있어서 동기화를 <see cref="Start"/>로 미룬다. 그 뒤에 껐다 켜는 경우에는
        /// 꺼져 있는 동안 <see cref="FieldModeManager.FieldModeChanged"/>를 놓쳤을 수 있으므로,
        /// 구독을 다시 건 <b>다음</b> 매니저가 지금 말하는 상태를 그대로 다시 적용한다 - 그러지 않으면
        /// 던전인데도 전투가 영영 닫힌 채로 남고, 대기열/회복소도 옛 모드 그대로 굳는다.
        ///
        /// 지웠다 다시 구독한다 - 오브젝트가 여러 번 켜졌다 꺼져도 핸들러가 두 번 붙지 않는다.
        /// </summary>
        private void OnEnable()
        {
            SetCombat(false);
            Subscribe();

            if (!startCompleted) return; // 최초 활성화 - 나머지는 Start가 맡는다.

            // 참조가 빠져 있으면 재적용 자체가 불가능하다. 전투는 방금 닫았으므로 그대로 두는 것이
            // fail closed다(무엇이 빠졌는지는 Start가 이미 남겼다).
            if (!HasRequiredReferences) return;

            Apply(fieldModeManager.CurrentMode, fieldModeManager.CurrentDungeon);
        }

        /// <summary>구독을 짝지어 해제하고, <b>마지막에 전투를 닫는다</b>. 꺼진 컨트롤러는 어떤 전환도
        /// 따라갈 수 없으므로, 던전에서 꺼졌다고 해서 입력이 열린 채로 남으면 안 된다(fail closed).
        /// 여기서 나가는 것은 공격 세션을 닫는 신호뿐이라 처치/보상 이벤트는 하나도 생기지 않는다.</summary>
        private void OnDisable()
        {
            Unsubscribe();
            SetCombat(false);
        }

        private void Subscribe()
        {
            if (fieldModeManager == null) return;

            fieldModeManager.FieldModeChanged -= HandleFieldModeChanged;
            fieldModeManager.FieldModeChanged += HandleFieldModeChanged;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed) return;

            subscribed = false;
            if (fieldModeManager != null) fieldModeManager.FieldModeChanged -= HandleFieldModeChanged;
        }

        /// <summary>초기 상태를 매니저가 말하는 그대로 한 번 적용한다. 대기열은 Start에서 슬롯을 잡는데
        /// 컴포넌트 간 Start 순서는 보장되지 않으므로, 접기(Suspend)는 순서와 무관하게 같은 결과가 되도록
        /// 대기열 쪽이 접힘 플래그로 소유권 획득 자체를 막는다.</summary>
        private void Start()
        {
            // 검증 결과와 무관하게 "최초 동기화 지점을 지났다"는 사실 자체는 확정한다 - 이후의
            // 재활성화는 전부 재적용 경로를 타야 한다.
            startCompleted = true;

            if (!ValidateReferences())
            {
                // 실패해도 전투는 이미 꺼져 있다(OnEnable). 여기서 더 열지 않는 것이 fail closed다.
                return;
            }

            Apply(fieldModeManager.CurrentMode, fieldModeManager.CurrentDungeon);
        }

        /// <summary>필수/선택 참조를 한 번에 점검한다 - 무엇이 빠졌는지 로그 하나로 알 수 있어야 한다.</summary>
        /// <returns>필수 참조가 전부 있으면 true.</returns>
        private bool ValidateReferences()
        {
            if (fieldModeManager == null)
            {
                Debug.LogError($"[FieldModeRuntimeController] '{name}': Field Mode Manager가 연결되지 않았습니다 - " +
                               "모드 전환을 따라갈 수 없어 전투를 열지 않습니다.", this);
            }
            if (playerAnimator == null)
            {
                Debug.LogError($"[FieldModeRuntimeController] '{name}': Player Animator가 연결되지 않았습니다 - " +
                               "전투/입력을 제어할 수 없습니다(마을에서도 공격이 막히지 않습니다).", this);
            }
            if (encounterQueue == null)
            {
                Debug.LogError($"[FieldModeRuntimeController] '{name}': Monster Encounter Queue가 연결되지 " +
                               "않았습니다 - 몬스터를 정리하거나 던전 전투를 열 수 없어 전투를 열지 않습니다.", this);
            }
            if (recoveryStationButton == null)
            {
                Debug.LogWarning($"[FieldModeRuntimeController] '{name}': 회복소 입구 버튼이 연결되지 않아 " +
                                 "던전에서 버튼을 숨기지 못합니다 - Inspector에서 btn_RecoveryStation을 연결하세요.", this);
            }
            if (recoveryStationPanel == null)
            {
                Debug.LogWarning($"[FieldModeRuntimeController] '{name}': 회복소 패널이 연결되지 않아 던전 입장 " +
                                 "시 열려 있는 회복소를 닫지 못합니다 - Inspector에서 pn_RecoveryStation을 연결하세요.", this);
            }

            return HasRequiredReferences;
        }

        private void HandleFieldModeChanged(FieldMode mode, DungeonDefinition dungeon)
        {
            if (!HasRequiredReferences)
            {
                // Start가 이미 무엇이 빠졌는지 남겼다 - 전환 때마다 같은 로그를 쏟지 않고, 전투만
                // 확실히 닫아둔다.
                SetCombat(false);
                return;
            }

            Apply(mode, dungeon);
        }

        /// <summary>모드 하나를 실제 런타임 상태로 만든다 - 진입점이 초기 동기화든 전환 이벤트든
        /// 언제나 같은 순서를 지나게 하기 위해 경로를 하나로 모아둔다.</summary>
        private void Apply(FieldMode mode, DungeonDefinition dungeon)
        {
            if (mode == FieldMode.Dungeon) ApplyDungeon(dungeon);
            else ApplyTown();
        }

        /// <summary>마을. 공격이 성립할 수 있는 것부터 닫고, 마지막에 회복소를 열어준다.
        /// <b>어떤 처치/보상 이벤트도 만들지 않는다</b> - 몬스터 정리는 전부 이벤트 없는 경로다.</summary>
        private void ApplyTown()
        {
            // 1. 입력과 진행 중이던 공격을 그 자리에서 끊는다(발사체/이동/충전/대기열 입력 포함).
            SetCombat(false);

            // 2. 몬스터를 이벤트 없이 정리한다 - Current도 Standby도 남지 않는다.
            encounterQueue.Suspend();

            // 3. 타격 흐름이 끊겼으므로 콤보도 함께 끊는다(타임아웃과 같은 경로라 표시도 평소와 같다).
            ComboManager.ResetCombo();

            // 4. 마을에서는 회복소를 쓸 수 있어야 한다(패널은 사용자가 직접 연다 - 여기서 열지 않는다).
            SetRecoveryButtonVisible(true);

            AppliedMode = FieldMode.Town;
            AppliedDungeon = null;
        }

        /// <summary>던전. <b>준비가 성공했고 매니저가 여전히 그 던전을 가리킬 때만</b> 전투를 연다 -
        /// 실패하면 던전 모드인 채로 전투가 닫힌 상태가 되고, 무엇이 잘못됐는지는 로그가 남긴다.</summary>
        private void ApplyDungeon(DungeonDefinition dungeon)
        {
            // 1. 준비가 끝날 때까지 전투/입력은 닫아둔다.
            SetCombat(false);

            AppliedMode = FieldMode.Dungeon;
            AppliedDungeon = null;

            if (dungeon == null)
            {
                Debug.LogError($"[FieldModeRuntimeController] '{name}': 던전 모드인데 던전이 비어 있습니다 - " +
                               "몬스터를 세우지 않고 전투를 닫아 둡니다.", this);
                encounterQueue.Suspend();
                SetRecoveryButtonVisible(false);
                return;
            }

            // 2. 이전 전투가 남아 있으면 먼저 접는다 - 새 던전의 몬스터가 옛 Current 옆에 서는 일이 없다.
            encounterQueue.Suspend();

            // 3. 던전에서는 회복소를 쓸 수 없다. 열려 있으면 닫고 입구 버튼을 숨긴다.
            CloseRecoveryStation();

            // 4. 이 던전의 몬스터로 대기열을 연다(실패하면 접힌 상태 그대로 false).
            if (!encounterQueue.TryStartDungeonEncounter(dungeon.Monsters))
            {
                Debug.LogError($"[FieldModeRuntimeController] '{name}': 던전 '{dungeon.DungeonId}'의 몬스터 준비에 " +
                               "실패해 전투를 열지 않습니다 - 던전의 Monsters와 각 몬스터의 Motion Profile을 확인하세요.", this);
                return;
            }

            // 5. 준비가 끝난 지금도 매니저가 같은 던전을 가리키는지 다시 확인한다 - 준비 도중에 상태가
            //    달라졌다면(구독자 예외/재진입 등) 그 던전의 전투를 여는 것은 틀린 일이다. 입력만 닫고
            //    끝내면 방금 세운 Current가 그대로 서 있게 되므로, 만들어 둔 전투를 여기서 되돌린다.
            if (fieldModeManager.CurrentMode != FieldMode.Dungeon || fieldModeManager.CurrentDungeon != dungeon)
            {
                Debug.LogWarning($"[FieldModeRuntimeController] '{name}': 던전 '{dungeon.DungeonId}' 준비를 마쳤지만 " +
                                 "그 사이 필드 모드가 바뀌어 방금 만든 전투를 접고 전투를 열지 않습니다.", this);
                encounterQueue.Suspend(); // 이벤트 없음: Current/Standby가 남지 않는다.
                return;
            }

            AppliedDungeon = dungeon;
            SetCombat(true);
        }

        /// <summary>플레이어 전투/입력 상태를 바꾸고 그 사실을 읽기 전용 상태로도 남긴다.</summary>
        private void SetCombat(bool value)
        {
            CombatEnabled = value;
            if (playerAnimator != null) playerAnimator.SetCombatEnabled(value);
        }

        /// <summary>열려 있는 회복소 패널을 닫고 입구 버튼을 숨긴다 - 이미 닫혀 있으면 다시 닫지 않는다
        /// (기존 공개 API인 <see cref="ModalPanel.Close"/>만 쓰고 패널 내부 규칙은 건드리지 않는다).</summary>
        private void CloseRecoveryStation()
        {
            if (recoveryStationPanel != null && recoveryStationPanel.gameObject.activeSelf)
            {
                recoveryStationPanel.Close();
            }

            SetRecoveryButtonVisible(false);
        }

        private void SetRecoveryButtonVisible(bool visible)
        {
            if (recoveryStationButton == null) return;
            if (recoveryStationButton.activeSelf == visible) return;

            recoveryStationButton.SetActive(visible);
        }

        /// <summary>마을로 돌아가기를 요청한다 - 판정과 이벤트 발행은 전부
        /// <see cref="FieldModeManager.TryReturnToTown"/>이 하고, 여기서는 그대로 전달만 한다.
        /// 마을 복귀 UI가 생기기 전까지 런타임에서 이 흐름을 부를 수 있는 공개 통로다.</summary>
        /// <returns>실제로 마을로 돌아갔으면 true. 거부되면 아무것도 바뀌지 않는다.</returns>
        public bool TryReturnToTown()
        {
            if (fieldModeManager == null)
            {
                Debug.LogError($"[FieldModeRuntimeController] '{name}': Field Mode Manager가 없어 마을 복귀 요청을 " +
                               "전달할 수 없습니다.", this);
                return false;
            }

            return fieldModeManager.TryReturnToTown();
        }

        /// <summary>Play Mode에서 마을 복귀 전체 경로를 확인하는 테스트 진입점 - 영구 UI를 만들지 않기 위한
        /// 임시 통로다(컴포넌트 우클릭 메뉴).</summary>
        [ContextMenu("Test/Return To Town")]
        private void TestReturnToTown()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning($"[FieldModeRuntimeController] '{name}': 마을 복귀는 Play Mode에서만 확인할 수 " +
                                 "있습니다.", this);
                return;
            }

            Debug.Log($"[FieldModeRuntimeController] '{name}': 테스트 마을 복귀 요청 결과 = {TryReturnToTown()}", this);
        }
    }
}
