using System;
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
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    [RequireComponent(typeof(FlashOnCue))]
    public class CatKnightIdleAnimator : MonoBehaviour
    {
        /// <summary>공격 세션에 처음 진입하는 순간(Idle -> Attack) 발생.</summary>
        public static event Action AttackStarted;

        /// <summary>공격 재생이 hitFrameIndex에 도달할 때마다 발생. 인자는 이번 타격의 데미지량(basicAttackPower).</summary>
        public static event Action<int> HitPoint;

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
        /// 공격 애니메이션 데이터. 0번 프레임이 항상 시작(Windup)이고, hitFrameIndex가 타격 프레임,
        /// 그 이후부터 마지막 프레임까지가 복귀(Recovery)다. 프레임 개수는 frames.Length 그대로다.
        /// </summary>
        [System.Serializable]
        public class AttackAnimation
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
        }

        private const int IdleIndex = 0;

        private enum AttackPhase { None, Windup, Recovery }

        [Header("Base Idle (계속 루프)")]
        [SerializeField] private FrameAnimation idle;

        [Header("Idle Variants (한 번만 재생, 동일 확률)")]
        [SerializeField] private FrameAnimation idleA;
        [SerializeField] private FrameAnimation idleB;
        [SerializeField] private FrameAnimation idleC;

        [Header("Attack (연속 입력 유지형)")]
        [SerializeField] private AttackAnimation attack;

        [Header("Combat")]
        [Tooltip("기본 공격 1회(타격 1번)당 적용할 데미지량. 강공격/치명타 등 추가 계산식은 아직 없다.")]
        [SerializeField] private int basicAttackPower = 5;

        [Header("Timing")]
        [SerializeField] private float variantCheckInterval = 4f;

        private SpriteRenderer spriteRenderer;
        private FlashOnCue flashOnCue;
        private FrameAnimation[] animations;
        private Sprite[] attackFrames;

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

        private int HitFrameIndex => attackFrames.Length == 0
            ? 0
            : Mathf.Clamp(attack.hitFrameIndex, 0, attackFrames.Length - 1);

        private void Awake()
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
            flashOnCue = GetComponent<FlashOnCue>();
            animations = new[] { idle, idleA, idleB, idleC };
            attackFrames = SafeFrames(attack?.frames);

            for (int i = 0; i < animations.Length; i++)
            {
                animations[i].frames = SafeFrames(animations[i].frames);
            }

            activeAnimIndex = IdleIndex;
            currentFrame = 0;
            ApplyFrame();
        }

        private static Sprite[] SafeFrames(Sprite[] frames) => frames ?? Array.Empty<Sprite>();

        private void Update()
        {
            if (GlobalKeyboardHook.AnyKeyDownThisFrame)
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
            if (attackFrames.Length == 0) return;

            playingVariant = false;
            AttackStarted?.Invoke();
            StartWindup();
        }

        private void StartWindup()
        {
            attackPhase = AttackPhase.Windup;
            attackFrame = 0;
            attackPhaseTimer = 0f;
            ApplyAttackFrame();

            if (HitFrameIndex <= 0)
            {
                Strike(); // hitFrameIndex가 0이면 시작하자마자 타격
            }
        }

        private void Strike()
        {
            if (pendingAttacks > 0) pendingAttacks--; // 이 타격으로 대기열에서 요청 하나를 소비(확정)한다.

            flashOnCue.Flash();
            HitPoint?.Invoke(basicAttackPower);

            bool inputStillFresh = Time.time - lastInputTime < attack.queueExpireTimeout;
            if (pendingAttacks > 0 && inputStillFresh)
            {
                StartWindup(); // 대기 중인 타격이 있고 입력이 이어지고 있으면 곧바로 다음 재생으로
            }
            else
            {
                pendingAttacks = 0; // 입력이 끊겼으면 밀린 예약은 버리고 지금 재생만 마무리한다
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

                        if (attackFrame >= HitFrameIndex)
                        {
                            Strike();
                        }
                    });
                    break;

                case AttackPhase.Recovery:
                    if (attackFrame < attackFrames.Length - 1)
                    {
                        AdvanceStep(() =>
                        {
                            attackFrame++;
                            ApplyAttackFrame();

                            if (attackFrame >= attackFrames.Length - 1)
                            {
                                attackPhaseTimer = 0f; // 스텝 타이머 -> 유지 타이머로 전환하기 전 리셋
                            }
                        });
                    }
                    else
                    {
                        attackPhaseTimer += Time.deltaTime;
                        if (attackPhaseTimer >= attack.endFrameDuration)
                        {
                            FinishSession();
                        }
                    }
                    break;
            }
        }

        private void AdvanceStep(Action onStepComplete)
        {
            if (attack.animationFps <= 0f) return;

            float step = 1f / attack.animationFps;
            attackPhaseTimer += Time.deltaTime;

            if (attackPhaseTimer < step) return;
            attackPhaseTimer -= step;

            onStepComplete();
        }

        private void ApplyAttackFrame()
        {
            if (attackFrame < 0 || attackFrame >= attackFrames.Length) return;
            spriteRenderer.sprite = attackFrames[attackFrame];
        }

        // ---- Idle / Idle 변형 ----
        private void RollVariant()
        {
            int choice = UnityEngine.Random.Range(0, 4); // 0 = idle 유지, 1/2/3 = a/b/c
            if (choice == 0) return;

            if (animations[choice].frames.Length == 0) return;

            playingVariant = true;
            activeAnimIndex = choice;
            currentFrame = 0;
            frameTimer = 0f;
            ApplyFrame();
        }

        private void AdvanceFrame()
        {
            Sprite[] frames = animations[activeAnimIndex].frames;
            FrameAnimation anim = animations[activeAnimIndex];
            if (frames.Length == 0 || anim.animationFps <= 0f) return;

            float frameDuration = 1f / anim.animationFps;
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
            Sprite[] frames = animations[activeAnimIndex].frames;
            if (frames.Length == 0) return;
            spriteRenderer.sprite = frames[Mathf.Clamp(currentFrame, 0, frames.Length - 1)];
        }
    }
}
