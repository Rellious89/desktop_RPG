using System;
using System.Collections.Generic;
using Common;
using Dungeon;
using UnityEngine;

namespace Enemy
{
    /// <summary>
    /// 몬스터 대기열. 씬에 놓인 <b>정확히 두 개</b>의 몬스터 슬롯(물리 슬롯)을 소유하고, 그 둘을
    /// Current(전투 중) / Standby(대기) / Exiting(퇴장 중) 역할로 번갈아 돌려쓴다. 몬스터를 새로
    /// 생성하거나 파괴하지 않으며, 프로필만 갈아끼워 "다음 몬스터"를 표현한다.
    ///
    /// <b>역할은 알파나 위치가 아니라 <see cref="TargetEngagementRole"/> 하나로만 표현된다.</b> 대기 중인
    /// 몬스터는 살아 있고(IsDefeated == false) 화면에도 흐리게 보이지만, Target의 공격 가능 레지스트리에
    /// 포함되지 않고(HasAttackableTarget이 세지 않는다) TargetCombatController의 역할 게이트가 정적
    /// HitPoint도 전부 무시한다 - 어떤 순간에도 공격 가능한 몬스터는 최대 하나다.
    ///
    /// <b>승격은 반드시 LateUpdate에서만 한다.</b> Target의 처치 판정은 플레이어의 HitPoint 디스패치
    /// 한복판에서 동기적으로 일어나므로(Strike -> HitPoint -> ApplyDamage -> Defeat -> OnDefeated),
    /// 그 콜스택 안에서 역할을 바꾸면 같은 타격 스냅샷의 남은 구독자들이 방금 승격된 몬스터를 그대로
    /// 때리게 된다. 그래서 처치 알림은 promotionPending 플래그만 세우고 실제 전환은 그 프레임의
    /// LateUpdate에서 한 번만 수행한다 - 다음 프레임의 정상 입력(Update) 전이므로 새 Current는 제때
    /// 공격 가능해진다.
    ///
    /// <b>2물리슬롯 제약과 빠른 연속 처치.</b> 정상 흐름은 "퇴장 연출이 끝난 슬롯을 다음 Standby로
    /// 재장전"이다. 하지만 퇴장 연출이 끝나기 전에 새 Current가 죽으면 승격할 Standby가 없다 - 이때는
    /// 퇴장 중이던 슬롯을 즉시 마감(finalize)해 Standby로 만든 뒤 새 처치분을 Exiting으로 교대시킨다.
    /// 슬롯 참조를 덮어써 잃어버리거나, 관리자가 놓친 몬스터가 처치 상태로 영원히 고착되는 경로가 없도록
    /// 순서를 이 하나로 고정했다(퇴장 연출이 짧아지는 것은 슬롯이 둘뿐인 데서 오는 불가피한 tradeoff라
    /// 경고를 한 번 남긴다).
    ///
    /// 구성 오류(슬롯 미연결/중복 연결/재생 불가 프로필)에는 오류를 남기고 <b>이 관리자만</b>
    /// 비활성화한다 - 몬스터를 대기열 대상으로 편입시키는 것은 검증을 전부 통과한 뒤이므로, 실패하면
    /// 두 몬스터 모두 기존 standalone 동작(자체 리스폰, Current 역할)을 그대로 유지한다.
    /// </summary>
    [DisallowMultipleComponent]
    public class MonsterEncounterQueue : MonoBehaviour
    {
        [Header("Encounter Slots (정확히 두 개)")]
        [Tooltip("시작 시 Current(전투 중)가 되는 몬스터.")]
        [SerializeField] private TargetCombatController firstSlot;

        [Tooltip("시작 시 Standby(대기)가 되는 몬스터. First Slot과 같은 오브젝트를 넣으면 구성 오류다.")]
        [SerializeField] private TargetCombatController secondSlot;

        [Header("Standby Presentation")]
        [Tooltip("대기 중인 몬스터의 알파 배율. 각 Renderer의 '원래 알파 x 이 값'으로 매번 절대 계산하므로 " +
                 "몇 번을 대기/전투로 오가도 어두워지는 누적이 없다.")]
        [Range(0f, 1f)]
        [SerializeField] private float standbyAlpha = 0.1f;

        [Tooltip("전투 위치를 기준으로 한 대기 위치 오프셋(로컬 좌표). 예: (1.2, 0, 0)이면 전투 위치의 오른쪽.")]
        [SerializeField] private Vector3 standbyOffset = new Vector3(1.2f, 0f, 0f);

        [Header("Transition Timing")]
        [Tooltip("승격한 몬스터가 대기 위치에서 전투 위치까지 이동하는 시간(초). 0이면 즉시 이동한다. " +
                 "역할/공격 가능 여부는 이동과 무관하게 승격 즉시 켜진다.")]
        [Min(0f)]
        [SerializeField] private float promotionDuration = 0.25f;

        [Tooltip("켜면 퇴장 시간을 그 몬스터 Target의 Defeat Fade Duration으로 쓴다(처치 연출과 정확히 " +
                 "같은 길이). 끄면 아래 Exiting Duration을 쓰고 관리자가 직접 페이드를 재생한다.")]
        [SerializeField] private bool useTargetDefeatFadeDuration = true;

        [Tooltip("Use Target Defeat Fade Duration이 꺼져 있을 때 쓰는 퇴장 연출 시간(초).")]
        [Min(0f)]
        [SerializeField] private float exitingDuration = 0.35f;

        [Header("Combat Position (선택)")]
        [Tooltip("전투 위치 기준점. 비워두면 각 몬스터의 Combat Stage Layout + 프로필 Actor Offset으로 " +
                 "계산한 위치(그것도 없으면 씬에 배치된 최초 위치)를 쓴다 - 씬 연결 없이도 안전하게 동작한다.")]
        [SerializeField] private Transform combatPosition;

        [Header("Monster Profile Pool")]
        [Tooltip("재장전할 때 다음 몬스터로 쓸 프로필 목록(이 관리자가 소유한다). null/중복/재생 불가 " +
                 "항목은 자동으로 걸러낸다. 유효 후보가 하나뿐이면 그 프로필로 계속 반복하고, 하나도 없으면 " +
                 "경고를 남긴 뒤 각 슬롯의 현재 프로필을 그대로 유지한다(대기열은 정상 동작한다).")]
        [SerializeField] private MonsterMotionProfile[] profilePool;

        /// <summary>Standby가 Current로 승격된 직후 발생(LateUpdate).</summary>
        public event Action<TargetCombatController> CurrentPromoted;

        /// <summary>처치된 Current가 Exiting으로 내려간 직후 발생(LateUpdate).</summary>
        public event Action<TargetCombatController> ExitingStarted;

        /// <summary>퇴장이 끝난 슬롯이 다음 Standby로 재장전된 직후 발생.</summary>
        public event Action<TargetCombatController> StandbyRefilled;

        private TargetCombatController currentSlot;
        private TargetCombatController standbySlot;
        private TargetCombatController exitingSlot;

        private readonly List<MonsterMotionProfile> usableProfiles = new List<MonsterMotionProfile>();

        private bool configured;
        private bool subscribed;
        private bool started;

        // 필드 모드가 "지금은 전투가 없다"고 선언한 상태(마을). 소유권 획득 자체를 막으므로, 이 플래그가
        // 켜져 있는 동안에는 Start/OnEnable이 몇 번을 지나가도 슬롯이 되살아나지 않는다 - 컨트롤러의
        // 초기 동기화가 대기열의 Start보다 먼저 오든 나중에 오든 결과가 같다.
        private bool suspended;

        // 처치 알림(HitPoint 콜스택 안)에서 켜고 LateUpdate에서만 소비하는 예약 플래그.
        private bool promotionPending;

        private float exitStartTime;
        private float exitDuration;

        private bool warnedForcedExit;
        private bool warnedEmptyPool;
        private bool warnedEmptyStandby;

        // 소유권을 놓을 때 "동시에 둘 다 공격 가능"해지는 것을 막기 위해 우리가 비활성화한 슬롯.
        // 되살리는 일 자체는 TryConfigureSlots가 역할 배정을 마친 뒤에 하고(그래야 공격 가능 수가 잠깐도
        // 2가 되지 않는다), 이 참조는 "아직 우리가 꺼둔 채로 남은 슬롯이 있다"를 기억하기 위한 것이다 -
        // 획득에 성공한 뒤에만 비운다(검증이 실패하면 그 슬롯은 비활성 standalone 그대로 남는다).
        private TargetCombatController deactivatedByRelease;

        private static bool applicationQuitting;
        private static bool quitHookInstalled;

        public TargetCombatController CurrentMonster => currentSlot;
        public TargetCombatController StandbyMonster => standbySlot;
        public TargetCombatController ExitingMonster => exitingSlot;

        /// <summary>슬롯 검증을 통과해 실제로 대기열을 소유하고 있는지. false면 두 몬스터는 관리 대상이
        /// 아니며 기존 standalone 동작 그대로다.</summary>
        public bool IsConfigured => configured;

        /// <summary>유효 후보만 남긴 프로필 풀 크기(0이면 각 슬롯의 현재 프로필을 유지한다).</summary>
        public int UsableProfileCount => usableProfiles.Count;

        /// <summary>필드 모드가 대기열을 접어둔 상태인지(마을). true인 동안 Current/Standby는 모두
        /// null이고, 두 슬롯은 비공격 역할로 비활성화돼 있으며, LateUpdate는 아무 일도 하지 않는다.</summary>
        public bool IsSuspended => suspended;

        /// <summary>종료 중에는 다른 오브젝트를 건드리면 안 되므로 그 시점을 기억해둔다. Enter Play Mode
        /// 옵션으로 도메인 리로드를 껐을 때 정적 상태가 남아 있을 수 있어 플래그를 매 재생마다 초기화하고,
        /// 구독은 한 번만 건다.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            applicationQuitting = false;
            if (quitHookInstalled) return;

            quitHookInstalled = true;
            Application.quitting += () => applicationQuitting = true;
        }

        /// <summary>슬롯 구성은 Start에서 확정한다 - Awake 순서는 오브젝트마다 보장되지 않아서, 몬스터
        /// 쪽 Awake(프로필 검증 후 자기 비활성화 포함)가 전부 끝난 뒤에 판단해야 한다. Start는 첫
        /// Update보다 먼저 실행되므로, 플레이어가 첫 입력을 넣기 전에 역할 배정이 끝나 있다.</summary>
        private void Start()
        {
            started = true;
            AcquireOwnership();
        }

        private void OnEnable()
        {
            // 최초 활성화는 Start가 맡는다. 런타임에 껐다 켠 경우에만 여기서 소유권을 다시 잡는다.
            if (started) AcquireOwnership();
        }

        /// <summary>비활성화/파괴는 곧 소유권 포기다 - 관리 대상이던 몬스터를 처치 상태 그대로 두면
        /// 자체 리스폰도 없고 관리자도 없어 영원히 고착되므로, 여기서 반드시 정상 standalone 상태로
        /// 되돌린다(<see cref="ReleaseOwnership"/> 참고).</summary>
        private void OnDisable()
        {
            ReleaseOwnership();
        }

        private void OnDestroy()
        {
            ReleaseOwnership();
        }

#if UNITY_EDITOR
        /// <summary>Inspector 값 clamp와 구성 경고만 한다 - Edit Mode에서 씬의 Transform이나 색을
        /// 자동으로 바꾸거나 SetDirty를 호출하지 않는다(대기열 표현은 전부 런타임 동작이다).</summary>
        private void OnValidate()
        {
            standbyAlpha = Mathf.Clamp01(standbyAlpha);
            promotionDuration = Mathf.Max(0f, promotionDuration);
            exitingDuration = Mathf.Max(0f, exitingDuration);

            if (firstSlot != null && secondSlot != null && firstSlot == secondSlot)
            {
                Debug.LogWarning($"[MonsterEncounterQueue] '{name}': First Slot과 Second Slot에 같은 몬스터가 " +
                                 "연결돼 있습니다 - Play Mode에서 대기열이 비활성화됩니다.", this);
            }
        }
#endif

        // -------------------------------------------------------------------------------------
        // 소유권 획득/해제
        // -------------------------------------------------------------------------------------

        /// <summary>소유권을 잡는다. <b>여기서는 GameObject를 활성화하지 않는다</b> - 꺼둔 슬롯을 되살리는
        /// 일은 검증과 역할 배정을 모두 마친 뒤 <see cref="TryConfigureSlots"/> 안에서만 일어난다.
        /// 여기서 먼저 켜면 그 슬롯이 (해제 정책상) standalone Current 상태이므로 Target.OnEnable이
        /// 공격 가능 수를 +1 해버려, 역할이 배정되기 전까지 공격 가능 Target이 둘이 되는 구간이 생긴다.
        /// 검증이 실패하면 그 슬롯은 해제 때 그대로인 "비활성 standalone" 상태로 남고, 참조도 계속
        /// 들고 있다가 다음 성공적인 획득 때 되살린다.</summary>
        private void AcquireOwnership()
        {
            if (configured) return;

            // 마을(접힘) 상태에서는 어떤 경로로도 슬롯을 되살리지 않는다 - 던전 입장만이 이 상태를 푼다.
            if (suspended) return;

            if (!TryConfigureSlots()) return;

            deactivatedByRelease = null; // 성공적으로 소유권을 잡은 뒤에만 참조를 놓는다.
            Subscribe();
        }

        /// <summary>대기열 소유권을 놓고 두 슬롯을 안전한 standalone 상태로 되돌린다.
        ///
        /// <b>정책</b>: 살아남는 한 슬롯만 standalone Current(체력 만땅 + 원래 알파 + 전투 위치)로 되돌리고,
        /// 남는 슬롯은 <b>먼저 비활성화한 뒤</b> 같은 standalone 상태로 되돌린다 - 순서를 이렇게 고정해야
        /// "같은 순간 두 몬스터가 모두 공격 가능"해지는 구간이 아예 생기지 않는다(비활성 Target은
        /// 레지스트리에 등록되지 않는다). 되돌리는 데 쓰는 것은 전부 이벤트 없는 prepare 경로라
        /// AnyTargetDefeated/보상/경험치/킬카운트/행동력은 하나도 발생하지 않는다.
        ///
        /// 애플리케이션 종료나 씬 언로드 중에는 다른 오브젝트를 건드리지 않고 구독만 해제한다.</summary>
        public void ReleaseOwnership()
        {
            if (!configured) return;

            Unsubscribe();
            configured = false;
            promotionPending = false;

            if (applicationQuitting || !gameObject.scene.isLoaded)
            {
                currentSlot = null;
                standbySlot = null;
                exitingSlot = null;
                return;
            }

            TargetCombatController survivor = currentSlot != null ? currentSlot
                                            : standbySlot != null ? standbySlot
                                            : exitingSlot;
            TargetCombatController other = GetOtherSlot(survivor);

            currentSlot = null;
            standbySlot = null;
            exitingSlot = null;

            // 남는 슬롯 먼저: 비활성화 -> 되돌리기 순서라 attackable이 둘이 되는 순간이 없다.
            if (other != null)
            {
                other.gameObject.SetActive(false);
                deactivatedByRelease = other;
                RestoreToStandalone(other);
            }

            if (survivor != null) RestoreToStandalone(survivor);
        }

        /// <summary>슬롯 하나를 이벤트 없이 "살아 있는 standalone Current"로 되돌린다 - 처치 상태였다면
        /// 체력까지 복원되고, 알파와 위치도 전투 상태 기준으로 정리된다.</summary>
        private void RestoreToStandalone(TargetCombatController slot)
        {
            if (slot == null) return;

            slot.PrepareForEncounter(TargetEngagementRole.Current); // 이벤트 없음: 체력/처치 상태/포즈 복원
            slot.LeaveEncounter();                                  // 자체 리스폰(standalone) 소유권 반환
            slot.ApplyCurrentVisual();
            slot.SetPresentationBasePosition(ResolveCombatLocalPosition(slot));
        }

        // -------------------------------------------------------------------------------------
        // 필드 모드 전용 API - 접기(마을) / 선택 던전으로 열기
        //
        // 마을에서 <see cref="ReleaseOwnership"/>를 쓰지 않는 이유가 이 두 메서드가 따로 있는 이유다:
        // 해제는 "살아남는 한 슬롯을 standalone Current로 되돌리는" 정책이라, 마을에 공격 가능한
        // 몬스터가 한 마리 서 있게 된다. 마을에서 필요한 것은 정반대 - 공격 가능한 Target이 하나도
        // 없는 상태다.
        // -------------------------------------------------------------------------------------

        /// <summary>
        /// 대기열을 접는다(마을 전환/던전 재입장 전 정리). 접힌 뒤에는 <see cref="CurrentMonster"/>와
        /// <see cref="StandbyMonster"/>가 모두 null이고, 두 슬롯은 비공격 역할로 비활성화되며,
        /// <see cref="LateUpdate"/>는 아무 일도 하지 않는다 - 승격 예약도 퇴장 타이머도 남지 않는다.
        ///
        /// <b>처치/보상/킬카운트/경험치/재화/아이템/행동력 이벤트를 하나도 만들지 않는다.</b> 되돌리는 데
        /// 쓰는 것은 전부 이벤트 없는 prepare 경로(<see cref="TargetCombatController.PrepareForEncounter"/>)뿐이라,
        /// 처치 구독자 입장에서는 아무 일도 일어나지 않은 것과 같다.
        ///
        /// 순서를 지킨다: <b>구독 해제/상태 초기화 -> 비공격 역할 부여 -> 상태 복원 -> 비활성화</b>.
        /// 역할을 먼저 내려야 슬롯이 꺼지기 전 어느 순간에도 공격 가능한 Target이 남지 않는다.
        ///
        /// 몇 번을 다시 불러도 안전하고, 아직 소유권을 잡기 전(첫 Start 이전)에 불러도 안전하다.
        /// </summary>
        public void Suspend()
        {
            suspended = true;

            Unsubscribe();
            configured = false;
            promotionPending = false;

            currentSlot = null;
            standbySlot = null;
            exitingSlot = null;

            // 다음 던전이 자기 몬스터로 다시 채운다 - 접힌 상태에 이전 던전(또는 직렬화 풀)의 후보가
            // 남아 있지 않게 여기서 비운다.
            usableProfiles.Clear();

            // 종료/씬 언로드 중에는 다른 오브젝트를 건드리지 않는다(ReleaseOwnership과 같은 규칙).
            if (applicationQuitting || !gameObject.scene.isLoaded) return;

            SuspendSlot(firstSlot);
            SuspendSlot(secondSlot);
        }

        /// <summary>슬롯 하나를 "비공격 역할 + 이벤트 없는 초기 상태"로 되돌린 뒤 화면에서 감춘다.
        ///
        /// 아직 활성화된 적이 없는 슬롯에는 <see cref="TargetCombatController.PrepareForEncounter"/>를
        /// 호출하지 않는다 - 그 경로는 Idle 애니메이션 상태를 되돌리는데, Awake가 한 번도 돌지 않은
        /// 오브젝트에는 되돌릴 애니메이션 자체가 없기 때문이다. 그런 슬롯은 이미 화면에 없고 Target
        /// 레지스트리에도 등록되지 않았으므로 역할/수명 지정만으로 충분하다.</summary>
        private void SuspendSlot(TargetCombatController slot)
        {
            if (slot == null) return;

            // 자체 리스폰 소유권을 회수하고(진행 중이던 리스폰 코루틴이 멈춘다) 비공격 역할로 내린다.
            slot.JoinEncounter(TargetEngagementRole.Standby);

            // 이벤트 없음: 체력/처치 상태/진행 중인 Fade/흔들림/피격 자세까지 한 번에 초기화된다.
            if (slot.gameObject.activeInHierarchy) slot.PrepareForEncounter(TargetEngagementRole.Standby);

            slot.gameObject.SetActive(false);
        }

        /// <summary>
        /// 선택한 던전의 몬스터로 대기열을 새로 연다(<see cref="DungeonDefinition.Monsters"/>를 그대로
        /// 넘기면 된다). 성공하면 접힘 상태가 풀리고 <b>정확히 하나</b>의 Current와 하나의 Standby가
        /// 만들어지며, 그 뒤의 승격/재장전/중복 보상 방지는 전부 기존 대기열 규칙 그대로다.
        ///
        /// <b>런타임 풀은 이 던전의 몬스터로만 만든다.</b> 직렬화된 Profile Pool은 보지도 않고 섞이지도
        /// 않는다 - 후보는 넘어온 <see cref="MonsterDefinition.MotionProfile"/> 중 재생 가능한 것뿐이며,
        /// 유효 후보가 하나뿐이면 그 몬스터가 계속 반복 등장한다.
        ///
        /// <b>실패하면 아무것도 열지 않는다(fail closed).</b> 슬롯 구성이 잘못됐거나 유효한 몬스터가
        /// 하나도 없으면 오류를 남기고 <b>접힌 상태로</b> false를 돌려준다 - 공격 가능한 Target이 하나도
        /// 없으므로 전투가 시작될 수 없다. 이 보장은 <b>이 메서드가 직접</b> 만든다: 검증보다 먼저
        /// <see cref="Suspend"/>로 이전 전투를 접으므로, 호출자가 무엇을 먼저 했는지와 무관하게 실패한
        /// 호출은 언제나 Current/Standby 없는 접힘 상태를 남긴다(호출자 쪽 Suspend는 전환 순서를
        /// 문서화하는 의미로 남아 있어도 되며, 두 번 접어도 결과는 같다).
        ///
        /// 순서를 지킨다: <b>접기 -> 검증 -> 풀 구성 -> 두 슬롯 모두 비공격(Standby) -> 활성화 ->
        /// 프로필/상태 준비 -> 표현 -> 마지막에 하나만 Current</b>. 준비가 끝나기 전에는 어느 순간에도
        /// 공격 가능한 몬스터가 존재하지 않는다.
        /// </summary>
        /// <returns>대기열이 실제로 열렸으면 true.</returns>
        public bool TryStartDungeonEncounter(IReadOnlyList<MonsterDefinition> monsters)
        {
            // 무엇보다 먼저 접는다 - 이 아래의 어떤 실패도 "이전 전투가 그대로 살아 있는" 상태를 남길 수
            // 없게 만드는 유일한 근거다(구독/승격 예약/퇴장 타이머/이전 풀도 여기서 함께 정리된다).
            Suspend();

            if (!ValidateSlot(firstSlot, nameof(firstSlot)) || !ValidateSlot(secondSlot, nameof(secondSlot)))
            {
                return false;
            }
            if (firstSlot == secondSlot)
            {
                Debug.LogError($"[MonsterEncounterQueue] '{name}': First Slot과 Second Slot에 같은 몬스터가 " +
                               "연결돼 있습니다. 서로 다른 몬스터 두 개를 연결하세요 - 던전 전투를 시작하지 않습니다.", this);
                return false;
            }

            if (!TryBuildDungeonProfiles(monsters))
            {
                Debug.LogError($"[MonsterEncounterQueue] '{name}': 선택한 던전에 재생 가능한 Motion Profile을 가진 " +
                               "몬스터가 하나도 없습니다 - 대기열을 열지 않고 접힌 상태를 유지합니다(전투가 시작되지 않습니다).", this);
                return false;
            }

            // 여기서부터는 실패 경로가 없다 - 구독/승격 예약/퇴장 슬롯은 위의 Suspend가 이미 비웠으므로
            // 정리 소유자가 하나뿐이다(같은 일을 두 곳에서 하지 않는다).
            currentSlot = firstSlot;
            standbySlot = secondSlot;

            // 1) 두 슬롯 모두 비공격 역할로 먼저 내린다 - 아직 꺼져 있는 동안 확정하므로, 다음 단계에서
            //    켜지더라도 Target.OnEnable이 공격 가능 수를 올리지 않는다.
            currentSlot.JoinEncounter(TargetEngagementRole.Standby);
            standbySlot.JoinEncounter(TargetEngagementRole.Standby);

            // 2) 활성화. 아직 한 번도 켜진 적이 없는 슬롯이라면 여기서 Awake가 돌아 애니메이션 상태가
            //    만들어진다 - 그래서 프로필/상태 준비는 반드시 이 다음이다(기존 TryConfigureSlots가
            //    대기 슬롯을 다루는 순서와 같다).
            if (!currentSlot.gameObject.activeSelf) currentSlot.gameObject.SetActive(true);
            if (!standbySlot.gameObject.activeSelf) standbySlot.gameObject.SetActive(true);

            // 3) 프로필/체력/처치 상태/포즈를 이벤트 없이 준비한다. 유효 후보가 하나뿐이면 두 슬롯이
            //    같은 프로필을 쓴다(같은 몬스터가 반복 등장하는 정상 동작이다).
            MonsterMotionProfile currentProfile = PickNextProfile(null);
            currentSlot.PrepareForEncounter(TargetEngagementRole.Standby, currentProfile);
            ApplyCurrentPresentation(currentSlot, instant: true);

            MonsterMotionProfile standbyProfile = PickNextProfile(currentProfile);
            standbySlot.PrepareForEncounter(TargetEngagementRole.Standby, standbyProfile);
            ApplyStandbyPresentation(standbySlot);

            // 4) 준비가 전부 끝난 뒤에야 소유권을 확정하고 정확히 하나를 Current로 올린다.
            deactivatedByRelease = null;
            suspended = false;
            configured = true;
            Subscribe();

            currentSlot.SetEngagementRole(TargetEngagementRole.Current);
            return true;
        }

        /// <summary>넘어온 던전 몬스터에서 재생 가능한 모션 프로필만 골라 런타임 풀을 다시 만든다.
        /// null 정의/프로필 없음/중복/재생 불가는 걸러내고, 직렬화된 Profile Pool은 참조하지 않는다.</summary>
        /// <returns>유효 후보가 하나 이상 남았으면 true.</returns>
        private bool TryBuildDungeonProfiles(IReadOnlyList<MonsterDefinition> monsters)
        {
            usableProfiles.Clear();
            if (monsters == null) return false;

            for (int i = 0; i < monsters.Count; i++)
            {
                MonsterDefinition definition = monsters[i];
                if (definition == null) continue;

                MonsterMotionProfile profile = definition.MotionProfile;
                if (profile == null)
                {
                    Debug.LogWarning($"[MonsterEncounterQueue] '{name}': 몬스터 '{definition.MonsterId}'에 Motion " +
                                     "Profile이 없어 이번 던전 후보에서 제외합니다.", definition);
                    continue;
                }
                if (usableProfiles.Contains(profile)) continue; // 같은 프로필을 여러 몬스터가 써도 후보는 하나다.
                if (!TargetCombatController.IsProfileUsable(profile))
                {
                    Debug.LogWarning($"[MonsterEncounterQueue] '{name}': 몬스터 '{definition.MonsterId}'의 프로필 " +
                                     $"'{profile.name}'에 Base Idle/Hit 프레임이 없어 이번 던전 후보에서 제외합니다.", definition);
                    continue;
                }

                usableProfiles.Add(profile);
            }

            return usableProfiles.Count > 0;
        }

        // -------------------------------------------------------------------------------------
        // 구성/검증
        // -------------------------------------------------------------------------------------

        private bool TryConfigureSlots()
        {
            if (!ValidateSlot(firstSlot, nameof(firstSlot)) || !ValidateSlot(secondSlot, nameof(secondSlot)))
            {
                enabled = false;
                return false;
            }
            if (firstSlot == secondSlot)
            {
                Debug.LogError($"[MonsterEncounterQueue] '{name}': First Slot과 Second Slot에 같은 몬스터가 " +
                               "연결돼 있습니다. 서로 다른 몬스터 두 개를 연결하세요 - 대기열을 비활성화합니다.", this);
                enabled = false;
                return false;
            }

            BuildUsableProfiles();

            currentSlot = firstSlot;
            standbySlot = secondSlot;
            exitingSlot = null;

            // 1) 역할을 먼저 확정한다 - 반드시 "강등(Standby) 다음 승격(Current)" 순서다. 재획득처럼
            //    두 슬롯이 이미 standalone Current로 되돌아가 있는 상태에서 순서를 뒤집으면 잠깐이라도
            //    공격 가능 Target이 둘이 된다. 이 순서면 최악의 경우에도 잠깐 0이 될 뿐 절대 2가 되지
            //    않는다(0은 입력이 닫힐 뿐이라 안전하다). 비활성 슬롯에 역할을 지정하는 것도 안전하다 -
            //    Target은 등록되지 않은 동안 공격 가능 수에 아무 기여도 하지 않는다.
            standbySlot.JoinEncounter(TargetEngagementRole.Standby);
            currentSlot.JoinEncounter(TargetEngagementRole.Current);

            // 2) 역할이 확정된 뒤에야 GameObject를 켠다 - 대기열은 살아 있는 슬롯 두 개를 전제로 하므로,
            //    소유권을 놓을 때 우리가 꺼둔 슬롯이든 다른 이유로 꺼져 있던 슬롯이든 여기서 되살린다.
            //    이 시점의 Standby는 이미 Standby 역할이라 Target.OnEnable이 등록해도 공격 가능 수에는
            //    들어오지 않는다(활성화 순서와 무관하게 최대 1이 유지된다).
            if (!standbySlot.gameObject.activeSelf) standbySlot.gameObject.SetActive(true);
            if (!currentSlot.gameObject.activeSelf) currentSlot.gameObject.SetActive(true);

            // 3) 표현(알파/위치/프로필)은 활성화 이후에 적용한다 - 켜지는 순간 Awake가 돌면서 기준 위치와
            //    Idle 상태를 잡으므로, 그 뒤에 적용해야 값이 덮이지 않는다.
            ApplyCurrentPresentation(currentSlot, instant: true);

            // 대기 슬롯은 가능하면 Current와 다른 프로필로 세워둔다(유효 후보가 하나뿐이면 그대로 쓴다).
            MonsterMotionProfile standbyProfile = PickNextProfile(currentSlot.MotionProfile);
            if (standbyProfile != null && standbyProfile != standbySlot.MotionProfile)
            {
                standbySlot.PrepareForEncounter(TargetEngagementRole.Standby, standbyProfile); // 이벤트 없음
            }
            ApplyStandbyPresentation(standbySlot);

            configured = true;
            return true;
        }

        private bool ValidateSlot(TargetCombatController slot, string fieldName)
        {
            if (slot == null)
            {
                Debug.LogError($"[MonsterEncounterQueue] '{name}': {fieldName}이(가) 비어 있습니다. 몬스터 슬롯 " +
                               "두 개를 모두 연결하세요 - 대기열을 비활성화합니다(몬스터는 기존처럼 단독 동작합니다).", this);
                return false;
            }
            if (slot.CombatTarget == null)
            {
                Debug.LogError($"[MonsterEncounterQueue] '{name}': {fieldName}의 '{slot.name}'에 Target 컴포넌트가 " +
                               "없습니다 - 대기열을 비활성화합니다.", this);
                return false;
            }
            if (!slot.HasPlayableMotionProfile)
            {
                Debug.LogError($"[MonsterEncounterQueue] '{name}': {fieldName}의 '{slot.name}'에 재생 가능한 Monster " +
                               "Motion Profile이 없습니다(Base Idle/Hit 프레임 필요) - 대기열을 비활성화합니다.", this);
                return false;
            }
            return true;
        }

        /// <summary>프로필 풀에서 null/중복/재생 불가 후보를 걸러낸다. 유효 후보가 하나도 없으면 경고를
        /// 한 번만 남기고(매 재장전마다 로그를 쏟지 않는다) 각 슬롯의 현재 프로필을 그대로 쓴다 -
        /// 대기열 자체는 정상 동작한다.</summary>
        private void BuildUsableProfiles()
        {
            usableProfiles.Clear();

            int rejected = 0;
            if (profilePool != null)
            {
                foreach (MonsterMotionProfile profile in profilePool)
                {
                    if (profile == null) { rejected++; continue; }
                    if (usableProfiles.Contains(profile)) { rejected++; continue; }
                    if (!TargetCombatController.IsProfileUsable(profile))
                    {
                        rejected++;
                        Debug.LogWarning($"[MonsterEncounterQueue] '{name}': 프로필 풀의 '{profile.name}'에 Base Idle/Hit " +
                                         "프레임이 없어 후보에서 제외합니다.", this);
                        continue;
                    }
                    usableProfiles.Add(profile);
                }
            }

            if (usableProfiles.Count == 0 && !warnedEmptyPool)
            {
                warnedEmptyPool = true;
                Debug.LogWarning($"[MonsterEncounterQueue] '{name}': Profile Pool에 유효한 후보가 없습니다" +
                                 (rejected > 0 ? $"(무효 항목 {rejected}개 제외)" : "") +
                                 " - 각 슬롯의 현재 프로필을 그대로 재사용합니다.", this);
            }
        }

        /// <summary>다음 등장에 쓸 프로필을 고른다 - 가능하면 avoid(주로 지금 Current)와 다른 프로필을
        /// 균등 확률로 고르고, 유효 후보가 하나뿐이면 그 하나를 그대로 반복해서 쓴다. 후보가 없으면
        /// null(= 기존 프로필 유지)을 돌려준다.</summary>
        private MonsterMotionProfile PickNextProfile(MonsterMotionProfile avoid)
        {
            if (usableProfiles.Count == 0) return null;
            if (usableProfiles.Count == 1) return usableProfiles[0];

            int candidateCount = 0;
            for (int i = 0; i < usableProfiles.Count; i++)
            {
                if (usableProfiles[i] != avoid) candidateCount++;
            }
            if (candidateCount == 0) return usableProfiles[0];

            int choice = UnityEngine.Random.Range(0, candidateCount);
            for (int i = 0; i < usableProfiles.Count; i++)
            {
                if (usableProfiles[i] == avoid) continue;
                if (choice == 0) return usableProfiles[i];
                choice--;
            }
            return usableProfiles[0];
        }

        // -------------------------------------------------------------------------------------
        // 위치/알파 적용
        // -------------------------------------------------------------------------------------

        /// <summary>전투 위치(로컬). combatPosition Transform이 연결돼 있으면 그 월드 위치를 각 몬스터의
        /// 부모 공간으로 변환해 쓰고, 없으면 몬스터 자신의 Combat Stage Layout + 프로필 Actor Offset
        /// (그것도 없으면 씬 최초 위치)을 쓴다.</summary>
        private Vector3 ResolveCombatLocalPosition(TargetCombatController slot)
        {
            if (combatPosition == null) return slot.ResolveCombatBaseLocalPosition();

            Transform parent = slot.transform.parent;
            return parent != null ? parent.InverseTransformPoint(combatPosition.position) : combatPosition.position;
        }

        private Vector3 ResolveStandbyLocalPosition(TargetCombatController slot)
        {
            return ResolveCombatLocalPosition(slot) + standbyOffset;
        }

        private void ApplyCurrentPresentation(TargetCombatController slot, bool instant)
        {
            if (slot == null) return;

            slot.ApplyCurrentVisual();

            Vector3 combat = ResolveCombatLocalPosition(slot);
            if (instant || promotionDuration <= 0f) slot.SetPresentationBasePosition(combat);
            else slot.BeginPresentationMove(combat, promotionDuration);
        }

        private void ApplyStandbyPresentation(TargetCombatController slot)
        {
            if (slot == null) return;

            slot.ApplyStandbyVisual(standbyAlpha);
            slot.SetPresentationBasePosition(ResolveStandbyLocalPosition(slot));
        }

        // -------------------------------------------------------------------------------------
        // 처치 감지 -> LateUpdate 승격 -> 퇴장 마감/재장전
        // -------------------------------------------------------------------------------------

        private void Subscribe()
        {
            if (subscribed) return;

            // 두 슬롯 모두 구독해 둔다 - 승격으로 역할이 서로 바뀌어도 구독을 다시 걸 필요가 없고,
            // 처리 여부는 핸들러에서 "지금 Current인가"로만 판단한다.
            firstSlot.CombatTarget.OnDefeated += HandleFirstSlotDefeated;
            secondSlot.CombatTarget.OnDefeated += HandleSecondSlotDefeated;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed) return;

            if (firstSlot != null && firstSlot.CombatTarget != null) firstSlot.CombatTarget.OnDefeated -= HandleFirstSlotDefeated;
            if (secondSlot != null && secondSlot.CombatTarget != null) secondSlot.CombatTarget.OnDefeated -= HandleSecondSlotDefeated;
            subscribed = false;
        }

        private void HandleFirstSlotDefeated(string targetId) => HandleSlotDefeated(firstSlot);

        private void HandleSecondSlotDefeated(string targetId) => HandleSlotDefeated(secondSlot);

        /// <summary>지금 이 호출은 플레이어 타격의 HitPoint 디스패치 한복판이다(Target.Defeat이 동기
        /// 호출한다). <b>여기서는 역할도 시각 상태도 절대 바꾸지 않는다</b> - 예약만 하고 실제 전환은
        /// LateUpdate가 한다. 처치/보상 이벤트는 Target이 이미 정확히 한 번씩 보냈고, 이 관리자는 그
        /// 흐름에 아무것도 더하거나 빼지 않는다.</summary>
        private void HandleSlotDefeated(TargetCombatController slot)
        {
            if (!configured || slot != currentSlot) return;

            promotionPending = true;
        }

        private void LateUpdate()
        {
            // 접힌 동안(마을)에는 승격도 퇴장 마감도 없다 - configured가 이미 false지만, "접혀 있으면
            // LateUpdate는 아무 일도 하지 않는다"를 코드로도 명시해 둔다.
            if (suspended || !configured) return;

            if (promotionPending)
            {
                promotionPending = false;
                PromoteStandby();
            }

            UpdateExitingTimer();
        }

        /// <summary>처치된 Current를 Exiting으로 내리고 반대편 슬롯을 Current로 올린다.
        ///
        /// 반대편 슬롯이 아직 퇴장 연출 중이라면(퇴장이 끝나기 전에 새 Current가 죽은 빠른 연속 처치)
        /// <b>먼저</b> 그 퇴장을 마감해 Standby로 되돌린 뒤에 새 처치분을 Exiting 자리에 넣는다 - 순서가
        /// 반대면 exitingSlot 참조를 덮어써 그 몬스터가 처치 상태 그대로 영원히 남는다.
        ///
        /// 역할 변경은 즉시(동기적으로) 공격 가능 레지스트리에 반영되므로, 이 LateUpdate가 끝난 시점에
        /// 새 Current는 이미 공격 가능하다 - 다음 프레임의 첫 입력이 정상 동작한다. 위치 이동
        /// (promotionDuration)은 그와 무관하게 진행된다.</summary>
        private void PromoteStandby()
        {
            TargetCombatController defeated = currentSlot;
            if (defeated == null) return;

            TargetCombatController other = GetOtherSlot(defeated);

            if (other != null && other == exitingSlot)
            {
                // 2물리슬롯의 불가피한 tradeoff: 이전 몬스터의 퇴장 연출을 여기서 끊고 곧바로 재사용한다.
                if (!warnedForcedExit)
                {
                    warnedForcedExit = true;
                    Debug.LogWarning($"[MonsterEncounterQueue] '{name}': 이전 몬스터의 퇴장 연출이 끝나기 전에 다음 " +
                                     "몬스터가 처치돼 퇴장을 즉시 마감하고 재사용합니다(슬롯이 둘뿐인 구성의 한계). " +
                                     "연출을 온전히 보려면 Exiting 시간을 줄이거나 슬롯을 늘리세요.", this);
                }
                FinalizeExiting();
            }

            currentSlot = null;
            standbySlot = null;

            // 이 시점에 exitingSlot은 반드시 비어 있다(위에서 마감했거나 애초에 없었다).
            defeated.SetEngagementRole(TargetEngagementRole.Exiting);
            exitingSlot = defeated;
            exitStartTime = Time.time;
            exitDuration = useTargetDefeatFadeDuration
                ? Mathf.Max(0f, defeated.CombatTarget != null ? defeated.CombatTarget.DefeatFadeDuration : 0f)
                : Mathf.Max(0f, exitingDuration);

            // 처치 순간 컨트롤러가 이미 Target.DefeatFadeDuration으로 Fade-out을 시작했다 - 관리자 시간을
            // 쓰기로 한 경우에만 지금 알파에서 이어서 다시 재생한다.
            if (!useTargetDefeatFadeDuration) defeated.ApplyExitingVisual(exitDuration);
            ExitingStarted?.Invoke(defeated);

            if (other == null)
            {
                if (!warnedEmptyStandby)
                {
                    warnedEmptyStandby = true;
                    Debug.LogWarning($"[MonsterEncounterQueue] '{name}': 승격할 몬스터가 없어 대기열이 비었습니다.", this);
                }
                return;
            }

            currentSlot = other;
            currentSlot.SetEngagementRole(TargetEngagementRole.Current);
            ApplyCurrentPresentation(currentSlot, instant: false);
            CurrentPromoted?.Invoke(currentSlot);
        }

        /// <summary>퇴장 연출 시간이 끝나면 그 슬롯을 다음 Standby로 재장전한다. Target의 respawnDelay는
        /// 전혀 쓰지 않는다(관리 대상은 자체 리스폰을 하지 않으므로 타이밍은 오직 이 관리자 것이다).</summary>
        private void UpdateExitingTimer()
        {
            if (exitingSlot == null || standbySlot != null) return;
            if (Time.time - exitStartTime < exitDuration) return;

            FinalizeExiting();
        }

        /// <summary>퇴장이 끝난(또는 강제로 마감된) 슬롯을 이벤트 없이 다음 Standby로 되돌린다 -
        /// 프로필 교체 + 체력/처치 상태 복원 + 대기 알파/위치 적용까지 한 번에 처리한다.
        /// <b>AnyTargetDefeated/OnDefeated/보상/경험치/킬카운트/행동력은 하나도 발생하지 않는다.</b></summary>
        private void FinalizeExiting()
        {
            TargetCombatController slot = exitingSlot;
            exitingSlot = null;
            if (slot == null) return;

            MonsterMotionProfile avoid = currentSlot != null ? currentSlot.MotionProfile : null;
            MonsterMotionProfile next = PickNextProfile(avoid);

            slot.PrepareForEncounter(TargetEngagementRole.Standby, next); // 이벤트 없음
            ApplyStandbyPresentation(slot);

            standbySlot = slot;
            StandbyRefilled?.Invoke(slot);
        }

        private TargetCombatController GetOtherSlot(TargetCombatController slot)
        {
            if (slot == null) return null;
            if (slot == firstSlot) return secondSlot;
            if (slot == secondSlot) return firstSlot;
            return null;
        }
    }
}
