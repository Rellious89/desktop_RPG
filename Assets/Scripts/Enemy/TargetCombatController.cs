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
    ///
    /// <b>모션 제작 데이터의 원천은 MonsterMotionProfile 하나뿐이다.</b> Base Idle/Idle Event/Hit/
    /// Defeat 프레임, Hit Reaction 수치, Damage Number 연출값이 전부 프로필 에셋에 있고, 이 컴포넌트는
    /// 그 값을 읽어 재생만 한다 - 같은 값을 씬 Inspector에 다시 직렬화해두는 fallback 경로는 없다.
    /// 프로필이 비어 있거나 Base Idle/Hit 프레임이 없으면 조용히 빈 상태로 서 있는 대신 오류를 남기고
    /// 스스로 비활성화된다(랜덤 리젠 후보도 같은 기준으로 걸러낸다).
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

        [Header("Monster Motion Profile (필수)")]
        [Tooltip("이 몬스터의 Idle/Idle Event/Hit/Defeat 프레임과 Hit Reaction/Damage Number 제작값을 담은 " +
                 "프로필 에셋 - 모션 데이터의 유일한 원천이다. 비어 있으면 이 몬스터는 오류를 남기고 비활성화된다.")]
        [SerializeField] private MonsterMotionProfile motionProfile;

        [Header("Combat Stage Layout")]
        [Tooltip("시작 위치를 Monster Slot Position + 이 몬스터 프로필의 Actor Offset으로 계산하고, " +
                 "Actor Scale도 함께 적용한다(Motion Editor Preview와 동일한 공식). 비어 있으면 씬에 " +
                 "배치된 현재 Transform 위치/스케일을 그대로 쓴다 - 프로필이 랜덤 리젠으로 바뀌면 배치가 " +
                 "어긋나므로 연결하는 것을 권장한다.")]
        [SerializeField] private CombatStageLayout stageLayout;

        [Header("Random Respawn Profile Test")]
        [Tooltip("켜면 리젠이 시작될 때 respawnProfilePool 중 현재 motionProfile을 제외한 다른 프로필로 " +
                 "동일 확률 랜덤 교체한다. 정식 몬스터 테이블/리젠 시스템이 생기기 전까지의 테스트용 기능이라 " +
                 "꺼져 있으면 기존처럼 항상 같은 프로필로 리젠한다.")]
        [SerializeField] private bool randomizeProfileOnRespawn;

        [Tooltip("랜덤 리젠 후보 프로필 목록(테스트용). 배열 순서는 확률에 영향을 주지 않는다 - null, " +
                 "중복, 현재 프로필, Base Idle/Hit 프레임이 비어 있는 프로필은 자동으로 후보에서 제외된다.")]
        [SerializeField] private MonsterMotionProfile[] respawnProfilePool;

        private SpriteRenderer spriteRenderer;
        private FlashOnCue flashOnCue;
        private Target target;
        private DamageNumberSpawner damageNumberSpawner;
        private HitEffectSpawner hitEffectSpawner;

        // [0] = Base Idle(항상 존재), [1..] = Motion Profile의 Idle Events - PlayerCharacterAnimator의
        // animations 배열과 같은 구조. 전부 프로필 데이터로만 채워진다(씬 직접 설정 fallback 없음).
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

        // TrySwitchToRandomRespawnProfile()이 이번 리젠에서 실제로 프로필을 바꿨을 때만 켜진다.
        // HandleRespawned()가 이 플래그를 보고 Hit Recovery 대신 Base Idle을 유지하도록 분기한다 -
        // 새 프로필로 교체된 몬스터가 Fade-in 직후 이전 몬스터의 Hit Recovery 프레임을 잠깐 보여주는
        // 것을 막기 위함이다.
        private bool profileChangedForCurrentRespawn;

        private Vector3 basePosition;
        private bool shaking;
        private float shakeStartTime;

        // 씬에 배치된 최초 localPosition. stageLayout이 없는 몬스터의 "전투 위치" 기준점으로 쓴다 -
        // 대기 위치로 옮겨 다니는 동안에도 이 값은 절대 바뀌지 않는다.
        private Vector3 sceneAuthoredLocalPosition;

        // 기준 위치(basePosition) 자체를 시간에 걸쳐 옮기는 이동 tween - 대기 위치에서 전투 위치로
        // 승격할 때 쓴다. 피격 흔들림은 언제나 "그 시점의 basePosition"에 오프셋을 더하는 방식이라
        // (UpdateShake), 이동 중에 맞아도 서로 덮어쓰지 않고 자연스럽게 합쳐진다.
        private bool moving;
        private Vector3 moveStart;
        private Vector3 moveEnd;
        private float moveDuration;
        private float moveElapsed;

        // 처치/리젠 알파 Fade 대상. Awake에서 자신 및 자식의 SpriteRenderer를 전부 수집한다 -
        // ReceivePoint/ImpactPoint/DamageAnchor 같은 비시각 앵커는 SpriteRenderer가 없으므로 자동으로
        // 제외된다. RGB는 절대 건드리지 않고 각자의 원래 알파만 개별로 저장해 그 값 기준으로 Fade한다.
        private SpriteRenderer[] visualRenderers;
        private float[] originalAlphas;
        private Coroutine fadeRoutine;

        public MonsterMotionProfile MotionProfile => motionProfile;

        /// <summary>이 몬스터의 Target. 대기열 관리자가 처치 이벤트를 구독할 때 쓴다.</summary>
        public Target CombatTarget
        {
            get
            {
                EnsureRuntimeReferences();
                return target;
            }
        }

        /// <summary>지금 이 몬스터가 맡은 역할 - 실제 값은 Target 하나가 소유한다(공격 가능 레지스트리와
        /// 같은 값을 보게 해서 두 곳이 어긋날 여지를 없앤다).</summary>
        public TargetEngagementRole EngagementRole =>
            CombatTarget != null ? target.EngagementRole : TargetEngagementRole.Current;

        /// <summary>이 몬스터가 재생 가능한 모션 프로필을 갖고 있는지 - 관리자가 슬롯 구성을 검증할 때
        /// Awake의 자기 비활성화 기준과 똑같은 조건으로 확인할 수 있게 열어둔다.</summary>
        public bool HasPlayableMotionProfile => IsProfilePlayable(motionProfile);

        /// <summary>역할이 실제로 바뀐 직후 발생(Current/Standby/Exiting). Phase 4의 알파/위치 연출
        /// 컴포넌트가 이 신호 하나만 보고 붙을 수 있도록 남겨둔 확장점이다 - 이 컴포넌트 자체는 역할에
        /// 따른 시각 변화를 아직 만들지 않는다.</summary>
        public event Action<TargetEngagementRole> EngagementRoleChanged;

        private bool runtimeReferencesResolved;

        private void Awake()
        {
            EnsureRuntimeReferences();

            if (!IsProfilePlayable(motionProfile))
            {
                // 프로필이 이 몬스터의 유일한 모션 데이터 원천이다 - 없으면 조용히 빈 애니메이션으로
                // 서 있는 대신 오류를 남기고 스스로 꺼진다(OnEnable에서 HitPoint를 구독하지 않으므로
                // 피격 반응/데미지 숫자/이펙트가 전혀 실행되지 않는다).
                //
                // 이때 Target까지 함께 끄는 것이 중요하다. Target은 이 컴포넌트와 독립적으로 내구도와
                // 정적 aliveCount를 관리하므로, 그대로 두면 Target.HasAttackableTarget이 계속 true라
                // 플레이어가 "아무 반응도 없는 몬스터"를 계속 때리고 처치/리젠까지 눈에 보이지 않게
                // 진행된다. Awake 단계에서 끄면 Target.OnEnable이 아예 호출되지 않아 activeTargets에
                // 등록조차 되지 않고, 이미 등록된 뒤라면 OnDisable이 aliveCount를 정확히 되돌린다.
                Debug.LogError($"[TargetCombatController] '{name}': Monster Motion Profile이 없거나 Base Idle/Hit " +
                               "프레임이 비어 있습니다. 이 몬스터와 Target을 비활성화합니다(공격 대상에서 제외) - " +
                               "Inspector의 Motion Profile을 확인하세요.", this);
                if (target != null) target.enabled = false;
                enabled = false;
                return;
            }
            if (stageLayout == null)
            {
                Debug.LogWarning($"[TargetCombatController] '{name}': Combat Stage Layout이 비어 있어 씬에 배치된 현재 " +
                                 "Transform 위치/스케일을 그대로 사용합니다(랜덤 리젠으로 프로필이 바뀌어도 갱신되지 않습니다).", this);
            }

            basePosition = ResolveInitialBasePosition();
            transform.localPosition = basePosition;
            ApplyActorScale();

            spriteRenderer.flipX = motionProfile.SpriteFlipX;

            BuildRuntimeConfiguration();

            idleCurrentFrame = 0;
            ApplyIdleFrame();
        }

        /// <summary>컴포넌트 참조와 Fade 대상 Renderer/원래 알파를 한 번만 수집한다. Awake에서 부르지만,
        /// 대기열 관리자의 public API가 이 컴포넌트의 Awake보다 먼저 호출될 수 있으므로(같은 프레임의
        /// Awake 순서는 보장되지 않는다) 그 진입점들도 전부 이 메서드를 먼저 거친다 - 두 번 이상
        /// 호출해도 아무 일도 하지 않으므로 Fade 도중에 원래 알파가 덮어써지는 일은 없다.</summary>
        private void EnsureRuntimeReferences()
        {
            if (runtimeReferencesResolved) return;
            runtimeReferencesResolved = true;

            // 씬에 배치된 최초 위치는 여기서 딱 한 번 캐시한다 - 대기열이 위치를 옮기기 전의 값이어야
            // "전투 위치" fallback으로 쓸 수 있다.
            sceneAuthoredLocalPosition = transform.localPosition;

            spriteRenderer = GetComponent<SpriteRenderer>();
            flashOnCue = GetComponent<FlashOnCue>();
            target = GetComponent<Target>();
            damageNumberSpawner = GetComponent<DamageNumberSpawner>();
            hitEffectSpawner = GetComponent<HitEffectSpawner>();

            visualRenderers = GetComponentsInChildren<SpriteRenderer>(true);
            originalAlphas = new float[visualRenderers.Length];
            for (int i = 0; i < visualRenderers.Length; i++)
            {
                originalAlphas[i] = visualRenderers[i].color.a;
            }
        }

        /// <summary>프로필이 이 몬스터에 필요한 최소 조건을 갖췄는지 - Base Idle과 Hit 양쪽에 재생
        /// 가능한 프레임이 있어야 한다. Defeat는 비어 있어도 정상이다(프레임 없이 페이드아웃만 한다).
        /// 랜덤 리젠 후보를 고를 때도 같은 기준을 쓴다.</summary>
        private static bool IsProfilePlayable(MonsterMotionProfile profile)
        {
            return profile != null
                   && profile.BaseIdle != null && profile.BaseIdle.Frames.Length > 0
                   && profile.Hit != null && profile.Hit.Frames.Length > 0;
        }

        /// <summary>대기열 관리자가 프로필 풀 후보를 거를 때 쓰는 공개 판정 - 런타임 교체 후보에
        /// 요구하는 조건은 랜덤 리젠 후보와 완전히 동일하다(Base Idle/Hit 프레임 존재 + Base Idle 첫
        /// 프레임이 null이 아님). "교체하고 나서야 재생할 프레임이 없다"를 발견하는 일이 없도록 후보
        /// 단계에서 같은 기준으로 걸러낸다.</summary>
        public static bool IsProfileUsable(MonsterMotionProfile profile)
        {
            return IsProfilePlayable(profile) && profile.BaseIdle.Frames[0] != null;
        }

        /// <summary>Preview(DrawPairedStage)와 같은 공식: Slot + Actor Offset. stageLayout이 없으면
        /// <b>씬에 배치된 최초 위치</b>(Awake에서 캐시한 값)를 기준점으로 쓴다 - 지금 Transform 위치를
        /// 쓰면 대기 위치로 옮겨둔 몬스터의 "전투 위치"를 물었을 때 대기 위치가 그대로 돌아오므로,
        /// 대기열이 자리를 오갈 때마다 기준선이 흘러가 버린다.</summary>
        private Vector3 ResolveInitialBasePosition()
        {
            if (stageLayout == null) return sceneAuthoredLocalPosition;

            Vector2 offset = motionProfile.Preview.ActorOffset;
            Vector2 slot = stageLayout.MonsterSlotPosition;
            return new Vector3(slot.x + offset.x, slot.y + offset.y, sceneAuthoredLocalPosition.z);
        }

        /// <summary>지금 프로필 기준의 "전투 위치"(Current가 서야 할 자리)를 돌려준다 - stageLayout이
        /// 연결돼 있으면 Monster Slot + Actor Offset, 없으면 씬에 배치된 최초 위치다. 대기열 관리자가
        /// 전용 combatPosition Transform을 갖고 있지 않을 때의 안전한 fallback이다. 아무 상태도 바꾸지
        /// 않으며, 프로필이 바뀌면(Actor Offset이 달라지면) 결과도 함께 달라진다.</summary>
        public Vector3 ResolveCombatBaseLocalPosition()
        {
            EnsureRuntimeReferences();
            return ResolveInitialBasePosition();
        }

        private void ApplyActorScale()
        {
            if (stageLayout == null) return;

            float scale = motionProfile.Preview.ActorScale;
            transform.localScale = new Vector3(scale, scale, 1f);
        }

        /// <summary>Motion Editor의 "Apply Preview Layout to Open Stage"나 향후 런타임 배치 갱신이
        /// 호출하는 진입점 - basePosition과 실제 Transform 위치를 함께 새 기준점으로 맞추고, 진행 중이던
        /// 흔들림은 안전하게 취소한다(그대로 두면 다음 프레임에 옛 basePosition 기준으로 튈 수 있다).
        /// 피격 반응이 진행 중이 아닐 때(선택/교체/초기화 시점)만 호출해야 한다.</summary>
        public void SetPresentationBasePosition(Vector3 localPosition)
        {
            EnsureRuntimeReferences();

            moving = false; // 진행 중이던 이동 tween보다 명시적 지정이 항상 우선한다.
            basePosition = localPosition;
            transform.localPosition = localPosition;
            shaking = false;
        }

        /// <summary>기준 위치를 duration 동안 targetLocalPosition으로 옮긴다(대기 위치 -> 전투 위치
        /// 승격 이동). <b>위치의 소유자는 언제나 이 컴포넌트다</b> - 관리자는 목적지와 시간만 주고
        /// Transform을 직접 건드리지 않는다. 이동 중 피격이 들어와도 흔들림은 "그 시점의 기준 위치"에
        /// 오프셋을 더하는 방식이라 순간이동하거나 옛 기준 위치로 고착되지 않는다. duration이 0 이하면
        /// 즉시 이동과 동일하다.</summary>
        public void BeginPresentationMove(Vector3 targetLocalPosition, float duration)
        {
            EnsureRuntimeReferences();

            if (duration <= 0f)
            {
                SetPresentationBasePosition(targetLocalPosition);
                return;
            }

            moveStart = basePosition;
            moveEnd = targetLocalPosition;
            moveDuration = duration;
            moveElapsed = 0f;
            moving = true;
        }

        /// <summary>이동 tween 진행. 흔들림 중에는 Transform을 직접 쓰지 않고 기준 위치만 갱신한다 -
        /// 같은 프레임 뒤에 오는 UpdateShake가 "새 기준 위치 + 흔들림 오프셋"으로 최종 위치를 정한다.</summary>
        private void UpdatePresentationMove()
        {
            if (!moving) return;

            moveElapsed += Time.deltaTime;
            float t = Mathf.Clamp01(moveElapsed / moveDuration);
            float eased = t * t * (3f - 2f * t); // SmoothStep - 시작/끝이 부드럽다.
            basePosition = Vector3.LerpUnclamped(moveStart, moveEnd, eased);

            if (t >= 1f)
            {
                basePosition = moveEnd;
                moving = false;
            }

            if (!shaking) transform.localPosition = basePosition;
        }

        /// <summary>이동 tween이 진행 중인지 - 관리자/테스트가 승격 이동 완료를 확인할 때 쓴다.</summary>
        public bool IsPresentationMoving => moving;

        /// <summary>Idle/Idle Event/Hit/Hit Reaction/Defeat를 전부 motionProfile에서만 가져온다 -
        /// 씬 컴포넌트에 같은 값을 다시 직렬화해둔 fallback 경로는 없다(호출 전에 Awake/
        /// ApplyRuntimeMotionProfile이 IsProfilePlayable로 이미 검증한다). Defeat만 비어 있어도
        /// 정상이라 조용히 빈 배열이 된다 - Defeat 프레임 없이 페이드아웃만 하는 것은 정상 동작이다.</summary>
        private void BuildRuntimeConfiguration()
        {
            MonsterMotionProfile.FrameClip profileIdle = motionProfile.BaseIdle;
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

            hitFrames = motionProfile.Hit.Frames;
            MonsterMotionProfile.HitReactionSettings reaction = motionProfile.HitReaction;
            hitHoldFrame = reaction.HoldFrame;
            hitRecoveryFrame = reaction.RecoveryFrame;
            hitRecoveryDuration = reaction.RecoveryDuration;
            hitHoldTimeout = reaction.HoldTimeout;
            hitShakeStrength = reaction.ShakeStrength;
            hitShakeFrequency = reaction.ShakeFrequency;
            hitShakeDecayDuration = reaction.ShakeDecayDuration;

            defeatFrames = motionProfile.Defeat != null ? motionProfile.Defeat.Frames : Array.Empty<Sprite>();
        }

#if UNITY_EDITOR
        /// <summary>Edit Mode에서 motionProfile을 바꾸면 SpriteRenderer 미리보기(Sprite/Flip X)와
        /// stageLayout이 연결된 몬스터의 Transform Scale(Actor Scale)을 갱신한다. OnValidate 안에서
        /// 바로 Undo.RecordObject/SetDirty를 부르면 Unity의 Undo 처리 재진입 오류가 날 수 있어
        /// delayCall로 한 틱 미룬다 - 그 사이 오브젝트가 파괴되거나 Play Mode로 들어갔을 수 있어
        /// 실행 시점에 다시 확인한다.</summary>
        private void OnValidate()
        {
            UnityEditor.EditorApplication.delayCall += RefreshEditorPreviewPresentation;
        }

        private void RefreshEditorPreviewPresentation()
        {
            if (this == null || Application.isPlaying) return;

            if (stageLayout == null)
            {
                Debug.LogWarning($"[TargetCombatController] '{name}': Combat Stage Layout이 비어 있습니다 - " +
                                 "Motion Editor Preview와 배치가 어긋날 수 있습니다.", this);
            }
            if (!IsProfilePlayable(motionProfile))
            {
                Debug.LogWarning($"[TargetCombatController] '{name}': Monster Motion Profile이 없거나 Base Idle/Hit " +
                                 "프레임이 비어 있습니다 - Play Mode에서 이 몬스터는 비활성화됩니다.", this);
                return; // 프로필이 불완전하면 Sprite/Flip X/Scale 전부 기존 값 유지
            }

            RefreshEditorPreviewSpriteAndFlip();
            RefreshEditorPreviewScale();
        }

        /// <summary>Sprite와 Flip X는 서로 독립적으로 판정한다 - Base Idle 첫 프레임이 비어 있거나
        /// null이어도 Flip X는 프로필 값 그대로 적용한다(Flip X는 BaseIdle 프레임 유무와 무관한
        /// 프로필 설정이다). Sprite는 유효한 첫 프레임이 있을 때만 갱신하고, 없으면 기존 SpriteRenderer
        /// 값을 그대로 둔다.</summary>
        private void RefreshEditorPreviewSpriteAndFlip()
        {
            SpriteRenderer editorRenderer = GetComponent<SpriteRenderer>();
            if (editorRenderer == null) return;

            MonsterMotionProfile.FrameClip baseIdle = motionProfile.BaseIdle;
            Sprite previewSprite = baseIdle != null && baseIdle.Frames.Length > 0 ? baseIdle.Frames[0] : null;
            bool previewFlipX = motionProfile.SpriteFlipX;

            bool spriteChanged = previewSprite != null && editorRenderer.sprite != previewSprite;
            bool flipChanged = editorRenderer.flipX != previewFlipX;
            if (!spriteChanged && !flipChanged) return;

            UnityEditor.Undo.RecordObject(editorRenderer, "Update Monster Motion Preview");
            if (spriteChanged) editorRenderer.sprite = previewSprite;
            if (flipChanged) editorRenderer.flipX = previewFlipX;
            UnityEditor.EditorUtility.SetDirty(editorRenderer);
        }

        /// <summary>런타임 ApplyActorScale()과 동일한 공식 - stageLayout이 연결된 몬스터에서만 프로필
        /// Actor Scale을 Edit Mode Transform에 반영한다. stageLayout이 없는 기존 몬스터는 수동으로
        /// 설정한 Transform Scale을 그대로 둔다(런타임 규칙과 동일).</summary>
        private void RefreshEditorPreviewScale()
        {
            if (stageLayout == null) return;

            float actorScale = motionProfile.Preview.ActorScale;
            Vector3 previewScale = new Vector3(actorScale, actorScale, 1f);
            if (transform.localScale == previewScale) return;

            UnityEditor.Undo.RecordObject(transform, "Update Monster Motion Preview");
            transform.localScale = previewScale;
            UnityEditor.EditorUtility.SetDirty(transform);
        }
#endif

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

            CancelFade();
        }

        private void OnHitPoint(AttackHitCue cue)
        {
            // <b>역할 게이트가 가장 먼저다.</b> HitPoint는 씬의 모든 활성 컨트롤러가 함께 받는 정적
            // 이벤트라, 대기열 몬스터(Standby)나 퇴장 중인 몬스터(Exiting)도 같은 스냅샷 안에서 이
            // 핸들러를 호출받는다 - 그쪽은 살아 있고(IsDefeated == false) 화면에도 보이므로 처치
            // 상태만으로는 절대 걸러지지 않는다. 구독 해제에만 기대지 않고 여기서 명시적으로 막아,
            // 승격/강등이 어느 시점에 일어나든 "Current가 아닌 몬스터가 맞는" 경우가 원천적으로 없게 한다.
            //
            // 진입 시점 상태를 기준으로 판정하는 규칙은 그대로다: 이 시점에 Current로 살아 있었다면,
            // 아래에서 ApplyDamage로 처치를 유발해 IsDefeated가 true로 바뀌더라도 피격 반응/데미지
            // 숫자/이펙트/ReceiveImpact까지 반드시 끝까지 표시한다. 반대로 진입 시점에 Current가
            // 아니었다면 damage/반응/숫자/이펙트/ReceiveImpact 어느 것도 실행되지 않는다.
            if (target.EngagementRole != TargetEngagementRole.Current) return;
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

            // Damage Number Offset은 타격 시점의 최종 위치(현재 transform - CombatStageLayout/Actor
            // Offset이 이미 반영된 값) 기준으로 변환해서 쓴다. Shake는 이 시점에는 아직 시작 전
            // (TriggerShake는 방금 shaking 플래그만 켰을 뿐 위치는 다음 프레임 UpdateShake부터 움직인다)
            // 이라 "타격 시점의" 위치가 곧 흔들리기 직전의 기준 위치와 같다 - Hit Effect/Receive Point와
            // 동일한 타이밍 규칙이다. 위치뿐 아니라 Jitter/Rise Distance/Duration/색상/폰트 크기/
            // Sorting Order까지 전부 몬스터 프로필이 소유한다(DamageNumberSpawner에는 같은 값이 없다).
            MonsterMotionProfile.HitReactionSettings hitReaction = motionProfile.HitReaction;
            Vector3 damageNumberCenter = transform.TransformPoint(hitReaction.DamageNumberOffset);
            var damageNumberPresentation = new DamageNumberPresentation(
                hitReaction.DamageNumberRandomHorizontalJitter,
                hitReaction.DamageNumberRiseDistance,
                hitReaction.DamageNumberDuration,
                hitReaction.DamageNumberTextColor,
                hitReaction.DamageNumberFontSize,
                hitReaction.DamageNumberSortingOrder);
            damageNumberSpawner.Spawn(cue.Damage, damageNumberCenter, damageNumberPresentation);
            // Hit Effect는 공격 모션(AttackMotionDefinition)이 단독으로 소유한다 - prefab이 비어 있으면
            // "이 공격에는 타격 이펙트가 없다"는 뜻이고, 스포너가 대신 채워 넣는 기본 이펙트는 없다.
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
        /// 아니다(OnRespawned는 별도로 온다) - 이전 몬스터가 완전히 투명해진 시점이므로 이때 랜덤 프로필
        /// 교체를 시도한 뒤(randomizeProfileOnRespawn이 꺼져 있거나 후보가 없으면 아무 일도 하지 않는다),
        /// 이전 Hit/Defeated 프레임이 Fade-in 동안 노출되지 않게 먼저 Idle 기준 자세로 정리하고 알파를
        /// 원래 값으로 페이드한다.</summary>
        private void HandleRespawnStarted(string targetId)
        {
            TrySwitchToRandomRespawnProfile();

            ExitToIdle();
            StartFade(toOriginal: true, duration: target.RespawnFadeDuration);
        }

        private void HandleRespawned(string targetId)
        {
            if (profileChangedForCurrentRespawn)
            {
                // 새 프로필로 교체된 리젠: Base Idle 초기화는 이미 HandleRespawnStarted()의
                // ExitToIdle()에서 끝났고, Fade-in 동안 계속 재생되고 있었다 - 여기서 다시 ExitToIdle()을
                // 부르면 진행 중이던 Idle이 0번 프레임으로 튀며 끊겨 보이므로 플래그만 해제하고
                // 지금 재생 중인 Idle 상태를 그대로 둔다. Hit Recovery는 재생하지 않는다.
                profileChangedForCurrentRespawn = false;
                return;
            }

            EnterRecovery(); // 기존 복귀 흐름(Recovery -> Idle)을 그대로 재사용한다.
        }

        // ---------------------------------------------------------------------------------------
        // 대기열(Encounter Queue) 관리자 전용 API
        //
        // 아래 다섯 메서드가 MonsterEncounterQueue가 이 몬스터를 다루는 유일한 통로다 - 관리자는
        // Target이나 SpriteRenderer를 직접 건드리지 않는다. 전부 이벤트를 발생시키지 않는 명시적
        // 호출이라(역할 변경 알림인 EngagementRoleChanged 제외) 처치/보상 구독자와 완전히 분리돼 있다.
        // ---------------------------------------------------------------------------------------

        /// <summary>이 몬스터를 대기열 관리 대상으로 편입시킨다 - 처치 후 자체 리스폰을 멈추고(이후
        /// 재사용 시점은 관리자가 정한다) 지정된 역할로 시작한다. 관리자가 슬롯 구성을 전부 검증한
        /// 뒤에만 호출해야 한다: 호출 전까지 이 몬스터는 기존 standalone 몬스터와 완전히 동일하게
        /// 동작하므로, 구성이 잘못됐을 때 관리자가 그냥 손을 떼면 아무것도 망가지지 않는다.</summary>
        public void JoinEncounter(TargetEngagementRole role)
        {
            EnsureRuntimeReferences();
            if (target == null) return;

            target.ConfigureLifecycle(TargetLifecycleMode.EncounterManaged);
            SetEngagementRole(role);
        }

        /// <summary>대기열 관리에서 빼내 기존 standalone 동작(자체 리스폰 + Current 역할)으로 되돌린다.
        /// 관리자가 해체되거나 이 몬스터를 더 이상 소유하지 않을 때 쓴다.</summary>
        public void LeaveEncounter()
        {
            EnsureRuntimeReferences();
            if (target == null) return;

            target.ConfigureLifecycle(TargetLifecycleMode.StandaloneSelfRespawn);
            SetEngagementRole(TargetEngagementRole.Current);
        }

        /// <summary>역할만 바꾼다 - 체력/처치 상태/포즈/알파는 그대로 두고 공격 가능 레지스트리만 즉시
        /// 갱신된다(이 호출이 끝난 시점부터 Target.HasAttackableTarget이 새 역할을 반영한다).
        /// 처치/보상 이벤트는 발생하지 않는다.</summary>
        public void SetEngagementRole(TargetEngagementRole role)
        {
            EnsureRuntimeReferences();
            if (target == null || target.EngagementRole == role) return;

            target.SetEngagementRole(role);
            EngagementRoleChanged?.Invoke(role);
        }

        /// <summary>이 몬스터를 다음 등장에 재사용할 수 있도록 상태를 전부 초기화한다 - 체력/처치 상태
        /// (Target.PrepareForEncounter) + 역할 + 진행 중이던 Fade/흔들림/피격 자세까지 한 번에 되돌리고,
        /// 프로필 교체가 필요하면 같은 호출에서 처리한다. <b>어떤 처치/보상 이벤트도 발생하지 않는다.</b>
        ///
        /// <b>호출 순서 규칙</b>: 처치 Fade-out이 진행 중인 슬롯을 되살리는 유일한 경로가 이 메서드다 -
        /// 여기서 Fade를 취소하고 알파를 원래 값으로 되돌리기 전에 <see cref="Target.PrepareForEncounter"/>가
        /// 먼저 IsDefeated를 해제한다. 반대로 <see cref="ApplyCurrentVisual"/>은 아직 처치 상태인 슬롯의
        /// 알파를 절대 건드리지 않으므로(경고 후 무시), 진행 중인 사망 연출을 우발적으로 덮어쓰는 경로가
        /// 없다. 역할별 알파/위치는 이 호출 뒤에 관리자가 명시적으로 적용한다.</summary>
        public void PrepareForEncounter(TargetEngagementRole role, MonsterMotionProfile profileOverride = null)
        {
            EnsureRuntimeReferences();
            if (target == null) return;

            if (profileOverride != null && profileOverride != motionProfile)
            {
                if (IsProfilePlayable(profileOverride))
                {
                    ApplyRuntimeMotionProfile(profileOverride);
                }
                else
                {
                    Debug.LogWarning($"[TargetCombatController] '{name}': 대기열이 지정한 교체 프로필 " +
                                     $"'{profileOverride.name}'에 Base Idle/Hit 프레임이 없어 무시하고 기존 프로필을 유지합니다.", this);
                }
            }

            target.PrepareForEncounter(role);

            CancelFade();
            ApplyTargetAlphas(toOriginal: true);

            moving = false;
            shaking = false;
            transform.localPosition = basePosition;

            defeatedByCurrentHit = false;
            profileChangedForCurrentRespawn = false;
            lastHitTime = 0f;
            ExitToIdle(); // hitPhase/Idle 애니메이션 상태까지 완전히 초기 상태로 되돌린다.

            EngagementRoleChanged?.Invoke(role);
        }

        /// <summary>Current(전투 중) 표현 - 각 Renderer의 알파를 Awake에서 캐시한 원래 값으로 즉시
        /// 되돌린다. <b>처치 상태(사망 Fade-out 진행 중)에서는 아무것도 하지 않고 경고만 남긴다</b> -
        /// 진행 중인 사망 연출을 승격/복구 호출이 우발적으로 덮어쓰는 것을 구조적으로 막기 위함이다.
        /// 처치된 슬롯을 되살리는 정식 경로는 <see cref="PrepareForEncounter"/> 하나뿐이다.</summary>
        public void ApplyCurrentVisual()
        {
            EnsureRuntimeReferences();
            if (target != null && target.IsDefeated)
            {
                Debug.LogWarning($"[TargetCombatController] '{name}': 처치 연출이 진행 중인 몬스터에 Current 알파 복구를 " +
                                 "요청받아 무시했습니다 - 되살리려면 PrepareForEncounter를 먼저 호출하세요.", this);
                return;
            }

            SetPresentationAlphaScale(1f);
        }

        /// <summary>Standby(대기) 표현 - 각 Renderer의 알파를 "원래 알파 x standbyAlpha"로 즉시 맞춘다.
        /// 언제나 캐시된 원래 알파에서 절대값으로 계산하므로 몇 번을 반복 호출해도 곱이 누적되지 않는다.</summary>
        public void ApplyStandbyVisual(float standbyAlpha)
        {
            EnsureRuntimeReferences();
            SetPresentationAlphaScale(standbyAlpha);
        }

        /// <summary>Exiting(퇴장) 표현 - 지정한 시간 동안 알파를 0으로 페이드한다. 처치 순간
        /// <see cref="HandleDefeated"/>가 이미 Target.DefeatFadeDuration으로 같은 Fade를 시작하므로,
        /// 관리자가 자기 시간을 쓰고 싶을 때만 호출하면 된다(진행 중이던 Fade는 지금 알파에서 이어서
        /// 다시 시작한다 - 알파가 튀지 않는다).</summary>
        public void ApplyExitingVisual(float duration)
        {
            EnsureRuntimeReferences();
            StartFade(toOriginal: false, duration: Mathf.Max(0f, duration));
        }

        /// <summary>모든 역할 알파의 실제 구현 - 진행 중이던 Fade를 취소하고 각 Renderer의 알파를
        /// "원래 알파 x scale"로 즉시 맞춘다(RGB는 건드리지 않는다). 역할 판정 자체는 알파와 무관하게
        /// 항상 Target.EngagementRole이 소유한다 - 알파는 표현일 뿐이다.</summary>
        private void SetPresentationAlphaScale(float scale)
        {
            EnsureRuntimeReferences();
            if (visualRenderers == null) return;

            CancelFade();
            float clamped = Mathf.Clamp01(scale);
            for (int i = 0; i < visualRenderers.Length; i++)
            {
                if (visualRenderers[i] == null) continue;
                Color c = visualRenderers[i].color;
                c.a = originalAlphas[i] * clamped;
                visualRenderers[i].color = c;
            }
        }

        /// <summary>randomizeProfileOnRespawn이 켜져 있고 respawnProfilePool에 현재 motionProfile을
        /// 제외한 유효한 후보(null 아님/중복 제거/Base Idle 첫 프레임 존재)가 있으면 그중 하나를 동일
        /// 확률로 골라 ApplyRuntimeMotionProfile()로 교체한다. 후보 유효성 기준은 Awake와 동일하다
        /// (IsProfilePlayable - Base Idle과 Hit 프레임이 모두 있어야 한다). 유효한 후보가 없으면(옵션이
        /// 꺼져 있거나, 풀이 비어 있거나, 남은 후보가 전부 현재 프로필/null/불완전 프로필이면) 아무것도
        /// 바꾸지 않고 false를 반환한다 - 기존 고정 프로필 리젠 동작이 그대로 유지된다.</summary>
        private bool TrySwitchToRandomRespawnProfile()
        {
            // 이 기능은 Target의 자체 리스폰(Standalone) 전용이다. 대기열 관리 대상이면 애초에
            // OnRespawnStarted가 발생하지 않아 여기까지 오지 않지만, "다음 등장에 어떤 프로필을 쓸지"는
            // 관리자가 PrepareForEncounter의 profileOverride로 결정한다는 소유권을 코드로도 못박아 둔다.
            if (target != null && target.LifecycleMode != TargetLifecycleMode.StandaloneSelfRespawn) return false;
            if (!randomizeProfileOnRespawn) return false;
            if (respawnProfilePool == null || respawnProfilePool.Length == 0) return false;

            var candidates = new List<MonsterMotionProfile>();
            foreach (MonsterMotionProfile candidate in respawnProfilePool)
            {
                if (candidate == null) continue;
                if (candidate == motionProfile) continue; // 연속 등장 금지: 현재 프로필 제외
                if (candidates.Contains(candidate)) continue; // 중복 등록은 한 번만 후보로 취급

                // Awake와 완전히 같은 유효성 기준을 쓴다 - 리젠으로 갈아끼운 뒤에야 "재생할 프레임이
                // 없다"를 발견하는 일이 없도록 후보 단계에서 걸러낸다.
                if (!IsProfilePlayable(candidate) || candidate.BaseIdle.Frames[0] == null) continue;

                candidates.Add(candidate);
            }

            if (candidates.Count == 0) return false;

            MonsterMotionProfile chosen = candidates[UnityEngine.Random.Range(0, candidates.Count)];
            ApplyRuntimeMotionProfile(chosen);
            profileChangedForCurrentRespawn = true;
            return true;
        }

        /// <summary>motionProfile을 nextProfile로 바꾸고, Awake에서 한 번만 구성되던 런타임 설정 전체를
        /// 새 프로필 기준으로 다시 만든다 - Idle/Idle Event/Hit/Hit Reaction/Defeat 프레임은
        /// BuildRuntimeConfiguration()을 재사용해 중복 계산 없이 갱신하고, stageLayout이 연결된
        /// 몬스터라면 위치/스케일도 Awake와 동일한 공식(ResolveInitialBasePosition/ApplyActorScale)으로
        /// 다시 계산한다. 향후 정식 몬스터 리젠 시스템도 이 메서드 하나로 프로필을 교체할 수 있도록
        /// 만든 단일 진입점이다 - Idle 프레임 적용/애니메이션 상태 리셋(ExitToIdle)은 호출자가 맡는다.</summary>
        private void ApplyRuntimeMotionProfile(MonsterMotionProfile nextProfile)
        {
            motionProfile = nextProfile;
            BuildRuntimeConfiguration();

            spriteRenderer.flipX = motionProfile.SpriteFlipX;

            if (stageLayout != null)
            {
                basePosition = ResolveInitialBasePosition();
                transform.localPosition = basePosition;
                ApplyActorScale();
            }

            shaking = false;
        }

        /// <summary>처치/리젠 Fade를 시작한다. 진행 중이던 Fade가 있으면 안전하게 중단하고, 새 Fade는
        /// 그 순간의 실제 알파를 시작값으로 삼아 이어간다 - 그래야 처치와 리젠 Fade가 같은 Renderer를
        /// 동시에 제어하거나 순간이동하는 일이 없다.</summary>
        private void StartFade(bool toOriginal, float duration)
        {
            CancelFade();
            fadeRoutine = StartCoroutine(FadeRoutine(toOriginal, duration));
        }

        private void CancelFade()
        {
            if (fadeRoutine == null) return;

            StopCoroutine(fadeRoutine);
            fadeRoutine = null;
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
            UpdatePresentationMove(); // 기준 위치를 먼저 옮기고,
            UpdateShake();            // 그 위에 흔들림 오프셋을 얹는다(순서가 바뀌면 이동 중 흔들림이 튄다).

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
