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

        /// <summary>섬광이 재생 중일 때 오브젝트가 꺼지면(캐릭터 교체 등) 코루틴은 그 자리에서 중단되고
        /// 색을 되돌리는 마지막 줄에 영영 도달하지 못한다 - 그러면 다음에 이 렌더러를 다시 켰을 때
        /// flashColor(보통 흰색)가 그대로 남아 캐릭터가 하얗게 굳은 것처럼 보인다. 여기서 직접 원래
        /// 색으로 되돌려 그 잔상을 남기지 않는다.</summary>
        private void OnDisable()
        {
            if (flashRoutine != null)
            {
                StopCoroutine(flashRoutine);
                flashRoutine = null;
            }
            if (spriteRenderer != null) spriteRenderer.color = originalColor;
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
