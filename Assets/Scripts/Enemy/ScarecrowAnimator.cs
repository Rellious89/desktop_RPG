using System;
using Character;
using Common;
using UnityEngine;

namespace Enemy
{
    /// <summary>
    /// 기본 Idle을 계속 루프하다가, 플레이어 공격의 HitPoint(CatKnightIdleAnimator.HitPoint)를 받으면
    /// 즉시 피격 자세(holdFrame)로 전환해 유지한다. 연타 중에는 애니메이션을 처음부터 재시작하지 않고
    /// 그 자세를 유지한 채 매 타격마다 플래시만 갱신해서 "계속 맞고 있는" 느낌을 준다.
    /// 마지막 HitPoint 이후 holdTimeout이 지나면 복귀 프레임(recoveryFrame)을 보여준 뒤 Idle로 돌아간다.
    /// 복귀 중에 새 HitPoint가 들어오면 즉시 피격 상태로 되돌아간다.
    ///
    /// 타격 처리 순서는 고정이다: damage 적용(Target.ApplyDamage) -> 피격 반응(자세/플래시/흔들림) ->
    /// 데미지 숫자(DamageNumberSpawner) -> 타격 이펙트(HitEffectSpawner). ApplyDamage는 이번 타격이
    /// 처치를 유발하면 OnDefeated를 동기 호출하고, respawnDelay가 0이면 그 안에서 곧바로 OnRespawned까지
    /// 연달아 호출한다 - 즉 ApplyDamage가 끝난 시점에 이미 hitPhase가 Defeated 또는(0지연 리스폰이면) Recovery로
    /// 넘어가 있을 수 있다. 피격 반응 단계는 이 상태 전이를 덮어쓰지 않고 그 위에서 플래시/자세만 보정한다 -
    /// 자세한 규칙은 OnHitPoint/HandleDefeated 주석 참고.
    ///
    /// 내구도/처치/리젠 타이밍은 전부 Target이 담당한다 - 이 스크립트는 Target이 보내는 이벤트(OnDefeated,
    /// OnRespawned)를 듣고 그에 맞는 자세만 보여준다: 처치되면 지금 피격 자세를 그대로 유지("Defeated" 상태로
    /// 전환만 하고 별도 타이머는 없음), Target이 리젠을 마치고 OnRespawned를 보내오면 그때 기존 복귀 흐름
    /// (Recovery -> Idle)을 재사용해 돌아간다. respawnDelay가 0이면 두 이벤트가 같은 프레임에 연달아 오므로
    /// 사실상 즉시 Recovery로 넘어간다 - 사망 리소스 없이 임시 처치를 표현한다.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    [RequireComponent(typeof(FlashOnCue))]
    [RequireComponent(typeof(Target))]
    [RequireComponent(typeof(DamageNumberSpawner))]
    [RequireComponent(typeof(HitEffectSpawner))]
    public class ScarecrowAnimator : MonoBehaviour
    {
        /// <summary>HitPoint를 받아 실제로 반응할 때마다 발생(연타 중에는 매 타격마다).</summary>
        public event Action ReceiveImpact;

        [System.Serializable]
        public class FrameAnimation
        {
            public Sprite[] frames;

            [Tooltip("이 애니메이션의 프레임 재생 속도(초당 프레임 전환 횟수)")]
            public float animationFps = 3f;
        }

        /// <summary>
        /// 피격 애니메이션 데이터. 프레임 낱장 Sprite를 배열로 직접 받는다(아틀라스 슬라이싱 아님).
        /// holdFrame을 연타 중 계속 유지하고, recoveryFrame은 종료 시 한 번만 보여준다 - 둘 다 실제
        /// 프레임 수(frames.Length) 범위로 자동 보정된다.
        /// </summary>
        [System.Serializable]
        public class HitAnimation
        {
            public Sprite[] frames;

            [Tooltip("피격 중 유지할 프레임 인덱스(0부터). 연타 동안 이 프레임을 계속 유지한다.")]
            public int holdFrame = 0;

            [Tooltip("피격이 끝난 뒤 보여줄 복귀 프레임 인덱스")]
            public int recoveryFrame = 1;

            [Tooltip("복귀 프레임을 보여주는 시간(초)")]
            public float recoveryDuration = 0.12f;

            [Tooltip("마지막 HitPoint 이후 이 시간(초)이 지나면 복귀를 시작한다. 0.15~0.25 권장")]
            public float holdTimeout = 0.2f;

            [Header("Hit Shake (피격 시 좌우로 떠는 연출)")]
            [Tooltip("타격 강도 - 좌우로 흔들리는 최대 폭(월드 유닛). 0이면 흔들리지 않는다.")]
            public float shakeStrength = 0.04f;

            [Tooltip("초당 좌우 진동 횟수. 높을수록 빠르게 떤다.")]
            public float shakeFrequency = 35f;

            [Tooltip("한 번의 흔들림이 잦아드는 데 걸리는 시간(초). 연타 중에는 타격마다 갱신되어 계속 이어진다.")]
            public float shakeDecayDuration = 0.15f;
        }

        private enum HitPhase { None, Reacting, Recovery, Defeated }

        [Header("Base Idle (계속 루프)")]
        [SerializeField] private FrameAnimation idle;

        [Header("Hit (연속 타격에 맞춰 유지/갱신)")]
        [SerializeField] private HitAnimation hit;

        private SpriteRenderer spriteRenderer;
        private FlashOnCue flashOnCue;
        private Target target;
        private DamageNumberSpawner damageNumberSpawner;
        private HitEffectSpawner hitEffectSpawner;

        private Sprite[] idleFrames;
        private Sprite[] hitFrames;

        private int idleCurrentFrame;
        private float idleFrameTimer;

        private HitPhase hitPhase = HitPhase.None;
        private float lastHitTime;
        private float hitPhaseTimer;

        // OnHitPoint가 target.ApplyDamage를 호출하는 동안 HandleDefeated가 동기적으로 켜주는 스크래치
        // 플래그. "이번 타격이 처치를 유발했는지"를 hitPhase 스냅샷 비교 없이 확실하게 판정하기 위해 쓴다 -
        // ApplyDamage는 이 컴포넌트의 Target에 대해 OnHitPoint에서만 호출되므로 안전하다.
        private bool defeatedByCurrentHit;

        private Vector3 basePosition;
        private bool shaking;
        private float shakeStartTime;

        private void Awake()
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
            flashOnCue = GetComponent<FlashOnCue>();
            target = GetComponent<Target>();
            damageNumberSpawner = GetComponent<DamageNumberSpawner>();
            hitEffectSpawner = GetComponent<HitEffectSpawner>();
            basePosition = transform.localPosition;

            idleFrames = idle?.frames ?? Array.Empty<Sprite>();
            hitFrames = hit?.frames ?? Array.Empty<Sprite>();

            idleCurrentFrame = 0;
            ApplyIdleFrame();
        }

        private void OnEnable()
        {
            CatKnightIdleAnimator.HitPoint += OnHitPoint;
            target.OnDefeated += HandleDefeated;
            target.OnRespawned += HandleRespawned;
        }

        private void OnDisable()
        {
            CatKnightIdleAnimator.HitPoint -= OnHitPoint;
            target.OnDefeated -= HandleDefeated;
            target.OnRespawned -= HandleRespawned;
        }

        private void OnHitPoint(int damageAmount)
        {
            // 진입 시점 상태를 기준으로 판정한다: 이미 처치된 상태로 들어온 타격은 여기서 완전히
            // 무시한다. 반대로 이 시점에 살아 있었다면, 아래에서 ApplyDamage로 처치를 유발해
            // IsDefeated가 true로 바뀌더라도 피격 반응/데미지 숫자/이펙트까지 반드시 끝까지 표시한다.
            if (target.IsDefeated) return;

            lastHitTime = Time.time;

            // 순서 고정: damage -> hit reaction -> damage number -> hit effect.
            // ApplyDamage가 이번 타격으로 처치를 유발하면 HandleDefeated가 동기적으로 hitPhase를
            // Defeated로 옮기고, respawnDelay가 0이면 그 안에서 곧바로 HandleRespawned까지 이어져
            // Recovery로 넘어간다 - defeatedByCurrentHit로 그 전이를 감지해 아래 피격 반응 단계가
            // 덮어쓰지 않도록 한다.
            defeatedByCurrentHit = false;
            target.ApplyDamage(damageAmount);

            if (defeatedByCurrentHit)
            {
                // 처치를 유발한 타격: hitPhase는 이미 Defeated(지연 리스폰) 또는 Recovery(즉시 리스폰)로
                // 결정되어 있다 - 여기서 hitPhase 자체는 건드리지 않는다. Defeated로 남아있다면 그 자세를
                // "피격 홀드 포즈"로 보여주고(리스폰 전까지 유지), Recovery라면 EnterRecovery가 이미 올바른
                // 복귀 포즈를 그려뒀으니 손대지 않는다. 두 경우 모두 이번 타격이 눈에 보이도록 플래시는 갱신한다.
                if (hitPhase == HitPhase.Defeated)
                {
                    ApplyHitPose();
                }
                flashOnCue.Flash();
            }
            else if (hitPhase == HitPhase.Reacting)
            {
                // 이미 피격 자세 유지 중이면 포즈는 그대로 두고 반짝임만 갱신해서 연타처럼 보이게 한다.
                flashOnCue.Flash();
            }
            else
            {
                // Idle 또는 Recovery(복귀 중 새 타격) 상태에서 들어온 비처치성 타격 -> 피격 상태로 (재)진입한다.
                EnterReacting();
            }

            TriggerShake();

            damageNumberSpawner.Spawn(damageAmount);
            hitEffectSpawner.Spawn();

            ReceiveImpact?.Invoke();
        }

        private void HandleDefeated(string targetId)
        {
            // 이번 타격이 처치를 유발했음을 OnHitPoint에 동기적으로 알린다(defeatedByCurrentHit).
            // 자세 전환은 여기서 hitPhase를 Defeated로 옮기는 것뿐이다 - 실제 "피격 홀드 포즈" 스프라이트는
            // OnHitPoint가 defeatedByCurrentHit를 보고 필요할 때만 적용한다(즉시 리스폰이면 아래
            // HandleRespawned가 곧바로 Recovery로 덮어쓰므로 여기서 미리 그릴 필요가 없다).
            defeatedByCurrentHit = true;
            hitPhase = HitPhase.Defeated;
        }

        private void HandleRespawned(string targetId)
        {
            EnterRecovery(); // 기존 복귀 흐름(Recovery -> Idle)을 그대로 재사용한다.
        }

        private void TriggerShake()
        {
            if (hit.shakeStrength <= 0f) return;

            shaking = true;
            shakeStartTime = Time.time;
        }

        private void UpdateShake()
        {
            if (!shaking) return;

            float elapsed = Time.time - shakeStartTime;
            if (elapsed >= hit.shakeDecayDuration)
            {
                shaking = false;
                transform.localPosition = basePosition;
                return;
            }

            float remaining = 1f - (elapsed / hit.shakeDecayDuration);
            float offsetX = Mathf.Sin(elapsed * hit.shakeFrequency * Mathf.PI * 2f) * hit.shakeStrength * remaining;
            transform.localPosition = basePosition + new Vector3(offsetX, 0f, 0f);
        }

        private void EnterReacting()
        {
            hitPhase = HitPhase.Reacting;
            ApplyHitPose();
            flashOnCue.Flash();
        }

        /// <summary>피격 홀드 프레임(hit.holdFrame)만 그린다 - hitPhase는 건드리지 않는다.</summary>
        private void ApplyHitPose()
        {
            if (hitFrames.Length == 0) return;
            int frame = Mathf.Clamp(hit.holdFrame, 0, hitFrames.Length - 1);
            spriteRenderer.sprite = hitFrames[frame];
        }

        private void EnterRecovery()
        {
            hitPhase = HitPhase.Recovery;
            hitPhaseTimer = 0f;

            if (hitFrames.Length > 0)
            {
                int frame = Mathf.Clamp(hit.recoveryFrame, 0, hitFrames.Length - 1);
                spriteRenderer.sprite = hitFrames[frame];
            }
        }

        private void ExitToIdle()
        {
            hitPhase = HitPhase.None;
            idleCurrentFrame = 0;
            idleFrameTimer = 0f;
            ApplyIdleFrame();
        }

        private void Update()
        {
            UpdateShake();

            switch (hitPhase)
            {
                case HitPhase.Reacting:
                    if (Time.time - lastHitTime >= hit.holdTimeout)
                    {
                        EnterRecovery();
                    }
                    break;

                case HitPhase.Recovery:
                    hitPhaseTimer += Time.deltaTime;
                    if (hitPhaseTimer >= hit.recoveryDuration)
                    {
                        ExitToIdle();
                    }
                    break;

                case HitPhase.Defeated:
                    // 아무것도 하지 않는다. Target이 respawnDelay만큼 기다렸다가 OnRespawned로 알려주면
                    // HandleRespawned가 EnterRecovery를 호출해 다음 단계로 넘어간다.
                    break;

                default:
                    AdvanceIdle();
                    break;
            }
        }

        private void AdvanceIdle()
        {
            if (idleFrames.Length == 0 || idle.animationFps <= 0f) return;

            float frameDuration = 1f / idle.animationFps;
            idleFrameTimer += Time.deltaTime;

            if (idleFrameTimer < frameDuration) return;

            idleFrameTimer -= frameDuration;
            idleCurrentFrame = (idleCurrentFrame + 1) % idleFrames.Length;
            ApplyIdleFrame();
        }

        private void ApplyIdleFrame()
        {
            if (idleFrames.Length == 0) return;
            spriteRenderer.sprite = idleFrames[idleCurrentFrame];
        }
    }
}
