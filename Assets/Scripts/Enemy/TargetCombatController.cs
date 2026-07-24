using System;
using System.Collections;
using System.Collections.Generic;
using Character;
using Common;
using UnityEngine;

namespace Enemy
{
    /// <summary>
    /// 기본 Idle을 계속 루프하다가, 플레이어 공격의 HitPoint(PlayerCharacterAnimator.HitPoint)를 받으면
    /// 즉시 피격 자세(holdFrame)로 전환해 유지한다. 연타 중에는 애니메이션을 처음부터 재시작하지 않고
    /// 그 자세를 유지한 채 매 타격마다 플래시만 갱신해서 "계속 맞고 있는" 느낌을 준다.
    /// 마지막 HitPoint 이후 holdTimeout이 지나면 복귀 프레임(recoveryFrame)을 보여준 뒤 Idle로 돌아간다.
    /// 복귀 중에 새 HitPoint가 들어오면 즉시 피격 상태로 되돌아간다.
    ///
    /// 타격 처리 순서는 고정이다: damage 적용(Target.ApplyDamage) -> 피격 반응(자세/플래시/흔들림) ->
    /// 데미지 숫자(DamageNumberSpawner) -> 타격 이펙트(HitEffectSpawner). ApplyDamage는 이번 타격이
    /// 처치를 유발하면 OnDefeated를 동기 호출한다 - 즉 ApplyDamage가 끝난 시점에 이미 hitPhase가
    /// Defeated로 넘어가 있을 수 있다. Target은 그 뒤로 Fade-out/대기/Fade-in을 전부 코루틴으로
    /// 순서대로 진행하며(같은 프레임에 몰리지 않는다), OnRespawnStarted/OnRespawned는 항상 이후
    /// 프레임에 온다. 피격 반응 단계는 이 상태 전이를 덮어쓰지 않고 그 위에서 플래시/자세만 보정한다 -
    /// 자세한 규칙은 OnHitPoint/HandleDefeated 주석 참고.
    ///
    /// 내구도/처치/리젠 "시간"은 전부 Target이 담당한다 - 이 스크립트는 Target이 보내는 이벤트
    /// (OnDefeated, OnRespawnStarted, OnRespawned)를 듣고 그에 맞는 자세와 알파만 보여준다: 처치되면
    /// 지금 피격 자세를 그대로 유지("Defeated" 상태로 전환만 하고 별도 타이머는 없음)한 채
    /// Target.DefeatFadeDuration에 맞춰 알파를 0으로 페이드한다. Target이 대기를 마치고
    /// OnRespawnStarted를 보내오면 이전 Hit/Defeated 프레임이 노출되지 않도록 먼저 Idle 기준 자세로
    /// 정리한 뒤 Target.RespawnFadeDuration에 맞춰 알파를 원래 값으로 페이드한다. Fade-in이 끝나
    /// Target이 OnRespawned를 보내오면 그때 기존 복귀 흐름(Recovery -> Idle)을 재사용해 돌아간다.
    /// 알파 Fade는 스케일/위치/회전과 완전히 분리된 별도 코루틴(fadeRoutine)이 SpriteRenderer의
    /// color.a만 건드리며, hitPhase 기반의 자세 전환 로직과는 서로 간섭하지 않는다.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    [RequireComponent(typeof(FlashOnCue))]
    [RequireComponent(typeof(Target))]
    [RequireComponent(typeof(DamageNumberSpawner))]
    [RequireComponent(typeof(HitEffectSpawner))]
    public class TargetCombatController : MonoBehaviour
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

        private sealed class RuntimeFrameAnimation
        {
            public readonly Sprite[] Frames;
            public readonly float AnimationFps;

            public RuntimeFrameAnimation(Sprite[] frames, float animationFps)
            {
                Frames = frames ?? Array.Empty<Sprite>();
                AnimationFps = animationFps;
            }
        }

        private const int BaseIdleAnimIndex = 0;

        [Header("Monster Motion Profile (optional)")]
        [Tooltip("연결하면 Idle/Hit Reaction/Defeat 프레임과 Hit Reaction 제작값을 몬스터별 프로필에서 가져온다. " +
                 "비어 있으면(또는 프로필에 해당 모션이 비어 있으면) 아래 기존 Inspector 값을 그대로 사용한다.")]
        [SerializeField] private MonsterMotionProfile motionProfile;

        [Header("Combat Stage Layout (optional)")]
        [Tooltip("연결하면 시작 위치를 Monster Slot Position + 이 몬스터 프로필의 Actor Offset으로 계산하고, " +
                 "Actor Scale도 함께 적용한다(Motion Editor Preview와 동일한 공식). 비어 있으면 기존처럼 씬에 " +
                 "배치된 현재 Transform 위치/스케일을 그대로 쓴다.")]
        [SerializeField] private CombatStageLayout stageLayout;

        [Header("Base Idle (계속 루프)")]
        [SerializeField] private FrameAnimation idle;

        [Header("Hit (연속 타격에 맞춰 유지/갱신)")]
        [SerializeField] private HitAnimation hit;

        private SpriteRenderer spriteRenderer;
        private FlashOnCue flashOnCue;
        private Target target;
        private DamageNumberSpawner damageNumberSpawner;
        private HitEffectSpawner hitEffectSpawner;

        // [0] = Base Idle(항상 존재), [1..] = Motion Profile의 Idle Events - PlayerCharacterAnimator의
        // animations 배열과 같은 구조. 몬스터에는 캐릭터의 idleA/b/c 같은 레거시 인라인 변형 슬롯이
        // 없었으므로 Idle Event는 전적으로 프로필 데이터로만 채워진다(직접 설정 fallback 없음).
        private RuntimeFrameAnimation[] idleAnimations;
        private int idleAnimIndex;
        private bool playingIdleEvent;
        private float idleEventTimer;
        private float idleEventCheckInterval = 4f;
        private float idleEventChance = 0.5f;

        private Sprite[] hitFrames;
        private int hitHoldFrame;
        private int hitRecoveryFrame;
        private float hitRecoveryDuration;
        private float hitHoldTimeout;
        private float hitShakeStrength;
        private float hitShakeFrequency;
        private float hitShakeDecayDuration;
        private Sprite[] defeatFrames;

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

        // 처치/리젠 알파 Fade 대상. Awake에서 자신 및 자식의 SpriteRenderer를 전부 수집한다 -
        // ReceivePoint/ImpactPoint/DamageAnchor 같은 비시각 앵커는 SpriteRenderer가 없으므로 자동으로
        // 제외된다. RGB는 절대 건드리지 않고 각자의 원래 알파만 개별로 저장해 그 값 기준으로 Fade한다.
        private SpriteRenderer[] visualRenderers;
        private float[] originalAlphas;
        private Coroutine fadeRoutine;

        public MonsterMotionProfile MotionProfile => motionProfile;

        private void Awake()
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
            flashOnCue = GetComponent<FlashOnCue>();
            target = GetComponent<Target>();
            damageNumberSpawner = GetComponent<DamageNumberSpawner>();
            hitEffectSpawner = GetComponent<HitEffectSpawner>();

            basePosition = ResolveInitialBasePosition();
            transform.localPosition = basePosition;
            ApplyActorScale();

            // 프로필이 없으면 SpriteRenderer에 이미 authored된 flipX 값(기존 수동 설정)을 그대로 둔다 -
            // 프로필이 연결됐을 때만 그 값으로 넘겨받는다.
            if (motionProfile != null) spriteRenderer.flipX = motionProfile.SpriteFlipX;

            BuildRuntimeConfiguration();

            idleCurrentFrame = 0;
            ApplyIdleFrame();

            visualRenderers = GetComponentsInChildren<SpriteRenderer>(true);
            originalAlphas = new float[visualRenderers.Length];
            for (int i = 0; i < visualRenderers.Length; i++)
            {
                originalAlphas[i] = visualRenderers[i].color.a;
            }
        }

        /// <summary>Preview(DrawPairedStage)와 같은 공식: Slot + Actor Offset. stageLayout이 없는
        /// 몬스터(기존 프리팹/씬)는 지금 Transform 위치를 그대로 기준점으로 쓴다 - 아무것도 깨지지 않는다.</summary>
        private Vector3 ResolveInitialBasePosition()
        {
            if (stageLayout == null) return transform.localPosition;

            Vector2 offset = motionProfile != null ? motionProfile.Preview.ActorOffset : Vector2.zero;
            Vector2 slot = stageLayout.MonsterSlotPosition;
            return new Vector3(slot.x + offset.x, slot.y + offset.y, transform.localPosition.z);
        }

        private void ApplyActorScale()
        {
            if (stageLayout == null) return;

            float scale = motionProfile != null ? motionProfile.Preview.ActorScale : 1f;
            transform.localScale = new Vector3(scale, scale, 1f);
        }

        /// <summary>Motion Editor의 "Apply Preview Layout to Open Stage"나 향후 런타임 배치 갱신이
        /// 호출하는 진입점 - basePosition과 실제 Transform 위치를 함께 새 기준점으로 맞추고, 진행 중이던
        /// 흔들림은 안전하게 취소한다(그대로 두면 다음 프레임에 옛 basePosition 기준으로 튈 수 있다).
        /// 피격 반응이 진행 중이 아닐 때(선택/교체/초기화 시점)만 호출해야 한다.</summary>
        public void SetPresentationBasePosition(Vector3 localPosition)
        {
            basePosition = localPosition;
            transform.localPosition = localPosition;
            shaking = false;
        }

        /// <summary>Idle/Hit은 motionProfile에 프레임이 있으면 그 값(및 Hit Reaction 제작값)을 쓰고,
        /// 없으면 기존처럼 이 컴포넌트에 직접 설정한 idle/hit 필드로 fallback한다 - 프로필이 없는
        /// 기존 프리팹이 그대로 동작해야 하기 때문이다. Defeat는 기존에 직접 설정하는 필드 자체가
        /// 없었던 완전히 새로운 값이라 프로필에만 있으면 쓰고, 없으면 조용히 빈 배열이다(경고 없음 -
        /// Defeat 프레임 없이 페이드아웃만 하는 것은 지금까지의 정상 동작이다).</summary>
        private void BuildRuntimeConfiguration()
        {
            MonsterMotionProfile.FrameClip profileIdle = motionProfile != null ? motionProfile.BaseIdle : null;
            if (profileIdle != null && profileIdle.Frames.Length > 0)
            {
                var runtimeIdleAnimations = new List<RuntimeFrameAnimation>
                {
                    new RuntimeFrameAnimation(profileIdle.Frames, profileIdle.AnimationFps)
                };

                IReadOnlyList<MonsterMotionProfile.FrameClip> idleEvents = motionProfile.IdleEvents;
                for (int i = 0; i < idleEvents.Count; i++)
                {
                    MonsterMotionProfile.FrameClip clip = idleEvents[i];
                    if (clip == null || clip.Frames.Length == 0) continue;
                    runtimeIdleAnimations.Add(new RuntimeFrameAnimation(clip.Frames, clip.AnimationFps));
                }

                idleAnimations = runtimeIdleAnimations.ToArray();
                idleEventCheckInterval = motionProfile.IdleEventCheckInterval;
                idleEventChance = motionProfile.IdleEventChance;
            }
            else
            {
                idleAnimations = new[]
                {
                    new RuntimeFrameAnimation(idle?.frames ?? Array.Empty<Sprite>(), idle != null ? idle.animationFps : 3f)
                };
                idleEventCheckInterval = 4f;
                idleEventChance = 0.5f;
            }

            MonsterMotionProfile.FrameClip profileHit = motionProfile != null ? motionProfile.Hit : null;
            if (profileHit != null && profileHit.Frames.Length > 0)
            {
                hitFrames = profileHit.Frames;
                MonsterMotionProfile.HitReactionSettings reaction = motionProfile.HitReaction;
                hitHoldFrame = reaction.HoldFrame;
                hitRecoveryFrame = reaction.RecoveryFrame;
                hitRecoveryDuration = reaction.RecoveryDuration;
                hitHoldTimeout = reaction.HoldTimeout;
                hitShakeStrength = reaction.ShakeStrength;
                hitShakeFrequency = reaction.ShakeFrequency;
                hitShakeDecayDuration = reaction.ShakeDecayDuration;
            }
            else
            {
                hitFrames = hit?.frames ?? Array.Empty<Sprite>();
                hitHoldFrame = hit != null ? hit.holdFrame : 0;
                hitRecoveryFrame = hit != null ? hit.recoveryFrame : 1;
                hitRecoveryDuration = hit != null ? hit.recoveryDuration : 0.12f;
                hitHoldTimeout = hit != null ? hit.holdTimeout : 0.2f;
                hitShakeStrength = hit != null ? hit.shakeStrength : 0.04f;
                hitShakeFrequency = hit != null ? hit.shakeFrequency : 35f;
                hitShakeDecayDuration = hit != null ? hit.shakeDecayDuration : 0.15f;
            }

            defeatFrames = motionProfile != null && motionProfile.Defeat != null ? motionProfile.Defeat.Frames : Array.Empty<Sprite>();

            if (idleAnimations[BaseIdleAnimIndex].Frames.Length == 0)
            {
                Debug.LogWarning($"[TargetCombatController] '{name}': Idle 프레임이 없습니다(프로필 또는 직접 설정 확인).", this);
            }
            if (hitFrames.Length == 0)
            {
                Debug.LogWarning($"[TargetCombatController] '{name}': Hit 프레임이 없습니다(프로필 또는 직접 설정 확인).", this);
            }
        }

        private void OnEnable()
        {
            PlayerCharacterAnimator.HitPoint += OnHitPoint;
            target.OnDefeated += HandleDefeated;
            target.OnRespawnStarted += HandleRespawnStarted;
            target.OnRespawned += HandleRespawned;
        }

        private void OnDisable()
        {
            PlayerCharacterAnimator.HitPoint -= OnHitPoint;
            target.OnDefeated -= HandleDefeated;
            target.OnRespawnStarted -= HandleRespawnStarted;
            target.OnRespawned -= HandleRespawned;

            if (fadeRoutine != null)
            {
                StopCoroutine(fadeRoutine);
                fadeRoutine = null;
            }
        }

        private void OnHitPoint(AttackHitCue cue)
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
            target.ApplyDamage(cue.Damage);

            if (defeatedByCurrentHit)
            {
                // 처치를 유발한 타격: HandleDefeated가 이미 hitPhase를 Defeated로 옮기고(Fade-out은
                // 별도 코루틴으로 이미 시작된 상태), Defeat 프레임이 있으면 그 포즈로 이미 바꿔뒀다 -
                // 여기서는 그 포즈를 덮어쓰지 않는다. Defeat 프레임이 없으면 기존처럼 Hit 홀드 포즈를
                // 유지(리스폰 전까지)하고, 이번 타격이 눈에 보이도록 플래시를 갱신한다.
                if (defeatFrames.Length == 0) ApplyHitPose();
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

            // Motion Profile이 연결돼 있으면 그 Damage Number Offset을 타격 시점의 최종 위치(현재
            // transform - CombatStageLayout/Actor Offset이 이미 반영된 값) 기준으로 변환해서 쓴다.
            // Shake는 이 시점에는 아직 시작 전(TriggerShake는 방금 shaking 플래그만 켰을 뿐 위치는
            // 다음 프레임 UpdateShake부터 움직인다)이라 "타격 시점의" 위치가 곧 흔들리기 직전의 기준
            // 위치와 같다 - Hit Effect/Receive Point와 동일한 타이밍 규칙이다. 프로필이 없으면 null을
            // 넘겨 DamageNumberSpawner가 기존 anchor 기준으로 그대로 동작하게 둔다.
            Vector3? damageNumberCenter = motionProfile != null
                ? transform.TransformPoint(motionProfile.HitReaction.DamageNumberOffset)
                : (Vector3?)null;
            // 위치뿐 아니라 Jitter/Rise Distance/Duration/색상/폰트 크기/Sorting Order도 프로필이
            // 있으면 그 값을 우선 쓴다(연결 안 됐으면 null - DamageNumberSpawner가 기존 Inspector
            // 값 그대로 동작).
            DamageNumberPresentation? damageNumberPresentation = motionProfile != null
                ? new DamageNumberPresentation(
                    motionProfile.HitReaction.DamageNumberRandomHorizontalJitter,
                    motionProfile.HitReaction.DamageNumberRiseDistance,
                    motionProfile.HitReaction.DamageNumberDuration,
                    motionProfile.HitReaction.DamageNumberTextColor,
                    motionProfile.HitReaction.DamageNumberFontSize,
                    motionProfile.HitReaction.DamageNumberSortingOrder)
                : (DamageNumberPresentation?)null;
            damageNumberSpawner.Spawn(cue.Damage, damageNumberCenter, damageNumberPresentation);
            // 공격별 Hit Effect(cue.EffectPrefab)가 비어 있으면 hitEffectSpawner의 defaultEffectPrefab으로
            // 자동 fallback한다(Spawn() 자체 로직) - Offset/Scale은 프리팹 오버라이드 여부와 무관하게
            // 공격 모션 값을 그대로 적용한다.
            hitEffectSpawner.Spawn(cue.EffectPrefab, offsetOverride: cue.EffectOffset, scaleOverride: cue.EffectScale);

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
            ApplyDefeatPose();

            StartFade(toOriginal: false, duration: target.DefeatFadeDuration);
        }

        /// <summary>Defeat 프레임(motionProfile.Defeat)이 있으면 그 첫 프레임을 즉시 보여준다 - 페이드아웃
        /// 되는 동안 유지되는 정지 포즈일 뿐이라 별도로 재생/루프하지 않는다. 없으면 아무것도 하지
        /// 않는다(OnHitPoint가 기존처럼 Hit 홀드 포즈를 유지한다).</summary>
        private void ApplyDefeatPose()
        {
            if (defeatFrames.Length == 0) return;
            spriteRenderer.sprite = defeatFrames[0];
        }

        /// <summary>Target의 WaitingForRespawn이 끝나 Fade-in이 시작될 때 호출된다. 아직 Alive는
        /// 아니다(OnRespawned는 별도로 온다) - 이전 Hit/Defeated 프레임이 Fade-in 동안 노출되지 않게
        /// 먼저 Idle 기준 자세로 정리한 뒤 알파를 원래 값으로 페이드한다.</summary>
        private void HandleRespawnStarted(string targetId)
        {
            ExitToIdle();
            StartFade(toOriginal: true, duration: target.RespawnFadeDuration);
        }

        private void HandleRespawned(string targetId)
        {
            EnterRecovery(); // 기존 복귀 흐름(Recovery -> Idle)을 그대로 재사용한다.
        }

        /// <summary>처치/리젠 Fade를 시작한다. 진행 중이던 Fade가 있으면 안전하게 중단하고, 새 Fade는
        /// 그 순간의 실제 알파를 시작값으로 삼아 이어간다 - 그래야 처치와 리젠 Fade가 같은 Renderer를
        /// 동시에 제어하거나 순간이동하는 일이 없다.</summary>
        private void StartFade(bool toOriginal, float duration)
        {
            if (fadeRoutine != null)
            {
                StopCoroutine(fadeRoutine);
                fadeRoutine = null;
            }
            fadeRoutine = StartCoroutine(FadeRoutine(toOriginal, duration));
        }

        private IEnumerator FadeRoutine(bool toOriginal, float duration)
        {
            int count = visualRenderers.Length;
            var startAlphas = new float[count];
            for (int i = 0; i < count; i++)
            {
                startAlphas[i] = visualRenderers[i] != null ? visualRenderers[i].color.a : 0f;
            }

            if (duration <= 0f)
            {
                ApplyTargetAlphas(toOriginal);
                fadeRoutine = null;
                yield break;
            }

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                for (int i = 0; i < count; i++)
                {
                    if (visualRenderers[i] == null) continue;
                    float targetAlpha = toOriginal ? originalAlphas[i] : 0f;
                    Color c = visualRenderers[i].color;
                    c.a = Mathf.Lerp(startAlphas[i], targetAlpha, t);
                    visualRenderers[i].color = c;
                }
                yield return null;
            }

            ApplyTargetAlphas(toOriginal);
            fadeRoutine = null;
        }

        /// <summary>각 Renderer의 알파를 목표값으로 정확히 맞춘다 - toOriginal이면 Awake에서 저장해둔
        /// 그 Renderer만의 원래 알파로(무조건 1이 아니다), 아니면 0으로. RGB는 절대 건드리지 않는다.</summary>
        private void ApplyTargetAlphas(bool toOriginal)
        {
            for (int i = 0; i < visualRenderers.Length; i++)
            {
                if (visualRenderers[i] == null) continue;
                Color c = visualRenderers[i].color;
                c.a = toOriginal ? originalAlphas[i] : 0f;
                visualRenderers[i].color = c;
            }
        }

        private void TriggerShake()
        {
            if (hitShakeStrength <= 0f) return;

            shaking = true;
            shakeStartTime = Time.time;
        }

        private void UpdateShake()
        {
            if (!shaking) return;

            float elapsed = Time.time - shakeStartTime;
            if (elapsed >= hitShakeDecayDuration)
            {
                shaking = false;
                transform.localPosition = basePosition;
                return;
            }

            float remaining = 1f - (elapsed / hitShakeDecayDuration);
            float offsetX = Mathf.Sin(elapsed * hitShakeFrequency * Mathf.PI * 2f) * hitShakeStrength * remaining;
            transform.localPosition = basePosition + new Vector3(offsetX, 0f, 0f);
        }

        private void EnterReacting()
        {
            hitPhase = HitPhase.Reacting;
            ApplyHitPose();
            flashOnCue.Flash();
        }

        /// <summary>피격 홀드 프레임(hitHoldFrame)만 그린다 - hitPhase는 건드리지 않는다.</summary>
        private void ApplyHitPose()
        {
            if (hitFrames.Length == 0) return;
            int frame = Mathf.Clamp(hitHoldFrame, 0, hitFrames.Length - 1);
            spriteRenderer.sprite = hitFrames[frame];
        }

        private void EnterRecovery()
        {
            hitPhase = HitPhase.Recovery;
            hitPhaseTimer = 0f;

            if (hitFrames.Length > 0)
            {
                int frame = Mathf.Clamp(hitRecoveryFrame, 0, hitFrames.Length - 1);
                spriteRenderer.sprite = hitFrames[frame];
            }
        }

        /// <summary>Hit/Recovery를 마치고, 또는 Respawn Fade-in 준비로 Idle에 복귀할 때 호출된다.
        /// 진행 중이던 Idle Event는 여기서 무조건 취소되고 Base Idle 0번 프레임부터 다시 시작한다 -
        /// "Hit/Recovery 종료 후에는 Idle Event가 이어지지 않고 Base Idle부터 재개"를 보장한다.</summary>
        private void ExitToIdle()
        {
            hitPhase = HitPhase.None;
            playingIdleEvent = false;
            idleAnimIndex = BaseIdleAnimIndex;
            idleCurrentFrame = 0;
            idleFrameTimer = 0f;
            idleEventTimer = 0f;
            ApplyIdleFrame();
        }

        private void Update()
        {
            UpdateShake();

            switch (hitPhase)
            {
                case HitPhase.Reacting:
                    if (Time.time - lastHitTime >= hitHoldTimeout)
                    {
                        EnterRecovery();
                    }
                    break;

                case HitPhase.Recovery:
                    hitPhaseTimer += Time.deltaTime;
                    if (hitPhaseTimer >= hitRecoveryDuration)
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

                    // Idle Event는 Hit/Recovery/Defeated 중에는(위 case들이라 여기 도달하지 않음)
                    // 시작하지 않는다. fadeRoutine이 도는 동안(Respawn Fade-in 포함)도 hitPhase는
                    // 이미 None으로 돌아와 있을 수 있어 별도로 막는다 - Fade 중에는 새 Idle Event를
                    // 굴리지 않는다(카운트다운도 그동안은 멈춘다).
                    if (!playingIdleEvent && fadeRoutine == null)
                    {
                        idleEventTimer += Time.deltaTime;
                        if (idleEventTimer >= idleEventCheckInterval)
                        {
                            idleEventTimer = 0f;
                            RollIdleEvent();
                        }
                    }
                    break;
            }
        }

        /// <summary>PlayerCharacterAnimator.RollVariant()의 Idle Event 분기와 동일한 규칙: 등록된
        /// Idle Event가 하나도 없으면(Base Idle 하나뿐이면) 아무것도 하지 않고, Chance 판정에
        /// 성공하면 Idle Event 중 하나를 완전 균등 확률로 골라 한 번 재생한다.</summary>
        private void RollIdleEvent()
        {
            if (idleAnimations.Length <= 1 || UnityEngine.Random.value > idleEventChance) return;

            int choice = UnityEngine.Random.Range(1, idleAnimations.Length);
            playingIdleEvent = true;
            idleAnimIndex = choice;
            idleCurrentFrame = 0;
            idleFrameTimer = 0f;
            ApplyIdleFrame();
        }

        private void AdvanceIdle()
        {
            RuntimeFrameAnimation anim = idleAnimations[idleAnimIndex];
            Sprite[] frames = anim.Frames;
            if (frames.Length == 0 || anim.AnimationFps <= 0f) return;

            float frameDuration = 1f / anim.AnimationFps;
            idleFrameTimer += Time.deltaTime;

            if (idleFrameTimer < frameDuration) return;

            idleFrameTimer -= frameDuration;
            idleCurrentFrame++;

            if (idleCurrentFrame >= frames.Length)
            {
                if (playingIdleEvent)
                {
                    // Idle Event 재생 종료 - Base Idle로 자연스럽게 복귀(다음 Check Interval도 여기서부터 새로 센다).
                    playingIdleEvent = false;
                    idleAnimIndex = BaseIdleAnimIndex;
                    idleCurrentFrame = 0;
                    idleEventTimer = 0f;
                }
                else
                {
                    idleCurrentFrame = 0; // Base Idle은 계속 Loop
                }
            }

            ApplyIdleFrame();
        }

        private void ApplyIdleFrame()
        {
            Sprite[] frames = idleAnimations[idleAnimIndex].Frames;
            if (frames.Length == 0) return;
            spriteRenderer.sprite = frames[Mathf.Clamp(idleCurrentFrame, 0, frames.Length - 1)];
        }
    }
}
