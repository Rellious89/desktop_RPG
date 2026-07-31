using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Common
{
    /// <summary>
    /// 재사용 가능한 내구도/처치/리젠 컴포넌트. 몬스터 등 공격받는 어떤 오브젝트에도 붙여 쓴다.
    ///
    /// 상태 흐름: Alive -> (HP 0) -> Defeated/FadingOut -> WaitingForRespawn -> Respawning/FadingIn ->
    /// Alive. 이 네 구간 전체를 코루틴 하나(RespawnSequence)가 순서대로 지나가며, IsDefeated는 그
    /// 전체 구간(FadingOut+Waiting+FadingIn) 동안 계속 true로 유지된다 - "Fade-in이 시작되는 시점"과
    /// "다시 피격 가능해지는 시점"을 분리하기 위함이다(보이지도 않는/아직 다 안 나타난 몬스터가
    /// 공격받는 문제를 막는다). ApplyDamage는 이 값 하나만 보고 데미지를 무시하므로 안전하다.
    ///
    /// 실제 알파 Fade는 이 컴포넌트가 하지 않는다 - SpriteRenderer 등 시각 요소는 TargetCombatController
    /// 같은 Target 전투 표현 컴포넌트의 책임이다. 이 컴포넌트는 defeatFadeDuration/respawnFadeDuration
    /// "시간"만 소유하고(Inspector에서 조정하는 단일 기준점), OnDefeated/OnRespawnStarted 이벤트를
    /// 시각 컴포넌트가 그 시간에 맞춰 자기 알파를 페이드하도록 신호로만 쓴다 - 게임 상태가 렌더러를
    /// 직접 조작하지 않도록 분리한다.
    ///
    /// 활성 Target 전체를 정적 등록소(activeTargets)에 모아두고, 그중 "지금 공격 가능한" 개수
    /// (aliveCount)를 증감분만 갱신해 HasAttackableTarget을 O(1)로 판정한다 - 매 프레임 여러
    /// 컴포넌트(PlayerCharacterAnimator/ComboManager/AttackMovement)가 이 값을 읽으므로 순회 없이
    /// 즉시 답할 수 있어야 한다. 특정 몬스터 이름이나 타입에 의존하지 않으므로 몬스터가 늘어나거나
    /// 종류가 달라져도 그대로 쓸 수 있다.
    ///
    /// <b>공격 가능 조건은 Alive 하나가 아니라 (등록됨 AND !IsDefeated AND 역할==Current) 셋의 AND다.</b>
    /// 대기열 몬스터(<see cref="TargetEngagementRole.Standby"/>)는 살아 있고 화면에도 보이지만 공격
    /// 가능 수에 절대 포함되지 않아야 하므로, "살아 있음"과 "때릴 수 있음"을 분리한다. 이 판정을
    /// 여러 곳에 흩어 쓰지 않도록 aliveCount 증감은 <see cref="RefreshAttackableRegistration"/> 한
    /// 군데에서만 일어나고, 각 인스턴스는 자기가 지금 카운트에 기여 중인지(countedAsAttackable)를 들고
    /// 있다가 목표 상태와 다를 때만 ±1 한다 - enable/disable, 처치, prepare, 역할 전환이 어떤 순서로
    /// 몇 번 반복돼도 중복 증가나 underflow가 생기지 않는다(전부 idempotent).
    ///
    /// 처치 후 흐름의 소유권은 <see cref="TargetLifecycleMode"/>가 정한다 - 기본값인 Standalone은 위에
    /// 적은 자체 리스폰 코루틴을 그대로 돌리고, EncounterManaged는 처치 이벤트만 기존과 동일하게
    /// 정확히 한 번 보낸 뒤 코루틴을 시작하지 않고 관리자에게 넘긴다.
    /// </summary>
    public class Target : MonoBehaviour
    {
        private static readonly HashSet<Target> activeTargets = new HashSet<Target>();
        private static int aliveCount;

        /// <summary>활성 Target 중 하나 이상이 지금 공격 가능하면(Alive이면서 역할이 Current) true.
        /// 전투 입력(공격/이동/콤보)의 공통 게이트로 쓴다. 특정 인스턴스를 참조하지 않는 정적 판정이라
        /// 몬스터가 여러 마리로 늘어나도 그대로 동작한다 - 대기열이 생겨도 이 값이 세는 것은 Current
        /// 하나뿐이라 "대기 중인 몬스터 때문에 입력이 열려 있는" 상태가 만들어지지 않는다.</summary>
        public static bool HasAttackableTarget => aliveCount > 0;

        /// <summary>
        /// 현재 공격 가능한(Alive) Target 인스턴스를 하나 돌려준다 - 발사체처럼 "지금 조준할 대상"의
        /// 실제 컴포넌트가 필요한 쪽을 위한 읽기 전용 조회다. 아무 상태도 바꾸지 않는다.
        ///
        /// <see cref="HasAttackableTarget"/>이 false면 곧바로 실패로 빠지므로(aliveCount만 확인) 대부분의
        /// 프레임에서 순회 비용이 아예 없다. 후보 판정은 aliveCount를 세는 기준(IsAttackable)과 정확히
        /// 같은 조건을 쓴다 - 그래야 "HasAttackableTarget은 true인데 실제 대상은 못 찾는" 불일치가
        /// 생기지 않는다. 대기열이 있으면 Current 역할인 몬스터가 하나뿐이므로 결과도 그 하나로
        /// 결정된다(관리자가 Current 유일성을 보장한다).
        /// </summary>
        public static bool TryGetAttackableTarget(out Target target)
        {
            target = null;
            if (aliveCount <= 0) return false;

            foreach (Target candidate in activeTargets)
            {
                if (candidate == null || !candidate.IsAttackable) continue;
                target = candidate;
                return true;
            }
            return false;
        }

        [SerializeField] private string targetId;
        [SerializeField] private int maxDurability = 30;

        [Header("Respawn Timing")]
        [Tooltip("처치 판정 직후 Fade-out에 걸리는 시간(초). 실제 페이드 자체는 시각 컴포넌트가 재생하고, 여기서는 그 시간만큼 상태 진행을 대기시킨다.")]
        [SerializeField] private float defeatFadeDuration = 0.25f;
        [Tooltip("Fade-out이 끝난 뒤 완전히 사라진 채로 대기하는 시간(초).")]
        [SerializeField] private float respawnDelay = 1f;
        [Tooltip("리젠 시 Fade-in에 걸리는 시간(초). 이 시간이 끝나야 다시 피격 가능한 Alive 상태가 된다.")]
        [SerializeField] private float respawnFadeDuration = 0.25f;

        /// <summary>targetId를 비워두면 GameObject 이름을 그대로 쓴다.</summary>
        public string TargetId => string.IsNullOrEmpty(targetId) ? gameObject.name : targetId;
        public int MaxDurability => maxDurability;
        public int CurrentDurability { get; private set; }

        /// <summary>true면 Alive가 아니다 - Fade-out/대기/Fade-in 전 구간에서 계속 true이고, Fade-in이
        /// 완전히 끝난 순간에만 false로 바뀐다(EncounterManaged면 관리자의 PrepareForEncounter가 그
        /// 역할을 대신한다).</summary>
        public bool IsDefeated { get; private set; }

        /// <summary>지금 이 Target이 공격 가능한지 - aliveCount에 기여 중인지와 정확히 같은 값이다.
        /// (등록됨 AND !IsDefeated AND 역할==Current). 시각 상태(알파/위치)와는 무관하다.</summary>
        public bool IsAttackable => countedAsAttackable;

        /// <summary>지금 맡고 있는 역할. 관리자만 <see cref="SetEngagementRole"/>/
        /// <see cref="PrepareForEncounter"/>로 바꾼다 - 기본값은 Current라 관리자가 없는 기존 몬스터는
        /// 예전과 완전히 같게 동작한다.</summary>
        public TargetEngagementRole EngagementRole => engagementRole;

        /// <summary>처치 후 흐름의 소유권. 기본값은 자체 리스폰이고, 관리자가
        /// <see cref="ConfigureLifecycle"/>로만 바꾼다(직렬화되지 않는 런타임 값이라 기존 씬/프리팹에는
        /// 아무 영향이 없다).</summary>
        public TargetLifecycleMode LifecycleMode => lifecycleMode;

        private TargetEngagementRole engagementRole = TargetEngagementRole.Current;
        private TargetLifecycleMode lifecycleMode = TargetLifecycleMode.StandaloneSelfRespawn;

        // activeTargets에 들어가 있는 구간(OnEnable~OnDisable)인지. 공격 가능 조건의 한 축이라 별도
        // 상태로 둔다 - OnDisable에서 이 값이 false가 되면 나머지 조건과 무관하게 카운트에서 빠진다.
        private bool registered;

        // 지금 aliveCount에 +1로 기여하고 있는지. RefreshAttackableRegistration만 이 값을 바꾸며,
        // 목표 상태와 다를 때만 aliveCount를 ±1 하므로 어떤 호출 순서에서도 중복/underflow가 없다.
        private bool countedAsAttackable;

        public float DefeatFadeDuration => defeatFadeDuration;
        public float RespawnDelay => respawnDelay;
        public float RespawnFadeDuration => respawnFadeDuration;

        /// <summary>데미지를 실제로 적용했을 때마다 발생. 이번에 받은 데미지량을 전달한다.</summary>
        public event Action<int> OnDamaged;

        /// <summary>HP가 0이 되어 처치 상태에 진입하는 순간 발생 - Fade-out을 시작하라는 신호다.
        /// targetId를 전달한다. 기존과 의미가 같다.</summary>
        public event Action<string> OnDefeated;

        /// <summary>WaitingForRespawn이 끝나 체력이 복원되고 Fade-in이 시작되는 순간 발생 - 아직
        /// Alive는 아니다(IsDefeated는 여전히 true). targetId를 전달한다.</summary>
        public event Action<string> OnRespawnStarted;

        /// <summary>Fade-in이 완전히 끝나 다시 피격 가능한 Alive 상태로 돌아오는 순간 발생. targetId를
        /// 전달한다. 기존과 의미가 같다.</summary>
        public event Action<string> OnRespawned;

        /// <summary>씬의 어떤 Target이 처치되든 발생하는 정적 이벤트. 세션 킬카운트처럼 전역 집계에 쓴다.</summary>
        public static event Action<string> AnyTargetDefeated;

        private Coroutine respawnRoutine;

        private void Awake()
        {
            CurrentDurability = maxDurability;
        }

        private void OnEnable()
        {
            activeTargets.Add(this);
            registered = true;
            RefreshAttackableRegistration();
        }

        private void OnDisable()
        {
            activeTargets.Remove(this);
            registered = false;
            RefreshAttackableRegistration(); // 조건이 하나라도 무너지면 여기서 정확히 한 번만 -1 된다.

            StopRespawnRoutine();
        }

        /// <summary>공격 가능 조건을 다시 계산해 aliveCount를 목표 상태에 맞춘다. 상태를 바꾸는 모든
        /// 경로(enable/disable, 처치, 리스폰 완료, 역할 전환, prepare)가 반드시 이 메서드만 거치게 해서
        /// 증감이 단 한 군데에서만 일어나도록 한다 - 같은 전이를 두 번 호출해도 아무 일도 일어나지
        /// 않으므로(idempotent) 순서나 반복에 상관없이 안전하다.</summary>
        private void RefreshAttackableRegistration()
        {
            bool shouldCount = registered && !IsDefeated && engagementRole == TargetEngagementRole.Current;
            if (shouldCount == countedAsAttackable) return;

            countedAsAttackable = shouldCount;
            aliveCount += shouldCount ? 1 : -1;
        }

        private void StopRespawnRoutine()
        {
            if (respawnRoutine == null) return;

            StopCoroutine(respawnRoutine);
            respawnRoutine = null;
        }

        /// <summary>처치 후 흐름의 소유권을 바꾼다(관리자 전용 진입점). EncounterManaged로 넘길 때는
        /// 진행 중이던 자체 리스폰 코루틴을 즉시 멈춘다 - 그대로 두면 관리자가 모르는 사이에 되살아나
        /// 대기열 상태와 어긋난다. 어떤 이벤트도 발생시키지 않는다.</summary>
        public void ConfigureLifecycle(TargetLifecycleMode mode)
        {
            if (lifecycleMode == mode) return;

            lifecycleMode = mode;
            if (mode == TargetLifecycleMode.EncounterManaged) StopRespawnRoutine();
        }

        /// <summary>역할만 바꾼다(관리자 전용 진입점) - 체력이나 처치 상태는 건드리지 않고, 처치/보상
        /// 이벤트도 발생시키지 않는다. 공격 가능 여부는 즉시(동기적으로) 갱신되므로, 이 호출이 끝난
        /// 시점부터 HasAttackableTarget/TryGetAttackableTarget이 새 역할을 반영한다.</summary>
        public void SetEngagementRole(TargetEngagementRole role)
        {
            if (engagementRole == role) return;

            engagementRole = role;
            RefreshAttackableRegistration();
        }

        /// <summary>관리자가 이 Target을 다음 등장에 재사용하기 위해 상태를 되돌린다 - 체력 복원 +
        /// 처치 상태 해제 + 역할 지정을 한 번에 하고, 공격 가능 여부를 즉시 갱신한다.
        ///
        /// <b>이 메서드는 어떤 이벤트도 발생시키지 않는다</b>(OnDefeated/AnyTargetDefeated는 물론
        /// OnRespawnStarted/OnRespawned도 보내지 않는다) - 준비/역할 변경이 킬카운트, 경험치, 행동력
        /// 소비, 보상 지급 같은 처치 구독자를 건드리면 안 되기 때문이다. 되돌린 상태를 화면에
        /// 반영하는 것(포즈/알파/위치)은 호출자인 시각 컴포넌트의 책임이다.</summary>
        public void PrepareForEncounter(TargetEngagementRole role)
        {
            StopRespawnRoutine();

            engagementRole = role;
            CurrentDurability = maxDurability;
            IsDefeated = false;
            RefreshAttackableRegistration();
        }

        public void ApplyDamage(int amount)
        {
            // 공격 가능한 상태(Alive이면서 Current)일 때만 데미지가 들어간다 - 대기열(Standby)이나
            // 퇴장 중(Exiting) 몬스터는 IsDefeated가 false여도 여기서 걸러진다.
            if (amount <= 0 || !IsAttackable) return;

            CurrentDurability = Mathf.Max(0, CurrentDurability - amount);
            OnDamaged?.Invoke(amount);

            if (CurrentDurability <= 0)
            {
                Defeat();
            }
        }

        /// <summary>처치 판정. 이벤트 순서와 횟수는 생명주기 모드와 무관하게 완전히 동일하다 -
        /// 공격 가능 수에서 먼저 빠진 뒤(구독자가 보는 HasAttackableTarget이 이미 false다) OnDefeated,
        /// AnyTargetDefeated 순으로 각각 정확히 한 번. 갈라지는 것은 그 다음 한 가지뿐이다: Standalone은
        /// 자체 리스폰 코루틴을 시작하고, EncounterManaged는 시작하지 않고 관리자에게 넘긴다(그래서
        /// 관리 대상 몬스터에서는 OnRespawnStarted/OnRespawned가 아예 발생하지 않는다).</summary>
        private void Defeat()
        {
            IsDefeated = true;
            RefreshAttackableRegistration();
            OnDefeated?.Invoke(TargetId);
            AnyTargetDefeated?.Invoke(TargetId); // 보상은 처치 판정 시점에 정확히 한 번만 지급(기존과 동일)

            if (lifecycleMode == TargetLifecycleMode.EncounterManaged) return;

            StopRespawnRoutine();
            respawnRoutine = StartCoroutine(RespawnSequence());
        }

        /// <summary>Fade-out 대기 -> WaitingForRespawn 대기 -> 체력 복원 + Fade-in 시작 신호 ->
        /// Fade-in 대기 -> Alive 순으로 진행한다. 각 구간은 duration이 0이어도 WaitForSeconds(0)이
        /// 최소 한 프레임은 넘기므로, 세 구간이 전부 0이어도 동일 프레임에 몰려 연출이 생략되지 않는다.</summary>
        private IEnumerator RespawnSequence()
        {
            yield return new WaitForSeconds(Mathf.Max(0f, defeatFadeDuration));
            yield return new WaitForSeconds(Mathf.Max(0f, respawnDelay));

            CurrentDurability = maxDurability;
            OnRespawnStarted?.Invoke(TargetId);

            yield return new WaitForSeconds(Mathf.Max(0f, respawnFadeDuration));

            respawnRoutine = null;
            IsDefeated = false;
            RefreshAttackableRegistration();
            OnRespawned?.Invoke(TargetId);
        }
    }
}
