using System.Collections;
using UnityEngine;

namespace Common
{
    /// <summary>
    /// 타격/피격처럼 특정 프레임 타이밍에 발생하는 이벤트를 육안으로 확인하기 위한 더미 연출.
    /// 데미지 숫자나 실제 이펙트가 붙기 전까지 자리 표시(placeholder) 용도로 쓴다.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class FlashOnCue : MonoBehaviour
    {
        [SerializeField] private Color flashColor = Color.white;
        [SerializeField] private float flashDuration = 0.08f;

        private SpriteRenderer spriteRenderer;
        private Color originalColor;
        private Coroutine flashRoutine;
        private WaitForSeconds cachedWait;

        private void Awake()
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
            originalColor = spriteRenderer.color;
            // new WaitForSeconds(...)를 Flash()마다 새로 만들면 연타 중 매 타격마다 할당이 생긴다 -
            // flashDuration은 런타임에 안 바뀌므로 하나만 만들어 재사용한다.
            cachedWait = new WaitForSeconds(flashDuration);
        }

        public void Flash()
        {
            if (flashRoutine != null)
            {
                StopCoroutine(flashRoutine);
            }

            flashRoutine = StartCoroutine(FlashRoutine());
        }

        private IEnumerator FlashRoutine()
        {
            spriteRenderer.color = flashColor;
            yield return cachedWait;
            spriteRenderer.color = originalColor;
            flashRoutine = null;
        }
    }
}
