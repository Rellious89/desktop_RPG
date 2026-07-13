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
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    [RequireComponent(typeof(FlashOnCue))]
    public class ScarecrowAnimator : MonoBehaviour
    {
        /// <summary>HitPoint를 받아 실제로 반응할 때마다 발생(연타 중에는 매 타격마다). 데미지/체력 감소 트리거로 쓴다.</summary>
        public event Action ReceiveImpact;

        [System.Serializable]
        public class FrameAnimation
        {
            public Sprite sheet;
            public int frameWidth = 512;
            public int frameHeight = 512;
            public int frameCount;
            public float framesPerSecond = 3f;
        }

        /// <summary>Hit는 항상 3프레임 고정 구조. holdFrame을 연타 중 계속 유지하고, recoveryFrame은 종료 시 한 번만 보여준다.</summary>
        [System.Serializable]
        public class HitAnimation
        {
            public Sprite sheet;
            public int frameWidth = 512;
            public int frameHeight = 512;

            [Tooltip("피격 중 유지할 프레임 인덱스(0부터). 연타 동안 이 프레임을 계속 유지한다.")]
            public int holdFrame = 1;

            [Tooltip("피격이 끝난 뒤 보여줄 복귀 프레임 인덱스")]
            public int recoveryFrame = 2;

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

        private const int HitFrameCount = 3;

        private enum HitPhase { None, Reacting, Recovery }

        [Header("Base Idle (계속 루프)")]
        [SerializeField] private FrameAnimation idle;

        [Header("Hit (연속 타격에 맞춰 유지/갱신)")]
        [SerializeField] private HitAnimation hit;

        [Header("Sprite Slicing")]
        [SerializeField] private float pixelsPerUnit = 200f;
        [SerializeField] private Vector2 pivot = new Vector2(0.5f, 0.0703125f);

        private SpriteRenderer spriteRenderer;
        private FlashOnCue flashOnCue;

        private Sprite[] idleFrames;
        private Sprite[] hitFrames;

        private int idleCurrentFrame;
        private float idleFrameTimer;

        private HitPhase hitPhase = HitPhase.None;
        private float lastHitTime;
        private float hitPhaseTimer;

        private Vector3 basePosition;
        private bool shaking;
        private float shakeStartTime;

        private void Awake()
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
            flashOnCue = GetComponent<FlashOnCue>();
            basePosition = transform.localPosition;

            idleFrames = SliceFrames(idle);
            hitFrames = SliceHitFrames(hit);

            idleCurrentFrame = 0;
            ApplyIdleFrame();
        }

        private void OnEnable()
        {
            CatKnightIdleAnimator.HitPoint += OnHitPoint;
        }

        private void OnDisable()
        {
            CatKnightIdleAnimator.HitPoint -= OnHitPoint;
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
                Debug.LogError($"[ScarecrowAnimator] '{texture.name}' 텍스처 크기({texture.width}x{texture.height})가 " +
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

        private Sprite[] SliceHitFrames(HitAnimation anim)
        {
            if (anim == null || anim.sheet == null || anim.sheet.texture == null)
            {
                return new Sprite[0];
            }

            Texture2D texture = anim.sheet.texture;
            int requiredWidth = anim.frameWidth * HitFrameCount;

            if (texture.width < requiredWidth || texture.height < anim.frameHeight)
            {
                Debug.LogError($"[ScarecrowAnimator] '{texture.name}' 텍스처 크기({texture.width}x{texture.height})가 " +
                    $"기대한 프레임 배치({requiredWidth}x{anim.frameHeight})보다 작습니다. " +
                    "임포트가 덜 끝난 상태일 수 있으니 해당 텍스처를 Reimport 해보세요.");
                return new Sprite[0];
            }

            var frames = new Sprite[HitFrameCount];

            for (int i = 0; i < HitFrameCount; i++)
            {
                var rect = new Rect(i * anim.frameWidth, 0, anim.frameWidth, anim.frameHeight);
                frames[i] = Sprite.Create(texture, rect, pivot, pixelsPerUnit, 0, SpriteMeshType.FullRect);
            }

            return frames;
        }

        private void OnHitPoint()
        {
            lastHitTime = Time.time;

            if (hitPhase != HitPhase.Reacting)
            {
                EnterReacting();
            }
            else
            {
                // 이미 피격 자세 유지 중이면 포즈는 그대로 두고 반짝임만 갱신해서 연타처럼 보이게 한다.
                flashOnCue.Flash();
            }

            TriggerShake();
            ReceiveImpact?.Invoke();
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

            if (hitFrames.Length > 0)
            {
                int frame = Mathf.Clamp(hit.holdFrame, 0, hitFrames.Length - 1);
                spriteRenderer.sprite = hitFrames[frame];
            }

            flashOnCue.Flash();
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

                default:
                    AdvanceIdle();
                    break;
            }
        }

        private void AdvanceIdle()
        {
            if (idleFrames.Length == 0 || idle.framesPerSecond <= 0f) return;

            float frameDuration = 1f / idle.framesPerSecond;
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
