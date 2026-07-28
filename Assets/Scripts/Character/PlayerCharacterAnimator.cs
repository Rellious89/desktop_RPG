using System;
using System.Collections.Generic;
using Common;
using DesktopWindow;
using UnityEngine;

namespace Character
{
    /// <summary>
    /// 기본 Idle(프로필의 Base Idle) 루프를 재생하다가, Idle Event Check Interval마다 Idle Event
    /// Chance 확률로 프로필에 등록된 Idle Event 중 하나를 균등 확률로 골라 한 번 재생하고 다시 기본
    /// Idle로 돌아온다. Idle Event 재생이 끝나면 다시 주기를 기다린 뒤 재판정한다.
    ///
    /// 공격은 키 입력 1회 = 타격 1회로 정확히 대응된다. 키 입력마다 대기열(pendingAttacks)에 하나씩
    /// 쌓이고, 공격 클립을 0번 프레임부터 hitFrameIndex까지 재생(Windup)한 뒤 타격(HitPoint)이
    /// 발생할 때마다 그 대기열을 하나씩 소비한다. 재생 도중 새 입력이 들어와도 애니메이션을
    /// 재시작하지 않고 대기열에만 추가되며, 타격 시점에 순서대로 소비된다.
    /// 타격 이후 대기 중인 입력이 있으면 즉시 Windup을 재시작(루프)하고, 없으면 hitFrameIndex
    /// 다음 프레임부터 마지막 프레임까지 이어서 재생(Recovery)한 뒤 Idle로 돌아간다.
    ///
    /// 각 애니메이션은 프레임 낱장 Sprite를 프로필 에셋에서 배열로 직접 받는다(아틀라스 런타임
    /// 슬라이싱 아님). 프레임 수는 배열 길이(frames.Length)가 그대로 정답이라 별도로 입력받지 않는다.
    /// Pivot/PPU도 각 스프라이트의 임포트 설정에 이미 들어있어서 여기서 따로 지정하지 않는다.
    ///
    /// 공격 가능한 Target이 하나도 없으면(Target.HasAttackableTarget == false) 새 키 입력을 아예
    /// 대기열에 올리지 않는다 - 처치 직후 Fade-out/리젠 대기/Fade-in 중에는 허공 공격이 시작되지
    /// 않는다. 이미 진행 중인 Windup~Recovery는 끊지 않고 그대로 마무리하되, Strike() 직후 다음
    /// Windup으로 이어갈지 판단할 때도 다시 한번 확인해서 - 마지막 남은 Target을 죽인 타격이었다면
    /// 그 뒤에 밀려 있던 예약 공격(pendingAttacks)은 전부 버리고 Recovery로 빠진다.
    ///
    /// 콤보 티어별 공격 모션 풀: 프로필의 Tier1/2/3 Pool(ComboTierAttackPool 에셋) 중
    /// ComboManager.CurrentTier에 대응하는 풀에서 매 StartWindup() 시점에 모션을 하나 완전 랜덤으로
    /// 뽑아 그 사이클(Windup -> Strike -> Recovery) 동안 그대로 쓴다 - 입력 처리/대기열/전환 규칙은
    /// 전혀 건드리지 않고 "어떤 프레임 배열을 재생할지"만 매 사이클마다 다시 고른다(직전 모션과 같아도
    /// 그대로 허용). 상위 티어 풀이 비어 있으면 한 단계씩 낮은 티어로 폴백한다(Tier3 -> Tier2 -> Tier1).
    ///
    /// <b>모션 제작 데이터의 원천은 CharacterMotionProfile 하나뿐이다.</b> Idle/Idle Event/공격 풀/
    /// Attack Movement 값은 전부 프로필 에셋에 있고, 이 컴포넌트는 그 값을 읽어 재생만 한다 - 같은
    /// 값을 씬 Inspector에 다시 직렬화해두는 경로는 없다. 프로필이 비어 있거나 재생 가능한 Base Idle이
    /// 없으면 임시 데이터로 조용히 동작하지 않고 오류를 남긴 뒤 스스로 비활성화된다.
    /// 공격 모션 데이터는 AttackMotionDefinition/ComboTierAttackPool 에셋에 있고, 여기서는
    /// IAttackMotion 인터페이스로만 다룬다.
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

        private const int IdleIndex = 0;

        /// <summary>attackFrameOverlayRenderer가 비어 있을 때 Awake에서 만들어 붙이는 자식 오브젝트 이름.</summary>
        private const string AttackFrameOverlayName = "AttackFrameOverlay";

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

        [Header("Character Motion Profile (필수)")]
        [Tooltip("이 캐릭터의 Idle/Idle Event/공격 풀/Attack Movement 제작값을 담은 프로필 에셋 - 모션 " +
                 "데이터의 유일한 원천이다. 비어 있으면 이 캐릭터는 오류를 남기고 비활성화된다.")]
        [SerializeField] private CharacterMotionProfile motionProfile;

        [Header("Frame-synced Overlay")]
        [Tooltip("공격 프레임과 같은 인덱스의 오버레이 스프라이트를 그릴 자식 SpriteRenderer. 비워두면 Awake에서 " +
                 "AttackFrameOverlay 자식 오브젝트를 한 번만 만들어 재사용한다(공격마다 생성/파괴하지 않는다).")]
        [SerializeField] private SpriteRenderer attackFrameOverlayRenderer;

        [Tooltip("오버레이(캐스팅 섬광/무기 잔상)에 쓸 Material. 비워두면 오버레이 렌더러가 이미 갖고 있는 " +
                 "Material(런타임 생성 시 Sprites/Default)을 그대로 쓴다. 본체에 외곽선 Material이 붙어도 " +
                 "오버레이에는 절대 복사되지 않는다.")]
        [SerializeField] private Material attackOverlayMaterial;

        [Header("Combat")]
        [Tooltip("기본 공격 1회(타격 1번)당 적용할 데미지량. 강공격/치명타 등 추가 계산식은 아직 없다.")]
        [SerializeField] private int basicAttackPower = 5;

        private SpriteRenderer spriteRenderer;
        private FlashOnCue flashOnCue;
        private HitEffectSpawner castEffectSpawner;
        private ProjectileSpawner projectileSpawner;

        // [0] = Base Idle(항상 존재), [1..] = 프로필의 Idle Events. Awake에서 프로필 기준으로 한 번만 만든다.
        private RuntimeFrameAnimation[] animations;
        private float idleEventCheckInterval = 4f;
        private float idleEventChance;

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

        // 본체 frames와 인덱스를 공유하는 오버레이 배열. 길이가 본체와 달라도 되고(범위 밖은 오버레이
        // 없음), 비어 있으면 이 공격에는 오버레이가 전혀 없다는 뜻이다.
        private Sprite[] activeMotionOverlayFrames = Array.Empty<Sprite>();

        // 이번 공격 인스턴스가 Cast Frame에 쏜 발사체. Hit Frame(Strike)에서 목표 위치로 스냅해 완료시킨다.
        // launchId를 함께 들고 있다가 대조하는 이유는 ProjectileMover.LaunchId 주석 참고 - 발사체가 이미
        // 스스로 회수돼 다른 공격에 재사용된 뒤라면 건드리지 않는다.
        private ProjectileMover activeProjectile;
        private int activeProjectileLaunchId;

        // 조준 대상(몬스터)의 HitEffectSpawner 캐시 - 대상이 바뀔 때만 GetComponent를 다시 한다.
        private Target cachedProjectileTarget;
        private HitEffectSpawner cachedProjectileTargetSpawner;

        // Cast/Hit Frame 순서가 잘못된 모션은 첫 발사 시도 때 한 번만 경고한다(연타마다 로그가 쏟아지지 않게).
        private readonly HashSet<IAttackMotion> warnedProjectileMotions = new HashSet<IAttackMotion>();

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

            if (!CharacterMotionProfile.IsPlayable(motionProfile))
            {
                // 조용히 임시 데이터로 동작시키지 않는다 - 프로필이 이 캐릭터의 유일한 모션 데이터
                // 원천이므로, 없으면 무엇을 재생해야 할지 알 수 없다. 오류를 남기고 스스로 꺼져서
                // 다른 캐릭터/시스템(입력, 콤보, 몬스터)에는 영향을 주지 않는다.
                Debug.LogError($"[PlayerCharacterAnimator] '{name}': Character Motion Profile이 없거나 재생 가능한 " +
                               "Base Idle 프레임이 없습니다. 이 캐릭터를 비활성화합니다 - Inspector의 Motion Profile을 확인하세요.", this);
                enabled = false;
                return;
            }

            // 발사체를 쓰는 공격이 하나도 없으면 이 스포너는 아무 일도 하지 않는다(풀도 만들지 않는다) -
            // 기존 씬/프리팹을 손대지 않아도 되도록 없으면 여기서 붙인다. Inspector에서 prewarmCount를
            // 조정하고 싶으면 미리 직접 붙여두면 된다.
            projectileSpawner = GetComponent<ProjectileSpawner>();
            if (projectileSpawner == null) projectileSpawner = gameObject.AddComponent<ProjectileSpawner>();
            EnsureAttackFrameOverlay();
            BuildRuntimeConfiguration();

            BuildResolvedPool(resolvedTier1, motionProfile.Tier1Pool);
            BuildResolvedPool(resolvedTier2, motionProfile.Tier2Pool);
            BuildResolvedPool(resolvedTier3, motionProfile.Tier3Pool);

            if (resolvedTier1.Count == 0)
            {
                // Idle은 재생할 수 있으므로 컴포넌트를 끄지는 않는다 - 공격만 영영 시작되지 않기 때문에
                // 무슨 일이 벌어지는지 알 수 있게 명시적으로 남긴다.
                Debug.LogError($"[PlayerCharacterAnimator] '{name}': 프로필의 Tier 1 Attack Pool에 재생 가능한 공격 " +
                               "모션이 하나도 없습니다 - 이 캐릭터는 공격하지 않습니다.", motionProfile);
            }

            activeAnimIndex = IdleIndex;
            currentFrame = 0;
            ApplyFrame();
        }

#if UNITY_EDITOR
        /// <summary>Play Mode에 들어가기 전에 Inspector에서 바로 알 수 있도록, 필수 데이터가 빠진
        /// 상태를 Edit Mode에서 경고로 남긴다 - 런타임에는 Awake가 같은 조건을 오류로 처리하고
        /// 컴포넌트를 비활성화한다.</summary>
        private void OnValidate()
        {
            if (Application.isPlaying) return;

            if (motionProfile == null)
            {
                Debug.LogWarning($"[PlayerCharacterAnimator] '{name}': Character Motion Profile이 비어 있습니다 - " +
                                 "Play Mode에서 이 캐릭터는 비활성화됩니다.", this);
                return;
            }
            if (!CharacterMotionProfile.IsPlayable(motionProfile))
            {
                Debug.LogWarning($"[PlayerCharacterAnimator] '{name}': 프로필 '{motionProfile.name}'에 Base Idle " +
                                 "프레임이 없습니다 - Play Mode에서 이 캐릭터와 Attack Movement가 모두 비활성화됩니다.", this);
            }
        }
#endif

        private void BuildRuntimeConfiguration()
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
            idleEventChance = motionProfile.IdleEventChance;
            idleEventCheckInterval = motionProfile.IdleEventCheckInterval;
        }

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

        /// <summary>Tier3 -> Tier2 -> Tier1 순으로 폴백한다 - 상위 티어 풀이 비어 있으면 한 단계씩
        /// 낮은 티어의 풀을 그대로 쓴다. Tier1까지 비어 있으면 Awake가 이미 오류를 남겼고, 여기서
        /// 돌려주는 빈 리스트를 BeginAttackSession()이 보고 공격 자체를 시작하지 않는다.</summary>
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
                if (variantTimer >= idleEventCheckInterval)
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
            // 콤보로 새 모션을 뽑았으면 오버레이도 그 모션 것으로 즉시 교체된다(직전 모션 것을 이어 쓰지 않는다).
            activeMotionOverlayFrames = activeMotion.OverlayFrames;

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

            TryLaunchProjectile();
        }

        /// <summary>이 공격에 발사체가 있으면 Cast Frame에서 정확히 한 번 발사한다(TryFireCastCue가 공격
        /// 인스턴스당 한 번만 호출되므로 여기서 별도 중복 방지가 필요 없다). ProjectilePrefab이 비어 있으면
        /// 아무 일도 하지 않고 즉시 돌아가므로, 기존 근접 공격의 실행 경로는 전혀 달라지지 않는다.
        ///
        /// 비행 시간은 (Hit Frame - Cast Frame) / FPS다 - 공격 템포를 늦추는 별도 속도 값이 없고, 발사체
        /// 때문에 애니메이션이나 다음 공격이 기다리는 일도 없다. 발사에 실패해도(대상 없음, 프레임 순서
        /// 오류 등) 공격 자체는 기존과 동일하게 계속 진행된다.</summary>
        private void TryLaunchProjectile()
        {
            GameObject prefab = activeMotion.ProjectilePrefab;
            if (prefab == null || projectileSpawner == null) return;

            int castFrame = ActiveCastFrameIndex;
            int hitFrame = ActiveHitFrameIndex;
            float fps = activeMotion.AnimationFps;
            if (hitFrame <= castFrame || fps <= 0f)
            {
                // 발사 시점과 도착 시점을 구분할 수 없는 설정 - 날아가는 구간 자체가 없으므로 생성하지 않고
                // 즉시 완료로 처리한다(기존 Hit 처리는 그대로 진행된다).
                if (warnedProjectileMotions.Add(activeMotion))
                {
                    Debug.LogWarning($"[PlayerCharacterAnimator] '{name}': 발사체 공격은 Cast Frame({castFrame})이 " +
                                     $"Hit Frame({hitFrame})보다 앞서야 합니다(FPS {fps}). 이번 공격은 발사체 없이 진행합니다.", this);
                }
                return;
            }

            if (!TryResolveProjectileTargetPosition(out Vector3 targetWorld)) return;

            Vector2 launchOffset = activeMotion.ProjectileLaunchOffset;
            // 캐릭터가 좌우 반전돼 있으면 시전 손 위치도 같이 뒤집힌다. 발사체 자체의 flipX는 상속하지
            // 않는다 - 진행 방향 회전은 ProjectileMover가 시작점/도착점만 보고 결정한다.
            if (spriteRenderer.flipX) launchOffset.x = -launchOffset.x;
            // TransformPoint를 쓰므로 오프셋은 캐릭터의 Actor Scale과 Stage 배율을 그대로 따라간다 -
            // 어떤 배율에서도 시전 손 위치에 그대로 붙어 있는다.
            Vector3 startWorld = transform.TransformPoint(launchOffset);

            float flightDuration = (hitFrame - castFrame) / fps;
            ReleaseActiveProjectile(); // 정상 흐름이면 이미 비어 있다 - 방어적으로 정리하고 새로 쏜다.
            activeProjectile = projectileSpawner.Launch(prefab, startWorld, targetWorld, flightDuration, activeMotion.ProjectileScale);
            activeProjectileLaunchId = activeProjectile != null ? activeProjectile.LaunchId : 0;
        }

        /// <summary>발사체의 도착 지점을 몬스터의 HitEffectSpawner 기준점으로 계산한다 - 공격 모션의
        /// Hit Effect Offset까지 그대로 반영해서 피격 이펙트와 같은 지점에 도착하게 한다(랜덤 지터는
        /// 이펙트 내부 표현이라 제외). 공격 가능한 대상이 없거나 그 대상에 HitEffectSpawner가 없으면
        /// false - 발사체 없이 기존 공격만 진행한다.</summary>
        private bool TryResolveProjectileTargetPosition(out Vector3 worldPosition)
        {
            worldPosition = default;
            if (!Target.TryGetAttackableTarget(out Target target)) return false;

            // Unity의 == 를 그대로 쓴다 - 캐시해둔 대상이 이미 파괴됐다면 새 대상과 다르다고 판정되어
            // 자동으로 다시 조회한다(리젠으로 몬스터 오브젝트 자체가 바뀌는 경우 대비).
            if (target != cachedProjectileTarget)
            {
                cachedProjectileTarget = target;
                cachedProjectileTargetSpawner = target.GetComponent<HitEffectSpawner>();
            }
            if (cachedProjectileTargetSpawner == null) return false;

            worldPosition = cachedProjectileTargetSpawner.GetImpactWorldPosition(activeMotion.HitEffectOffset);
            return true;
        }

        /// <summary>이번 공격의 발사체를 목표 위치로 스냅하고 완료시킨다. 프레임 드롭이나 Update 오차로
        /// 비행이 덜 끝났어도 Strike 순간에는 반드시 도착해 있게 만드는 보정이다 - 발사체가 피해를
        /// 발생시키는 구조가 아니므로 이 호출로 피격이 두 번 일어나는 일은 없다.</summary>
        private void CompleteActiveProjectile()
        {
            if (activeProjectile == null) return;

            if (activeProjectile.LaunchId == activeProjectileLaunchId) activeProjectile.CompleteNow();
            activeProjectile = null;
            activeProjectileLaunchId = 0;
        }

        /// <summary>완료 처리 없이 발사체를 즉시 회수한다(공격이 중간에 끊긴 경우).</summary>
        private void ReleaseActiveProjectile()
        {
            if (activeProjectile == null) return;

            if (activeProjectile.LaunchId == activeProjectileLaunchId) activeProjectile.Release();
            activeProjectile = null;
            activeProjectileLaunchId = 0;
        }

        private void Strike()
        {
            // HitPoint보다 먼저 - 발사체가 목표 지점에 도착한 그 프레임에 피격 이펙트가 겹쳐 나오게 한다.
            CompleteActiveProjectile();

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
            if (attackFrame < 0 || attackFrame >= activeMotionFrames.Length)
            {
                // 본체 프레임을 못 그리는 상황이면 오버레이도 남겨두지 않는다.
                SetOverlaySprite(null);
                return;
            }
            spriteRenderer.sprite = activeMotionFrames[attackFrame];
            ApplyAttackOverlayFrame();
        }

        /// <summary>본체가 방금 적용한 attackFrame과 "같은 인덱스"의 오버레이를 그대로 적용한다 - 오버레이는
        /// 자체 FPS나 재생 상태를 갖지 않으므로 여기서 시간 계산을 하지 않는다. 배열이 짧아 인덱스가 범위를
        /// 벗어나거나 그 요소가 null이면 그 프레임에는 오버레이가 없다는 뜻이라 sprite를 비운다.</summary>
        private void ApplyAttackOverlayFrame()
        {
            Sprite overlay = attackFrame >= 0 && attackFrame < activeMotionOverlayFrames.Length
                ? activeMotionOverlayFrames[attackFrame]
                : null;
            SetOverlaySprite(overlay);
        }

        /// <summary>공격마다 Instantiate/Destroy하지 않고 재사용하는 오버레이 renderer 하나에 sprite만 갈아
        /// 끼운다. 같은 sprite면 아무것도 하지 않아서 연타 중에도 추가 할당이 생기지 않는다.</summary>
        private void SetOverlaySprite(Sprite sprite)
        {
            if (attackFrameOverlayRenderer == null) return;
            if (ReferenceEquals(attackFrameOverlayRenderer.sprite, sprite)) return;
            attackFrameOverlayRenderer.sprite = sprite;
        }

        /// <summary>씬에 수동 배치가 없어도 동작하도록, 직렬화 참조가 비어 있으면 AttackFrameOverlay 자식
        /// 오브젝트와 SpriteRenderer를 여기서 한 번만 만든다. 오버레이는 본체와 같은 캔버스/Pivot/PPU로
        /// 제작하는 것이 전제라 로컬 Transform은 항등(zero/identity/one)으로 두고 코드 Offset을 주지 않는다.
        /// 정렬과 반전만 본체 기준으로 맞춘다 - 같은 Sorting Layer, Order는 본체보다 1 높게, flipX/flipY 동일.
        ///
        /// <b>본체의 Material은 복사하지 않는다.</b> 예전에는 sharedMaterial을 그대로 넘겨받았는데, 본체에
        /// 외곽선 Material(ActorOutlineController)이 붙는 순간 캐스팅 섬광과 무기 잔상까지 외곽선 처리되기
        /// 때문이다. 오버레이는 자기 자신의 Material을 유지한다 - 런타임에 새로 만든 SpriteRenderer는
        /// Sprites/Default를 기본으로 갖고 있고, 씬에 직접 배치한 오버레이라면 거기 지정한 Material을
        /// 그대로 쓴다. attackOverlayMaterial을 채워 두면 그 값으로 명시 지정한다.</summary>
        private void EnsureAttackFrameOverlay()
        {
            if (attackFrameOverlayRenderer == null)
            {
                Transform overlayTransform = transform.Find(AttackFrameOverlayName);
                if (overlayTransform == null)
                {
                    var overlayObject = new GameObject(AttackFrameOverlayName);
                    overlayObject.transform.SetParent(transform, false);
                    overlayTransform = overlayObject.transform;
                }
                // UnityEngine.Object의 null은 일반 C# null과 다르다. 컴포넌트가 없는 경우에도
                // null 병합 연산자(??)가 Unity의 커스텀 null 판정을 건너뛸 수 있으므로 명시적으로
                // 검사한다. 이미 이름만 같은 자식이 존재하는 씬/프리팹도 여기서 자동 복구한다.
                attackFrameOverlayRenderer = overlayTransform.GetComponent<SpriteRenderer>();
                if (attackFrameOverlayRenderer == null)
                {
                    attackFrameOverlayRenderer = overlayTransform.gameObject.AddComponent<SpriteRenderer>();
                }
            }

            Transform overlay = attackFrameOverlayRenderer.transform;
            overlay.localPosition = Vector3.zero;
            overlay.localRotation = Quaternion.identity;
            overlay.localScale = Vector3.one;

            // 정렬/반전은 런타임에 바뀌지 않으므로(FlashOnCue는 color만 건드린다) 여기서 한 번만 복사한다.
            // Material은 의도적으로 복사 대상에서 제외한다 - 위 주석 참고.
            if (attackOverlayMaterial != null)
            {
                attackFrameOverlayRenderer.sharedMaterial = attackOverlayMaterial;
            }
            attackFrameOverlayRenderer.sortingLayerID = spriteRenderer.sortingLayerID;
            attackFrameOverlayRenderer.sortingOrder = spriteRenderer.sortingOrder + 1;
            attackFrameOverlayRenderer.flipX = spriteRenderer.flipX;
            attackFrameOverlayRenderer.flipY = spriteRenderer.flipY;
            attackFrameOverlayRenderer.sprite = null;
        }

        /// <summary>공격 중 캐릭터가 비활성화되면(교체/파괴 직전 포함) 마지막 오버레이가 화면에 남지 않게 지우고,
        /// 아직 날아가고 있던 발사체도 즉시 회수한다 - 발사체는 CombatFxRoot의 자식이라 캐릭터가 사라져도
        /// 혼자 남아 계속 날아갈 수 있기 때문이다(Strike가 오지 않으므로 완료 처리도 되지 않는다).</summary>
        private void OnDisable()
        {
            SetOverlaySprite(null);
            ReleaseActiveProjectile();
        }

        // ---- Idle / Idle 변형 ----
        private void RollVariant()
        {
            // 등록된 Idle Event가 하나도 없으면(Base Idle 하나뿐이면) 아무것도 하지 않고, Chance 판정에
            // 성공하면 Idle Event 중 하나를 완전 균등 확률로 골라 한 번 재생한다.
            if (animations.Length <= 1 || UnityEngine.Random.value > idleEventChance) return;
            int choice = UnityEngine.Random.Range(1, animations.Length);

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
            // Idle 계열은 오버레이를 쓰지 않는다 - 공격이 끝나 여기로 돌아온 순간 이전 오버레이가 남지 않게 지운다.
            SetOverlaySprite(null);
            Sprite[] frames = animations[activeAnimIndex].Frames;
            if (frames.Length == 0) return;
            spriteRenderer.sprite = frames[Mathf.Clamp(currentFrame, 0, frames.Length - 1)];
        }
    }
}
