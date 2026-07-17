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
    /// 실제 알파 Fade는 이 컴포넌트가 하지 않는다 - SpriteRenderer 등 시각 요소는 ScarecrowAnimator
    /// 같은 전용 시각 컴포넌트의 책임이다. 이 컴포넌트는 defeatFadeDuration/respawnFadeDuration
    /// "시간"만 소유하고(Inspector에서 조정하는 단일 기준점), OnDefeated/OnRespawnStarted 이벤트를
    /// 시각 컴포넌트가 그 시간에 맞춰 자기 알파를 페이드하도록 신호로만 쓴다 - 게임 상태가 렌더러를
    /// 직접 조작하지 않도록 분리한다.
    ///
    /// 활성 Target 전체를 정적 등록소(activeTargets)에 모아두고, 그중 Alive(IsDefeated==false)인
    /// 개수(aliveCount)를 증감분만 갱신해 HasAttackableTarget을 O(1)로 판정한다 - 매 프레임 여러
    /// 컴포넌트(CatKnightIdleAnimator/ComboManager/AttackMovement)가 이 값을 읽으므로 순회 없이
    /// 즉시 답할 수 있어야 한다. 특정 몬스터 이름이나 타입에 의존하지 않으므로 몬스터가 늘어나거나
    /// 종류가 달라져도 그대로 쓸 수 있다.
    /// </summary>
    public class Target : MonoBehaviour
    {
        private static readonly HashSet<Target> activeTargets = new HashSet<Target>();
        private static int aliveCount;

        /// <summary>활성 Target 중 하나 이상이 Alive면 true. 전투 입력(공격/이동/콤보)의 공통 게이트로
        /// 쓴다. 특정 인스턴스를 참조하지 않는 정적 판정이라 몬스터가 여러 마리로 늘어나도 그대로
        /// 동작한다.</summary>
        public static bool HasAttackableTarget => aliveCount > 0;

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
        /// 완전히 끝난 순간에만 false로 바뀐다. ApplyDamage는 이 값만으로 피격 가능 여부를 판단한다.</summary>
        public bool IsDefeated { get; private set; }

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
            if (!IsDefeated) aliveCount++;
        }

        private void OnDisable()
        {
            if (activeTargets.Remove(this) && !IsDefeated)
            {
                aliveCount--;
            }

            if (respawnRoutine != null)
            {
                StopCoroutine(respawnRoutine);
                respawnRoutine = null;
            }
        }

        public void ApplyDamage(int amount)
        {
            if (amount <= 0 || IsDefeated) return;

            CurrentDurability = Mathf.Max(0, CurrentDurability - amount);
            OnDamaged?.Invoke(amount);

            if (CurrentDurability <= 0)
            {
                Defeat();
            }
        }

        private void Defeat()
        {
            IsDefeated = true;
            aliveCount--;
            OnDefeated?.Invoke(TargetId);
            AnyTargetDefeated?.Invoke(TargetId); // 보상은 처치 판정 시점에 정확히 한 번만 지급(기존과 동일)

            if (respawnRoutine != null)
            {
                StopCoroutine(respawnRoutine);
            }
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
            aliveCount++;
            OnRespawned?.Invoke(TargetId);
        }
    }
}
