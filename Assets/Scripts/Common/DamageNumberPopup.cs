using System;
using System.Collections;
using TMPro;
using UnityEngine;

namespace Common
{
    /// <summary>
    /// 짧게 떠올랐다가 사라지는 데미지 숫자 하나의 수명을 담당한다.
    /// DamageNumberSpawner가 재생 직전 Initialize를 호출해 값과 완료 콜백을 넘겨준다.
    /// fontSize는 TMP의 fontSize를 그대로 사용한다 - 값을 올리면 즉시, 직접적으로 커진다.
    /// 재생이 끝나면 Destroy하지 않고 onComplete(this)로 스포너의 풀에 돌려준다.
    /// </summary>
    [RequireComponent(typeof(TextMeshPro))]
    public class DamageNumberPopup : MonoBehaviour
    {
        private Coroutine activeRoutine;

        public void Initialize(string text, Color color, float fontSize, float riseDistance, float duration, Action<DamageNumberPopup> onComplete)
        {
            var tmp = GetComponent<TextMeshPro>();
            tmp.text = text;
            tmp.color = color;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontSize = fontSize;

            if (activeRoutine != null)
            {
                StopCoroutine(activeRoutine);
            }
            activeRoutine = StartCoroutine(RiseAndFade(tmp, color, riseDistance, duration, onComplete));
        }

        private IEnumerator RiseAndFade(TextMeshPro tmp, Color color, float riseDistance, float duration, Action<DamageNumberPopup> onComplete)
        {
            Vector3 start = transform.position;
            Vector3 end = start + Vector3.up * riseDistance;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);

                transform.position = Vector3.Lerp(start, end, t);

                Color c = color;
                c.a = 1f - t;
                tmp.color = c;

                yield return null;
            }

            activeRoutine = null;
            onComplete?.Invoke(this);
        }
    }
}
