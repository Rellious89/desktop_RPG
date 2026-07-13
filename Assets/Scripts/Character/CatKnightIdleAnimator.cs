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
    /// 쌓이고, 0번(준비) -> 1번(타격, HitPoint) 사이클이 하나씩 그 대기열을 소비한다. 사이클 도중 새
    /// 입력이 들어와도 애니메이션을 재시작하지 않고 대기열에만 추가되며, 타격 시점에 순서대로 소비된다.
    /// 마지막 타격 이후 대기 중인 입력이 없으면 2번 프레임(복귀)을 한 번 보여준 뒤 Idle로 돌아간다.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    [RequireComponent(typeof(FlashOnCue))]
    public class CatKnightIdleAnimator : MonoBehaviour
    {
        /// <summary>공격 루프에 처음 진입하는 순간(Idle -> Attack) 발생.</summary>
        public static event Action AttackStarted;

        /// <summary>공격 루프가 타격 프레임(1번)에 도달할 때마다 발생. 피격 반응/데미지 트리거로 쓴다.</summary>
        public static event Action HitPoint;

        /// <summary>공격이 끝나고 Idle로 돌아가는 순간 발생.</summary>
        public static event Action AttackEnded;

        [System.Serializable]
        public class FrameAnimation
        {
            public Sprite sheet;
            public int frameWidth = 512;
            public int frameHeight = 512;
            public int frameCount;
            public float framesPerSecond = 6f;
        }

        /// <summary>공격은 항상 3프레임 고정 구조: 0=준비, 1=타격(HitPoint), 2=복귀.</summary>
        [System.Serializable]
        public class AttackAnimation
        {
            public Sprite sheet;
            public int frameWidth = 512;
            public int frameHeight = 512;

            [Header("Cycle (0번 준비 -> 1번 타격)")]
            [Tooltip("준비/타격 각 단계가 유지되는 속도(초당 전환 횟수). 한 사이클(준비+타격)은 이 값의 " +
                     "역수의 2배 만큼 걸린다. 예: 18 -> 사이클당 약 0.11초")]
            public float stepFramesPerSecond = 18f;

            [Header("Attack End (2번 복귀)")]
            [Tooltip("복귀 프레임을 보여주는 시간(초)")]
            public float endFrameDuration = 0.12f;

            [Header("Queue")]
            [Tooltip("마지막 입력 이후 이 시간(초) 동안 새 입력이 없으면, 남아있는 예약(대기열)을 전부 취소하고 " +
                     "진행 중인 사이클만 마친 뒤 복귀한다. 0.15~0.25 권장")]
            public float queueExpireTimeout = 0.15f;
        }

        private const int AttackFrameCount = 3;
        private const int IdleIndex = 0;

        private enum AttackPhase { None, Ready, Strike, End }

        [Header("Base Idle (계속 루프)")]
        [SerializeField] private FrameAnimation idle;

        [Header("Idle Variants (한 번만 재생, 동일 확률)")]
        [SerializeField] private FrameAnimation idleA;
        [SerializeField] private FrameAnimation idleB;
        [SerializeField] private FrameAnimation idleC;

        [Header("Attack (연속 입력 유지형)")]
        [SerializeField] private AttackAnimation attack;

        [Header("Timing")]
        [SerializeField] private float variantCheckInterval = 4f;

        [Header("Sprite Slicing")]
        [SerializeField] private float pixelsPerUnit = 200f;
        [SerializeField] private Vector2 pivot = new Vector2(0.5f, 0.078125f);

        private SpriteRenderer spriteRenderer;
        private FlashOnCue flashOnCue;
        private FrameAnimation[] animations;
        private Sprite[][] slicedFrames;
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

        private void Awake()
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
            flashOnCue = GetComponent<FlashOnCue>();
            animations = new[] { idle, idleA, idleB, idleC };

            slicedFrames = new Sprite[animations.Length][];
            for (int i = 0; i < animations.Length; i++)
            {
                slicedFrames[i] = SliceFrames(animations[i]);
            }

            attackFrames = SliceAttackFrames(attack);

            activeAnimIndex = IdleIndex;
            currentFrame = 0;
            ApplyFrame();
        }

        private Sprite[] SliceFrames(FrameAnimation anim)
        {
            if (anim == null || anim.sheet == null || anim.sheet.texture == null || anim.frameCount <= 0)
            {
                return new Sprite[0];
            }

            Texture2D texture = anim.sheet.texture;
            int requiredWidth = anim.frameWidth * anim.frameCount;

            if (texture.width < requiredWidth || texture.height < anim.frameHeight)
            {
                Debug.LogError($"[CatKnightIdleAnimator] '{texture.name}' 텍스처 크기({texture.width}x{texture.height})가 " +
                    $"기대한 프레임 배치({requiredWidth}x{anim.frameHeight})보다 작습니다. " +
                    "임포트가 덜 끝난 상태일 수 있으니 해당 텍스처를 Reimport 해보세요.");
                return new Sprite[0];
            }

            var frames = new Sprite[anim.frameCount];

            for (int i = 0; i < anim.frameCount; i++)
            {
                var rect = new Rect(i * anim.frameWidth, 0, anim.frameWidth, anim.frameHeight);
                frames[i] = Sprite.Create(texture, rect, pivot, pixelsPerUnit, 0, SpriteMeshType.FullRect);
            }

            return frames;
        }

        private Sprite[] SliceAttackFrames(AttackAnimation anim)
        {
            if (anim == null || anim.sheet == null || anim.sheet.texture == null)
            {
                return new Sprite[0];
            }

            Texture2D texture = anim.sheet.texture;
            int requiredWidth = anim.frameWidth * AttackFrameCount;

            if (texture.width < requiredWidth || texture.height < anim.frameHeight)
            {
                Debug.LogError($"[CatKnightIdleAnimator] '{texture.name}' 텍스처 크기({texture.width}x{texture.height})가 " +
                    $"기대한 프레임 배치({requiredWidth}x{anim.frameHeight})보다 작습니다. " +
                    "임포트가 덜 끝난 상태일 수 있으니 해당 텍스처를 Reimport 해보세요.");
                return new Sprite[0];
            }

            var frames = new Sprite[AttackFrameCount];

            for (int i = 0; i < AttackFrameCount; i++)
            {
                var rect = new Rect(i * anim.frameWidth, 0, anim.frameWidth, anim.frameHeight);
                frames[i] = Sprite.Create(texture, rect, pivot, pixelsPerUnit, 0, SpriteMeshType.FullRect);
            }

            return frames;
        }

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

        // ---- 입력 처리: 키 입력 1회 = 타격 1회. 대기열에 쌓아두고 사이클이 순서대로 소비한다 ----
        private void OnKeyInput()
        {
            pendingAttacks++;
            lastInputTime = Time.time;

            switch (attackPhase)
            {
                case AttackPhase.None:
                    BeginAttackSession();
                    break;
                case AttackPhase.End:
                    // 복귀 프레임 재생 중 새 입력 -> 복귀를 취소하고 대기 중인 타격을 이어서 처리한다.
                    EnterReady();
                    break;
                case AttackPhase.Ready:
                case AttackPhase.Strike:
                    // 이미 사이클 진행 중이면 애니메이션을 건드리지 않는다. 대기열에 쌓이는 것만으로 충분하다.
                    break;
            }
        }

        private void BeginAttackSession()
        {
            if (attackFrames.Length == 0) return;

            playingVariant = false;
            AttackStarted?.Invoke();
            EnterReady();
        }

        private void EnterReady()
        {
            attackPhase = AttackPhase.Ready;
            attackFrame = 0;
            attackPhaseTimer = 0f;
            ApplyAttackFrame();
        }

        private void EnterStrike()
        {
            attackPhase = AttackPhase.Strike;
            attackFrame = 1;
            attackPhaseTimer = 0f;
            ApplyAttackFrame();

            // 이 타격으로 대기열에서 요청 하나를 소비(확정)한다.
            if (pendingAttacks > 0) pendingAttacks--;

            flashOnCue.Flash();
            HitPoint?.Invoke();
        }

        private void EnterEnd()
        {
            attackPhase = AttackPhase.End;
            attackFrame = 2;
            attackPhaseTimer = 0f;
            ApplyAttackFrame();
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
                case AttackPhase.Ready:
                    AdvanceStep(EnterStrike);
                    break;

                case AttackPhase.Strike:
                    AdvanceStep(() =>
                    {
                        bool inputStillFresh = Time.time - lastInputTime < attack.queueExpireTimeout;

                        if (pendingAttacks > 0 && inputStillFresh)
                        {
                            EnterReady(); // 대기 중인 타격이 있고 입력이 계속 이어지고 있으면 곧바로 다음 사이클로
                        }
                        else
                        {
                            pendingAttacks = 0; // 입력이 끊겼으면 밀린 예약은 버리고 지금 사이클만 마무리한다
                            EnterEnd();
                        }
                    });
                    break;

                case AttackPhase.End:
                    attackPhaseTimer += Time.deltaTime;
                    if (attackPhaseTimer >= attack.endFrameDuration)
                    {
                        FinishSession();
                    }
                    break;
            }
        }

        private void AdvanceStep(Action onStepComplete)
        {
            if (attack.stepFramesPerSecond <= 0f) return;

            float step = 1f / attack.stepFramesPerSecond;
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

            if (slicedFrames[choice].Length == 0) return;

            playingVariant = true;
            activeAnimIndex = choice;
            currentFrame = 0;
            frameTimer = 0f;
            ApplyFrame();
        }

        private void AdvanceFrame()
        {
            Sprite[] frames = slicedFrames[activeAnimIndex];
            FrameAnimation anim = animations[activeAnimIndex];
            if (frames.Length == 0 || anim.framesPerSecond <= 0f) return;

            float frameDuration = 1f / anim.framesPerSecond;
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
            Sprite[] frames = slicedFrames[activeAnimIndex];
            if (frames.Length == 0) return;
            spriteRenderer.sprite = frames[Mathf.Clamp(currentFrame, 0, frames.Length - 1)];
        }
    }
}
