using System;
using System.Collections;
using UnityEngine;

namespace Common
{
    /// <summary>
    /// 타격 이펙트 prefab에 붙는 단순 "팝" 연출 - 짧게 확대되며 페이드아웃한 뒤 사라진다.
    /// 준비된 아트가 없을 때 쓰는 더미 연출로, 실제 이펙트 애니메이션(스프라이트 시퀀스 등)으로
    /// 교체될 때까지의 자리 표시(placeholder) 용도다.
    ///
    /// OnEnable에서 기본 재생을 자동으로 시작한다(콜백 없이 호출하면 재생이 끝났을 때 스스로
    /// Destroy) - prefab만 단독으로 씬에 놓이거나 스포너 없이 Instantiate돼도 스스로 정리되는
    /// 독립적인 안전장치다. HitEffectSpawner가 이 인스턴스를 풀링할 때는 SetActive(true) 직후
    /// 같은 프레임 안에서(Update 이전) Play(duration, onComplete)를 다시 호출해서, 방금 OnEnable이
    /// 시작한 기본 재생(Destroy 모드)을 취소하고 풀 반환 모드로 바꿔치기한다 - Play()가 항상 진행
    /// 중이던 코루틴을 먼저 멈추기 때문에 안전하게 덮어쓸 수 있다.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class HitEffectPop : MonoBehaviour
    {
        [SerializeField] private float duration = 0.15f;
        [SerializeField] private float startScale = 0.6f;
        [SerializeField] private float endScale = 1.15f;

        private SpriteRenderer spriteRenderer;
        private Coroutine playRoutine;

        private void Awake()
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
        }

        private void OnEnable()
        {
            Play(duration, null);
        }

        /// <summary>
        /// 재생을 (재)시작한다. onComplete를 넘기면 재생이 끝났을 때 Destroy 대신 그 콜백을 호출한다
        /// (HitEffectSpawner가 풀로 반환할 때 사용). onComplete가 null이면 기존처럼 스스로 Destroy한다.
        /// </summary>
        public void Play(float playDuration, Action<HitEffectPop> onComplete)
        {
            if (playRoutine != null)
            {
                StopCoroutine(playRoutine);
            }

            float safeDuration = playDuration > 0f && !float.IsNaN(playDuration) && !float.IsInfinity(playDuration)
                ? playDuration
                : 0.15f;

            playRoutine = StartCoroutine(PlayRoutine(safeDuration, onComplete));
        }

        private IEnumerator PlayRoutine(float playDuration, Action<HitEffectPop> onComplete)
        {
            Color color = spriteRenderer.color;
            float elapsed = 0f;

            while (elapsed < playDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / playDuration);

                float scale = Mathf.Lerp(startScale, endScale, t);
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
