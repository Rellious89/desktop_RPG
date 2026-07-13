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

        private void Awake()
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
            originalColor = spriteRenderer.color;
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
            yield return new WaitForSeconds(flashDuration);
            spriteRenderer.color = originalColor;
            flashRoutine = null;
        }
    }
}
