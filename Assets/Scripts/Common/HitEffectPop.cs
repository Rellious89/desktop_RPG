using System;
using System.Collections;
using UnityEngine;

namespace Common
{
    /// <summary>
    /// 타격 이펙트 prefab에 붙는 단순 "팝" 연출 - 짧게 확대되며 페이드아웃한 뒤 사라진다.
    /// 준비된 아트가 없을 때 쓰는 더미 연출로, 실제 이펙트 애니메이션으로 교체될 때까지의 자리
    /// 표시(placeholder) 용도다(스프라이트시트 아트가 준비되면
    /// <see cref="HitEffectSpriteAnimation"/>을 쓴다).
    ///
    /// 여기서 duration은 "언제 사라지는가"인 동시에 <b>확대/페이드아웃이 진행되는 속도 그 자체</b>다 -
    /// 짧으면 탁 터지고 길면 늘어져 보인다. 그래서 이 값은 스포너가 정해줄 성질이 아니라 이 연출이
    /// 스스로 들고 있어야 하는 값이고, <see cref="IHitEffectPlayback.Duration"/>으로 그대로 보고한다.
    ///
    /// OnEnable에서 기본 재생을 자동으로 시작한다(콜백 없이 호출하면 재생이 끝났을 때 스스로
    /// Destroy) - prefab만 단독으로 씬에 놓이거나 스포너 없이 Instantiate돼도 스스로 정리되는
    /// 독립적인 안전장치다. HitEffectSpawner가 이 인스턴스를 풀링할 때는 SetActive(true) 직후
    /// 같은 프레임 안에서(Update 이전) Play(scale, onComplete)를 다시 호출해서, 방금 OnEnable이
    /// 시작한 기본 재생(Destroy 모드)을 취소하고 풀 반환 모드로 바꿔치기한다 - Play()가 항상 진행
    /// 중이던 코루틴을 먼저 멈추기 때문에 안전하게 덮어쓸 수 있다.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class HitEffectPop : MonoBehaviour, IHitEffectPlayback
    {
        [Tooltip("확대되며 페이드아웃하는 데 걸리는 시간(초) - 이 더미 연출의 타격감을 결정하는 값이자 " +
                 "이펙트가 사라지는 시점이다. 0.1~0.2가 무난하다.")]
        [SerializeField] private float duration = 0.15f;
        [SerializeField] private float startScale = 0.6f;
        [SerializeField] private float endScale = 1.15f;

        private SpriteRenderer spriteRenderer;
        private Coroutine playRoutine;

        public float Duration => duration > 0f && !float.IsNaN(duration) && !float.IsInfinity(duration)
            ? duration
            : 0.15f;

        private void Awake()
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
        }

        private void OnEnable()
        {
            Play(1f, null);
        }

        /// <summary>
        /// 재생을 처음부터 (재)시작한다. onComplete를 넘기면 재생이 끝났을 때 Destroy 대신 그 콜백을 호출한다
        /// (HitEffectSpawner가 풀로 반환할 때 사용). onComplete가 null이면 기존처럼 스스로 Destroy한다.
        /// scaleMultiplier는 startScale/endScale에 곱해져 재생 내내 적용된다(기본 1 = 원래 배율 그대로).
        /// </summary>
        public void Play(float scaleMultiplier, Action<IHitEffectPlayback> onComplete)
        {
            if (playRoutine != null)
            {
                StopCoroutine(playRoutine);
                playRoutine = null;
            }

            float safeScale = scaleMultiplier > 0f && !float.IsNaN(scaleMultiplier) && !float.IsInfinity(scaleMultiplier)
                ? scaleMultiplier
                : 1f;

            playRoutine = StartCoroutine(PlayRoutine(Duration, onComplete, safeScale));
        }

        /// <summary>이 연출은 스프라이트를 바꾸지 않고 배율/알파만 움직이므로, 재생 구간 내내 프리팹에
        /// 꽂혀 있는 그림 한 장을 그대로 돌려준다(모션 에디터 프리뷰용 조회).</summary>
        public Sprite GetFrameAt(float elapsed)
        {
            if (elapsed < 0f || elapsed >= Duration) return null;

            SpriteRenderer renderer = spriteRenderer != null ? spriteRenderer : GetComponent<SpriteRenderer>();
            return renderer != null ? renderer.sprite : null;
        }

        private IEnumerator PlayRoutine(float playDuration, Action<IHitEffectPlayback> onComplete, float scaleMultiplier)
        {
            Color color = spriteRenderer.color;
            float elapsed = 0f;

            while (elapsed < playDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / playDuration);

                float scale = Mathf.Lerp(startScale, endScale, t) * scaleMultiplier;
                transform.localScale = new Vector3(scale, scale, 1f);

                color.a = 1f - t;
                spriteRenderer.color = color;

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
    }
}
