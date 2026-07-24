using System;
using System.Collections.Generic;
using Common;
using DesktopWindow;
using UnityEngine;

namespace Character
{
    /// <summary>
    /// 기본 Idle 루프를 재생하다가, 일정 주기마다 1/n 확률로 Idle 변형(a/b/c) 중 하나를
    /// 한 번 재생하고 다시 기본 Idle로 돌아온다. 변형 재생이 끝나면 다시 주기를 기다린 뒤 재판정한다.
    ///
    /// 공격은 키 입력 1회 = 타격 1회로 정확히 대응된다. 키 입력마다 대기열(pendingAttacks)에 하나씩
    /// 쌓이고, 공격 클립을 0번 프레임부터 hitFrameIndex까지 재생(Windup)한 뒤 타격(HitPoint)이
    /// 발생할 때마다 그 대기열을 하나씩 소비한다. 재생 도중 새 입력이 들어와도 애니메이션을
    /// 재시작하지 않고 대기열에만 추가되며, 타격 시점에 순서대로 소비된다.
    /// 타격 이후 대기 중인 입력이 있으면 즉시 Windup을 재시작(루프)하고, 없으면 hitFrameIndex
    /// 다음 프레임부터 마지막 프레임까지 이어서 재생(Recovery)한 뒤 Idle로 돌아간다.
    ///
    /// 각 애니메이션은 프레임 낱장 Sprite를 Inspector에서 배열로 직접 받는다(아틀라스 런타임 슬라이싱
    /// 아님). 프레임 수는 배열 길이(frames.Length)가 그대로 정답이라 별도로 입력받지 않는다.
    /// Pivot/PPU도 각 스프라이트의 임포트 설정에 이미 들어있어서 여기서 따로 지정하지 않는다.
    ///
    /// 공격 가능한 Target이 하나도 없으면(Target.HasAttackableTarget == false) 새 키 입력을 아예
    /// 대기열에 올리지 않는다 - 처치 직후 Fade-out/리젠 대기/Fade-in 중에는 허공 공격이 시작되지
    /// 않는다. 이미 진행 중인 Windup~Recovery는 끊지 않고 그대로 마무리하되, Strike() 직후 다음
    /// Windup으로 이어갈지 판단할 때도 다시 한번 확인해서 - 마지막 남은 Target을 죽인 타격이었다면
    /// 그 뒤에 밀려 있던 예약 공격(pendingAttacks)은 전부 버리고 Recovery로 빠진다.
    ///
    /// 콤보 티어별 공격 모션 풀: tier1/2/3Pool(ComboTierAttackPool 에셋) 중 ComboManager.CurrentTier에
    /// 대응하는 풀에서 매 StartWindup() 시점에 모션을 하나 완전 랜덤으로 뽑아 그 사이클(Windup ->
    /// Strike -> Recovery) 동안 그대로 쓴다 - 입력 처리/대기열/전환 규칙은 전혀 건드리지 않고 "어떤
    /// 프레임 배열을 재생할지"만 매 사이클마다 다시 고른다(직전 모션과 같아도 그대로 허용). 상위 티어
    /// 풀이 비어 있으면 한 단계씩 낮은 티어로 폴백하고(Tier3 -> Tier2 -> Tier1), 레거시 단일 attack
    /// 필드는 tier1Pool이 비어 있을 때만 Tier 1 풀의 유일한 항목으로 자동 편입된다(기존에 이미 붙여둔
    /// 스프라이트를 잃지 않기 위한 하위 호환). 모션 데이터 자체는 AttackMotionDefinition/
    /// ComboTierAttackPool ScriptableObject 에셋에 있고, 여기서는 IAttackMotion 인터페이스로만
    /// 다뤄서 레거시 AttackAnimation과 동일한 재생 루프를 공유한다.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    [RequireComponent(typeof(FlashOnCue))]
    [RequireComponent(typeof(HitEffectSpawner))]
    public class PlayerCharacterAnimator : MonoBehaviour
    {
        /// <summary>공격 세션에 처음 진입하는 순간(Idle -> Attack) 발생.</summary>
        public static event Action AttackStarted;

        /// <summary>공격 재생이 hitFrameIndex에 도달할 때마다 발생 - 데미지와 이번 공격의 Hit Presentation
        /// 값(사운드/이펙트)을 함께 실어 보낸다. 구독자(AudioManager, TargetCombatController)가 각자
        /// 필요한 값만 꺼내 쓰고, 비어 있는 값은 각자의 기본값으로 fallback한다.</summary>
        public static event Action<AttackHitCue> HitPoint;

        /// <summary>공격이 Cast Frame에 도달할 때마다 발생(공격 인스턴스당 한 번). 인자는 이번 공격의
        /// Cast Sound - null이면 재생할 사운드가 없다는 뜻이라 구독자가 그대로 무시하면 된다. Cast
        /// Effect는 이 이벤트를 거치지 않고 이 컴포넌트가 직접 castEffectSpawner에 생성을 요청한다
        /// (이펙트는 시전자 자신의 위치가 기준이라 별도 구독자가 필요 없다).</summary>
        public static event Action<AudioClip> CastSoundCue;

        /// <summary>공격이 끝나고 Idle로 돌아가는 순간 발생.</summary>
        public static event Action AttackEnded;

        [System.Serializable]
        public class FrameAnimation
        {
            public Sprite[] frames;

            [Tooltip("이 애니메이션의 프레임 재생 속도(초당 프레임 전환 횟수)")]
            public float animationFps = 6f;
        }

        /// <summary>
        /// 레거시 단일 슬롯용 공격 애니메이션 데이터. 0번 프레임이 항상 시작(Windup)이고,
        /// hitFrameIndex가 타격 프레임, 그 이후부터 마지막 프레임까지가 복귀(Recovery)다. 프레임
        /// 개수는 frames.Length 그대로다. IAttackMotion을 구현해서 ScriptableObject 기반
        /// AttackMotionDefinition과 같은 재생 루프를 공유한다.
        /// </summary>
        [System.Serializable]
        public class AttackAnimation : IAttackMotion
        {
            public Sprite[] frames;

            [Header("Playback")]
            [Tooltip("Windup/Recovery 프레임 재생 속도(초당 프레임 전환 횟수)")]
            public float animationFps = 18f;

            [Tooltip("이 프레임(0부터)에 도달하면 타격 판정(HitPoint)이 발생한다. " +
                     "실제 프레임 수를 넘으면 마지막 프레임으로 자동 보정된다.")]
            [Min(0)]
            public int hitFrameIndex = 1;

            [Header("Recovery (hitFrameIndex 다음 프레임부터 마지막 프레임까지)")]
            [Tooltip("마지막 프레임에 도달한 뒤 그 프레임을 유지하는 시간(초)")]
            public float endFrameDuration = 0.12f;

            [Header("Queue")]
            [Tooltip("마지막 입력 이후 이 시간(초) 동안 새 입력이 없으면, 남아있는 예약(대기열)을 전부 취소하고 " +
                     "진행 중인 재생만 마친 뒤 복귀한다. 0.15~0.25 권장")]
            public float queueExpireTimeout = 0.15f;

            [Header("Cast Presentation")]
            public int castFrameIndex;
            public GameObject castEffectPrefab;
            public Vector2 castEffectOffset;
            [Min(0.01f)] public float castEffectScale = 1f;
            public AudioClip castSound;

            [Header("Hit Presentation")]
            public GameObject hitEffectPrefab;
            public Vector2 hitEffectOffset;
            [Min(0.01f)] public float hitEffectScale = 1f;
            public AudioClip hitSound;

            public Sprite[] Frames => frames ?? Array.Empty<Sprite>();
            public float AnimationFps => animationFps;
            public int HitFrameIndex => hitFrameIndex;
            public float EndFrameDuration => endFrameDuration;
            public float QueueExpireTimeout => queueExpireTimeout;

            public int CastFrameIndex => castFrameIndex;
            public GameObject CastEffectPrefab => castEffectPrefab;
            public Vector2 CastEffectOffset => castEffectOffset;
            public float CastEffectScale => Mathf.Max(0.01f, castEffectScale);
            public AudioClip CastSound => castSound;

            public GameObject HitEffectPrefab => hitEffectPrefab;
            public Vector2 HitEffectOffset => hitEffectOffset;
            public float HitEffectScale => Mathf.Max(0.01f, hitEffectScale);
            public AudioClip HitSound => hitSound;
        }

        private const int IdleIndex = 0;

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

        private enum AttackPhase { None, Windup, Recovery }

        [Header("Character Motion Profile (optional)")]
        [Tooltip("연결하면 Idle/Idle Event/공격 풀/AttackMovement 제작값을 캐릭터별 프로필에서 가져온다. " +
                 "비어 있으면 아래 기존 Inspector 값을 그대로 사용한다.")]
        [SerializeField] private CharacterMotionProfile motionProfile;

        [Header("Base Idle (계속 루프)")]
        [SerializeField] private FrameAnimation idle;

        [Header("Idle Variants (한 번만 재생, 동일 확률)")]
        [SerializeField] private FrameAnimation idleA;
        [SerializeField] private FrameAnimation idleB;
        [SerializeField] private FrameAnimation idleC;

        [Header("Attack (레거시 단일 슬롯)")]
        [Tooltip("tier1Pool이 비어 있을 때만 Tier 1 풀의 유일한 항목으로 자동 사용된다(하위 호환용). " +
                 "새로 작업할 때는 아래 tier1Pool 에셋에 직접 등록하는 것을 권장한다.")]
        [SerializeField] private AttackAnimation attack;

        [Header("Combo Tier Attack Pools (콤보 티어별 공격 모션 풀 에셋)")]
        [Tooltip("Tier 0/1(Normal)에서 사용할 공격 모션 풀 에셋(ComboTierAttackPool). 비어 있으면 위 attack 필드를 자동으로 사용한다.")]
        [SerializeField] private ComboTierAttackPool tier1Pool;

        [Tooltip("Tier 2(Boost)에서 사용할 공격 모션 풀 에셋. 비어 있으면 Tier 1 풀로 폴백한다.")]
        [SerializeField] private ComboTierAttackPool tier2Pool;

        [Tooltip("Tier 3(Fever)에서 사용할 공격 모션 풀 에셋. 비어 있으면 Tier 2 -> Tier 1 순으로 폴백한다.")]
        [SerializeField] private ComboTierAttackPool tier3Pool;

        [Header("Combat")]
        [Tooltip("기본 공격 1회(타격 1번)당 적용할 데미지량. 강공격/치명타 등 추가 계산식은 아직 없다.")]
        [SerializeField] private int basicAttackPower = 5;

        [Header("Timing")]
        [SerializeField] private float variantCheckInterval = 4f;

        private SpriteRenderer spriteRenderer;
        private FlashOnCue flashOnCue;
        private HitEffectSpawner castEffectSpawner;
        private RuntimeFrameAnimation[] animations;
        private bool usesProfileIdleEvents;
        private float profileIdleEventChance;

        private int activeAnimIndex;
        private int currentFrame;
        private float frameTimer;
        private float variantTimer;
        private bool playingVariant;

        private AttackPhase attackPhase = AttackPhase.None;
        private int attackFrame;
        private float attackPhaseTimer;
        private int pendingAttacks;
        private float lastInputTime;
        private bool castCueFired;

        // 콤보 티어별로 정리된 재생 가능한 모션 풀(frames가 비어 있는 항목은 제외) - Awake에서 한 번만 만든다.
        private readonly List<IAttackMotion> resolvedTier1 = new List<IAttackMotion>();
        private readonly List<IAttackMotion> resolvedTier2 = new List<IAttackMotion>();
        private readonly List<IAttackMotion> resolvedTier3 = new List<IAttackMotion>();

        // 이번 Windup~Recovery 사이클 동안 재생 중인 모션 - StartWindup()에서만 새로 뽑는다.
        private IAttackMotion activeMotion;
        private Sprite[] activeMotionFrames = Array.Empty<Sprite>();

        private int ActiveHitFrameIndex => activeMotionFrames.Length == 0
            ? 0
            : Mathf.Clamp(activeMotion.HitFrameIndex, 0, activeMotionFrames.Length - 1);

        private int ActiveCastFrameIndex => activeMotionFrames.Length == 0
            ? 0
            : Mathf.Clamp(activeMotion.CastFrameIndex, 0, activeMotionFrames.Length - 1);

        public CharacterMotionProfile MotionProfile => motionProfile;

        private void Awake()
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
            flashOnCue = GetComponent<FlashOnCue>();
            castEffectSpawner = GetComponent<HitEffectSpawner>();
            BuildRuntimeConfiguration();

            ComboTierAttackPool resolvedTier1Pool = motionProfile != null ? motionProfile.Tier1Pool : tier1Pool;
            ComboTierAttackPool resolvedTier2Pool = motionProfile != null ? motionProfile.Tier2Pool : tier2Pool;
            ComboTierAttackPool resolvedTier3Pool = motionProfile != null ? motionProfile.Tier3Pool : tier3Pool;

            BuildResolvedPool(resolvedTier1, resolvedTier1Pool);
            if (resolvedTier1.Count == 0 && attack != null)
            {
                // 하위 호환: tier1Pool 에셋을 아직 연결하지 않았다면, 기존에 이미 붙여둔 레거시 attack
                // 필드를 Tier 1 풀의 유일한 항목으로 그대로 쓴다 - 이미 배정된 스프라이트를 잃지 않는다.
                attack.frames = SafeFrames(attack.frames);
                if (attack.frames.Length > 0) resolvedTier1.Add(attack);
            }
            BuildResolvedPool(resolvedTier2, resolvedTier2Pool);
            BuildResolvedPool(resolvedTier3, resolvedTier3Pool);

            activeAnimIndex = IdleIndex;
            currentFrame = 0;
            ApplyFrame();
        }

        private void BuildRuntimeConfiguration()
        {
            if (motionProfile != null && motionProfile.BaseIdle != null && motionProfile.BaseIdle.Frames.Length > 0)
            {
                var runtimeAnimations = new List<RuntimeFrameAnimation>
                {
                    new RuntimeFrameAnimation(motionProfile.BaseIdle.Frames, motionProfile.BaseIdle.AnimationFps)
                };

                IReadOnlyList<CharacterMotionProfile.FrameClip> idleEvents = motionProfile.IdleEvents;
                for (int i = 0; i < idleEvents.Count; i++)
                {
                    CharacterMotionProfile.FrameClip clip = idleEvents[i];
                    if (clip == null || clip.Frames.Length == 0) continue;
                    runtimeAnimations.Add(new RuntimeFrameAnimation(clip.Frames, clip.AnimationFps));
                }

                animations = runtimeAnimations.ToArray();
                usesProfileIdleEvents = true;
                profileIdleEventChance = motionProfile.IdleEventChance;
                variantCheckInterval = motionProfile.IdleEventCheckInterval;
                return;
            }

            animations = new[]
            {
                new RuntimeFrameAnimation(SafeFrames(idle.frames), idle.animationFps),
                new RuntimeFrameAnimation(SafeFrames(idleA.frames), idleA.animationFps),
                new RuntimeFrameAnimation(SafeFrames(idleB.frames), idleB.animationFps),
                new RuntimeFrameAnimation(SafeFrames(idleC.frames), idleC.animationFps),
            };
            usesProfileIdleEvents = false;
            profileIdleEventChance = 0f;
        }

        private static Sprite[] SafeFrames(Sprite[] frames) => frames ?? Array.Empty<Sprite>();

        /// <summary>pool 에셋에서 Frames가 비어 있는 항목을 제외하고 destination에 채운다(재생
        /// 불가능한 슬롯이 랜덤 선택에 걸리지 않도록). destination은 항상 먼저 비운 뒤 다시 채운다.
        /// AttackMotionDefinition은 참조만 담기 때문에, 에셋 자체를 나중에 수정하면(프레임/타이밍)
        /// 그 변경이 다음 재생부터 곧바로 반영된다 - 여기서는 복사하지 않는다.</summary>
        private static void BuildResolvedPool(List<IAttackMotion> destination, ComboTierAttackPool pool)
        {
            destination.Clear();
            if (pool == null) return;

            IReadOnlyList<AttackMotionDefinition> motions = pool.Motions;
            for (int i = 0; i < motions.Count; i++)
            {
                AttackMotionDefinition motion = motions[i];
                if (motion == null) continue;
                if (motion.Frames.Length == 0) continue;
                destination.Add(motion);
            }
        }

        /// <summary>Tier3 -> Tier2 -> Tier1 순으로 폴백한다. resolvedTier1은 Awake에서 레거시 attack
        /// 필드까지 반영된 뒤이므로, 하나라도 모션이 등록돼 있었다면 항상 최소 1개는 채워져 있다.</summary>
        private List<IAttackMotion> GetPoolForTier(int tier)
        {
            if (tier >= 3)
            {
                if (resolvedTier3.Count > 0) return resolvedTier3;
                if (resolvedTier2.Count > 0) return resolvedTier2;
                return resolvedTier1;
            }
            if (tier >= 2)
            {
                if (resolvedTier2.Count > 0) return resolvedTier2;
                return resolvedTier1;
            }
            return resolvedTier1;
        }

        /// <summary>pool에서 모션을 완전 균등 확률로 랜덤 선택한다 - 직전에 재생한 모션과 같아도
        /// 그대로 허용한다(중복 방지 없음).</summary>
        private static IAttackMotion SelectMotion(List<IAttackMotion> pool)
        {
            return pool[UnityEngine.Random.Range(0, pool.Count)];
        }

        private void Update()
        {
            // 공격 가능한 Target이 하나도 없으면 새 입력을 아예 공격으로 등록하지 않는다(대기열도
            // 늘리지 않는다) - 처치/리젠 대기 중 허공 공격을 막기 위함이다. 이미 진행 중인 Windup/
            // Recovery는 아래 AdvanceAttack()에서 그대로 마무리된다(여기서 끊지 않는다).
            if (GlobalKeyboardHook.AnyKeyDownThisFrame && Target.HasAttackableTarget)
            {
                OnKeyInput();
            }

            if (attackPhase != AttackPhase.None)
            {
                AdvanceAttack();
                return;
            }

            AdvanceFrame();

            if (!playingVariant)
            {
                variantTimer += Time.deltaTime;
                if (variantTimer >= variantCheckInterval)
                {
                    variantTimer = 0f;
                    RollVariant();
                }
            }
        }

        // ---- 입력 처리: 키 입력 1회 = 타격 1회. 대기열에 쌓아두고 재생이 순서대로 소비한다 ----
        private void OnKeyInput()
        {
            pendingAttacks++;
            lastInputTime = Time.time;

            switch (attackPhase)
            {
                case AttackPhase.None:
                    BeginAttackSession();
                    break;
                case AttackPhase.Recovery:
                    // 복귀 재생 중 새 입력 -> 복귀를 취소하고 대기 중인 타격을 이어서 처리한다.
                    StartWindup();
                    break;
                case AttackPhase.Windup:
                    // 이미 재생 중이면 애니메이션을 건드리지 않는다. 대기열에 쌓이는 것만으로 충분하다.
                    break;
            }
        }

        private void BeginAttackSession()
        {
            if (GetPoolForTier(ComboManager.CurrentTier).Count == 0) return;

            playingVariant = false;
            AttackStarted?.Invoke();
            StartWindup();
        }

        private void StartWindup()
        {
            // 콤보 티어는 매 사이클(대기열에서 하나 꺼내 재생을 시작하는 시점)마다 다시 확인한다 -
            // 재생 중인 공격을 끊지 않고 "다음 공격 시작부터" 새 티어가 반영되도록 하기 위함이다.
            activeMotion = SelectMotion(GetPoolForTier(ComboManager.CurrentTier));
            activeMotionFrames = activeMotion.Frames;

            attackPhase = AttackPhase.Windup;
            attackFrame = 0;
            attackPhaseTimer = 0f;
            castCueFired = false; // 새 공격 인스턴스 - Cast Cue를 다시 한 번만 쏠 수 있게 리셋한다.
            ApplyAttackFrame();
            TryFireCastCue(); // Cast Frame Index가 0이면 시작하자마자 시전 연출

            if (ActiveHitFrameIndex <= 0)
            {
                Strike(); // hitFrameIndex가 0이면 시작하자마자 타격
            }
        }

        /// <summary>attackFrame이 Cast Frame Index에 도달하면 공격 인스턴스당 정확히 한 번만 Cast
        /// Presentation을 실행한다. Hit 여부와 무관하게(피격 성공 여부와 상관없이) 실행되며, Cast
        /// Effect는 시전자 자신의 위치가 기준이라 이 컴포넌트가 castEffectSpawner에 직접 생성을
        /// 요청하고, Cast Sound는 AudioManager 등 다른 구독자가 반응할 수 있게 이벤트로만 알린다.</summary>
        private void TryFireCastCue()
        {
            if (castCueFired) return;
            if (attackFrame < ActiveCastFrameIndex) return;
            castCueFired = true;

            if (activeMotion.CastSound != null) CastSoundCue?.Invoke(activeMotion.CastSound);
            if (castEffectSpawner != null && activeMotion.CastEffectPrefab != null)
            {
                castEffectSpawner.Spawn(activeMotion.CastEffectPrefab, offsetOverride: activeMotion.CastEffectOffset, scaleOverride: activeMotion.CastEffectScale);
            }
        }

        private void Strike()
        {
            if (pendingAttacks > 0) pendingAttacks--; // 이 타격으로 대기열에서 요청 하나를 소비(확정)한다.

            flashOnCue.Flash();
            HitPoint?.Invoke(new AttackHitCue(basicAttackPower, activeMotion.HitSound, activeMotion.HitEffectPrefab, activeMotion.HitEffectOffset, activeMotion.HitEffectScale)); // 이 호출이 처치를 유발하면 Target.HasAttackableTarget이 여기서 이미 false로 바뀌어 있을 수 있다.

            // 처치를 유발한 타격이었다면(마지막 남은 Target이었을 경우) 이 시점에 이미 공격 불가 상태다 -
            // 아직 실행하지 않은 예약 공격은 전부 폐기하고 새 Windup을 시작하지 않는다. 지금 재생 중인
            // 이번 공격의 Recovery는 그대로 자연스럽게 마무리한다(끊지 않는다).
            bool canAttack = Target.HasAttackableTarget;
            bool inputStillFresh = Time.time - lastInputTime < activeMotion.QueueExpireTimeout;
            if (canAttack && pendingAttacks > 0 && inputStillFresh)
            {
                StartWindup(); // 대기 중인 타격이 있고 입력이 이어지고 있으면 곧바로 다음 재생으로(모션은 여기서 새로 뽑힌다)
            }
            else
            {
                pendingAttacks = 0; // 입력이 끊겼거나 더 이상 공격 대상이 없으면 밀린 예약은 버리고 지금 재생만 마무리한다
                StartRecovery();
            }
        }

        private void StartRecovery()
        {
            attackPhase = AttackPhase.Recovery;
            attackPhaseTimer = 0f;
            // attackFrame은 Strike 시점의 hitFrameIndex에 이미 있다 - 여기서부터 마지막 프레임까지 이어 재생한다.
        }

        private void FinishSession()
        {
            attackPhase = AttackPhase.None;
            pendingAttacks = 0; // 정상 흐름이면 이미 0이지만 방어적으로 초기화
            activeAnimIndex = IdleIndex;
            currentFrame = 0;
            frameTimer = 0f;
            variantTimer = 0f;
            ApplyFrame();

            AttackEnded?.Invoke();
        }

        private void AdvanceAttack()
        {
            switch (attackPhase)
            {
                case AttackPhase.Windup:
                    AdvanceStep(() =>
                    {
                        attackFrame++;
                        ApplyAttackFrame();
                        TryFireCastCue(); // Hit보다 항상 먼저 판정 - 같은 프레임이어도 Cast가 먼저 발생한다.

                        if (attackFrame >= ActiveHitFrameIndex)
                        {
                            Strike();
                        }
                    });
                    break;

                case AttackPhase.Recovery:
                    if (attackFrame < activeMotionFrames.Length - 1)
                    {
                        AdvanceStep(() =>
                        {
                            attackFrame++;
                            ApplyAttackFrame();
                            TryFireCastCue(); // Cast Frame이 Hit Frame보다 뒤(Recovery 구간)에 있을 수도 있다.

                            if (attackFrame >= activeMotionFrames.Length - 1)
                            {
                                attackPhaseTimer = 0f; // 스텝 타이머 -> 유지 타이머로 전환하기 전 리셋
                            }
                        });
                    }
                    else
                    {
                        attackPhaseTimer += Time.deltaTime;
                        if (attackPhaseTimer >= activeMotion.EndFrameDuration)
                        {
                            FinishSession();
                        }
                    }
                    break;
            }
        }

        private void AdvanceStep(Action onStepComplete)
        {
            if (activeMotion.AnimationFps <= 0f) return;

            float step = 1f / activeMotion.AnimationFps;
            attackPhaseTimer += Time.deltaTime;

            if (attackPhaseTimer < step) return;
            attackPhaseTimer -= step;

            onStepComplete();
        }

        private void ApplyAttackFrame()
        {
            if (attackFrame < 0 || attackFrame >= activeMotionFrames.Length) return;
            spriteRenderer.sprite = activeMotionFrames[attackFrame];
        }

        // ---- Idle / Idle 변형 ----
        private void RollVariant()
        {
            int choice;
            if (usesProfileIdleEvents)
            {
                if (animations.Length <= 1 || UnityEngine.Random.value > profileIdleEventChance) return;
                choice = UnityEngine.Random.Range(1, animations.Length);
            }
            else
            {
                choice = UnityEngine.Random.Range(0, 4); // 기존 동작: 0 = idle 유지, 1/2/3 = a/b/c
                if (choice == 0) return;
                if (animations[choice].Frames.Length == 0) return;
            }

            playingVariant = true;
            activeAnimIndex = choice;
            currentFrame = 0;
            frameTimer = 0f;
            ApplyFrame();
        }

        private void AdvanceFrame()
        {
            Sprite[] frames = animations[activeAnimIndex].Frames;
            RuntimeFrameAnimation anim = animations[activeAnimIndex];
            if (frames.Length == 0 || anim.AnimationFps <= 0f) return;

            float frameDuration = 1f / anim.AnimationFps;
            frameTimer += Time.deltaTime;

            if (frameTimer < frameDuration) return;

            frameTimer -= frameDuration;
            currentFrame++;

            if (currentFrame >= frames.Length)
            {
                if (playingVariant)
                {
                    playingVariant = false;
                    activeAnimIndex = IdleIndex;
                    currentFrame = 0;
                    variantTimer = 0f;
                }
                else
                {
                    currentFrame = 0;
                }
            }

            ApplyFrame();
        }

        private void ApplyFrame()
        {
            Sprite[] frames = animations[activeAnimIndex].Frames;
            if (frames.Length == 0) return;
            spriteRenderer.sprite = frames[Mathf.Clamp(currentFrame, 0, frames.Length - 1)];
        }
    }
}
