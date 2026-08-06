using System;
using System.Collections;
using UnityEngine;

namespace Common
{
    /// <summary>
    /// 스프라이트시트로 만든 타격/시전 이펙트의 재생 컴포넌트. Sprite 배열을 고정 FPS로 한 번 훑고
    /// 마지막 프레임까지 온전히 보여준 뒤 완료를 알린다 - 반복 재생은 하지 않는다(타격 이펙트는 한 번
    /// 터지고 사라지는 연출이고, 반복이 필요하면 프레임을 늘려서 표현한다).
    ///
    /// 길이(<see cref="Duration"/>)는 frames/fps로 스스로 계산하므로 공격 모션 데이터나
    /// <see cref="HitEffectSpawner"/> 어디에도 길이를 적어둘 필요가 없다 - 프레임을 늘리거나 FPS를
    /// 바꾸면 수명이 자동으로 따라온다. 이게 <see cref="IHitEffectPlayback"/>을 둔 이유다.
    ///
    /// <see cref="ProjectileSpriteAnimation"/>과 달리 진행도를 밖에서 받지 않고 자기 시간으로 재생한다 -
    /// 발사체는 "출발할 때 첫 장, 도착할 때 마지막 장"이 비행 시간에 묶여야 하지만, 타격 이펙트는
    /// 어떤 공격에서 터지든 항상 같은 속도로 재생돼야 하기 때문이다.
    ///
    /// 풀에서 재사용되므로 <see cref="Play"/>는 항상 0번 프레임부터 다시 시작한다. OnEnable에서 기본
    /// 재생을 자동으로 시작해두므로(HitEffectPop과 같은 규칙) prefab만 단독으로 씬에 놓여도 스스로
    /// 재생되고 스스로 정리된다.
    /// </summary>
    [DisallowMultipleComponent]
    public class HitEffectSpriteAnimation : MonoBehaviour, IHitEffectPlayback
    {
        [Tooltip("비워두면 이 오브젝트(또는 자식)의 SpriteRenderer를 자동으로 사용한다.")]
        [SerializeField] private SpriteRenderer spriteRenderer;

        [Tooltip("순서대로 한 번 재생할 Sprite 배열. 이 개수와 FPS가 이펙트의 수명을 결정한다 - " +
                 "프레임을 늘리면 이펙트가 사라지는 시점도 자동으로 늦춰진다.")]
        [SerializeField] private Sprite[] frames = Array.Empty<Sprite>();

        [Tooltip("초당 재생할 프레임 수. 원본 스프라이트시트 애니메이션의 Sample Rate와 맞춘다.")]
        [Min(0.01f)]
        [SerializeField] private float fps = 12f;

        // 프리팹 원본 상태 - 풀에서 재사용될 때마다 여기로 되돌린다.
        private Sprite originalSprite;
        private Vector3 originalLocalScale = Vector3.one;
        private bool originalsCaptured;

        private Coroutine playRoutine;

        /// <summary>frames가 비어 있어도 0을 돌려주지 않는다 - 0이면 스포너가 생성하자마자 회수해서
        /// 한 프레임도 보이지 않는다. 이 경우 SpriteRenderer에 이미 꽂혀 있는 Sprite를 한 프레임 길이만큼
        /// 보여주는 단일 이미지 이펙트로 동작한다.</summary>
        public float Duration => Mathf.Max(1, frames.Length) / Mathf.Max(0.01f, fps);

        private void Awake()
        {
            CaptureOriginals();
        }

        private void OnEnable()
        {
            // 스포너가 SetActive(true) 직후 같은 프레임 안에서 Play(scale, 콜백)를 다시 호출해 이 기본
            // 재생을 덮어쓴다(Play가 진행 중이던 코루틴을 먼저 멈추므로 안전하다). 스포너 없이 prefab만
            // 놓인 경우에는 이 재생이 그대로 살아서 스스로 Destroy까지 간다.
            Play(1f, null);
        }

        public void Play(float scaleMultiplier, Action<IHitEffectPlayback> onComplete)
        {
            CaptureOriginals();

            if (playRoutine != null)
            {
                StopCoroutine(playRoutine);
                playRoutine = null;
            }

            float safeScale = scaleMultiplier > 0f && !float.IsNaN(scaleMultiplier) && !float.IsInfinity(scaleMultiplier)
                ? scaleMultiplier
                : 1f;
            transform.localScale = originalLocalScale * safeScale;

            // 직전 재생의 마지막 프레임이 한 프레임이라도 노출되지 않도록, 코루틴이 처음 돌기 전에
            // 0번 프레임을 즉시 반영한다.
            ApplyFrame(0);

            playRoutine = StartCoroutine(PlayRoutine(onComplete));
        }

        public Sprite GetFrameAt(float elapsed)
        {
            if (elapsed < 0f || elapsed >= Duration) return null;

            SpriteRenderer renderer = ResolveRenderer();
            Sprite baseSprite = renderer != null ? renderer.sprite : null;
            if (frames.Length == 0) return baseSprite;

            return ResolveFrame(FrameIndexAt(elapsed), baseSprite);
        }

        private IEnumerator PlayRoutine(Action<IHitEffectPlayback> onComplete)
        {
            float duration = Duration;
            float elapsed = 0f;
            int lastIndex = 0;

            // 마지막 프레임도 1/fps 만큼 온전히 보이도록 elapsed가 duration에 도달할 때까지 돈다.
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                int index = FrameIndexAt(elapsed);
                if (index != lastIndex)
                {
                    ApplyFrame(index);
                    lastIndex = index;
                }

                yield return null;
            }

            playRoutine = null;

            if (onComplete != null)
            {
                onComplete(this);
            }
            else
            {
                Destroy(gameObject);
            }
        }

        /// <summary>진행 시간을 프레임 인덱스로 바꾼다. frames 범위를 넘지 않도록 마지막 인덱스로 클램프
        /// 하므로 elapsed가 duration에 정확히 닿는 순간에도 배열 밖을 가리키지 않는다.</summary>
        private int FrameIndexAt(float elapsed)
        {
            int count = Mathf.Max(1, frames.Length);
            return Mathf.Clamp(Mathf.FloorToInt(elapsed * Mathf.Max(0.01f, fps)), 0, count - 1);
        }

        private void ApplyFrame(int index)
        {
            SpriteRenderer renderer = ResolveRenderer();
            if (renderer == null) return;

            Sprite frame = ResolveFrame(index, originalSprite);
            if (frame != null && !ReferenceEquals(renderer.sprite, frame))
            {
                renderer.sprite = frame;
            }
        }

        /// <summary>배열에 빈 칸(null)이 섞여 있으면 직전에 지정된 Sprite를 유지한다 -
        /// <see cref="ProjectileSpriteAnimation"/>과 같은 규칙이라 프리뷰와 런타임이 항상 같은 그림을 낸다.</summary>
        private Sprite ResolveFrame(int index, Sprite fallback)
        {
            Sprite resolved = fallback;
            for (int i = 0; i <= index && i < frames.Length; i++)
            {
                if (frames[i] != null) resolved = frames[i];
            }
            return resolved;
        }

        /// <summary>Awake가 돌지 않은 상태(프리팹 에셋 위에서 모션 에디터가 조회하는 경우)에도 안전하게
        /// SpriteRenderer를 찾는다. 직렬화 필드를 여기서 덮어쓰지 않는다 - 프리팹 에셋을 조회만 했는데
        /// 에셋이 더티로 표시되면 안 되기 때문이다.</summary>
        private SpriteRenderer ResolveRenderer()
        {
            return spriteRenderer != null ? spriteRenderer : GetComponentInChildren<SpriteRenderer>(true);
        }

        private void CaptureOriginals()
        {
            if (originalsCaptured) return;

            SpriteRenderer renderer = ResolveRenderer();
            originalSprite = renderer != null ? renderer.sprite : null;
            originalLocalScale = transform.localScale;
            originalsCaptured = true;
        }
    }
}
