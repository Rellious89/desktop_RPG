using System.Collections;
using UnityEngine;

namespace Common
{
    /// <summary>
    /// 타격 이펙트 prefab에 붙는 단순 "팝" 연출 - 짧게 확대되며 페이드아웃한 뒤 스스로 사라진다.
    /// 준비된 아트가 없을 때 쓰는 더미 연출로, 실제 이펙트 애니메이션(스프라이트 시퀀스 등)으로
    /// 교체될 때까지의 자리 표시(placeholder) 용도다.
    ///
    /// HitEffectSpawner도 같은 인스턴스를 duration 이후 Destroy하지만, 이 컴포넌트는 스포너 없이
    /// prefab만 단독으로 씬에 놓였을 때도 스스로 정리되도록 독립적인 안전장치로 동작한다.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class HitEffectPop : MonoBehaviour
    {
        [SerializeField] private float duration = 0.15f;
        [SerializeField] private float startScale = 0.6f;
        [SerializeField] private float endScale = 1.15f;

        private void Awake()
        {
            float playDuration = duration > 0f && !float.IsNaN(duration) && !float.IsInfinity(duration)
                ? duration
                : 0.15f;

            StartCoroutine(PlayAndDestroy(playDuration));
        }

        private IEnumerator PlayAndDestroy(float playDuration)
        {
            var spriteRenderer = GetComponent<SpriteRenderer>();
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

            Destroy(gameObject);
        }
    }
}
