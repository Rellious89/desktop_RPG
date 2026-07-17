using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Common
{
    /// <summary>
    /// 스택에 표시되는 토스트 하나의 수명(등장/슬롯 이동/퇴장)을 담당한다. ToastManager가 풀에서 꺼내
    /// EnterAt으로 재생을 시작시키고, 새 토스트가 추가될 때마다 MoveToSlot으로 절대 목표 위치를 다시
    /// 지정한다(상대 누적이 아니다 - ToastManager가 매번 active 목록 기준으로 슬롯을 다시 계산해서
    /// 넘겨준다. 그래야 중간 토스트가 먼저 만료돼도 나머지가 maxVisibleCount 밖으로 밀려나지 않는다).
    /// 자신의 visibleDuration이 끝나거나 ToastManager가 개수 초과로 강제 퇴장(ForceExit)시키면 같은
    /// 퇴장 경로(BeginExit)를 타므로 중복 처리 걱정 없이 한 번만 onBeginExit/onReleased가 불린다.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class ToastInstance : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("비워두면 자식에서 TextMeshProUGUI를 자동으로 찾는다.")]
        [SerializeField] private TextMeshProUGUI text;
        [Tooltip("향후 아이콘형 토스트를 위한 자리. 비워두면 아이콘을 표시하지 않는다.")]
        [SerializeField] private Image icon;

        private RectTransform rect;
        private CanvasGroup canvasGroup;
        private Vector3 baseScale;
        private Coroutine lifecycleRoutine;
        private Coroutine shiftRoutine;
        private bool exiting;

        private void Awake()
        {
            rect = GetComponent<RectTransform>();
            if (text == null)
            {
                text = GetComponentInChildren<TextMeshProUGUI>();
            }

            canvasGroup = GetComponent<CanvasGroup>();
            if (canvasGroup == null)
            {
                canvasGroup = gameObject.AddComponent<CanvasGroup>();
            }
            canvasGroup.blocksRaycasts = false;
            canvasGroup.interactable = false;

            baseScale = rect.localScale;
            canvasGroup.alpha = 0f;
        }

        /// <summary>지정된 위치에서 등장시키고, enterDuration → visibleDuration → exitDuration 순으로 재생한다.</summary>
        public void EnterAt(Vector2 position, ToastRequest request, float enterDuration, float visibleDuration, float exitDuration,
            Action<ToastInstance> onBeginExit, Action<ToastInstance> onReleased)
        {
            StopTrackedRoutine(ref lifecycleRoutine);
            StopTrackedRoutine(ref shiftRoutine);
            exiting = false;

            rect.anchoredPosition = position;
            rect.localScale = baseScale;
            canvasGroup.alpha = 0f;

            if (text != null)
            {
                text.text = request.message;
                text.color = request.color.a > 0f ? request.color : Color.white;
            }
            if (icon != null)
            {
                icon.sprite = request.icon;
                icon.enabled = request.icon != null;
            }

            // ContentSizeFitter가 자식(텍스트)의 preferredSize를 먼저 갱신한 뒤에야 부모 크기를 다시
            // 계산하므로, text.text만 바꾸고 다음 레이아웃 패스를 기다리면 이전 문구의 크기가 한 프레임
            // 남는다 - 자식→부모 순서로 강제 리빌드한다.
            if (text != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(text.rectTransform);
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(rect);

            lifecycleRoutine = StartCoroutine(Lifecycle(enterDuration, visibleDuration, exitDuration, onBeginExit, onReleased));
        }

        /// <summary>새 토스트가 추가될 때 ToastManager가 다시 계산한 절대 목표 슬롯 위치로 이동시킨다.
        /// 이동 중에 다시 호출되면 현재 실제 위치를 시작점으로 삼아 새 목표까지 다시 이동한다.</summary>
        public void MoveToSlot(Vector2 targetPosition, float duration)
        {
            StopTrackedRoutine(ref shiftRoutine);
            shiftRoutine = StartCoroutine(ShiftRoutine(targetPosition, duration));
        }

        /// <summary>개수 초과 등으로 자신의 visibleDuration을 기다리지 않고 즉시 퇴장 연출을 시작한다.</summary>
        public void ForceExit(float exitDuration, Action<ToastInstance> onBeginExit, Action<ToastInstance> onReleased)
        {
            if (exiting) return;

            StopTrackedRoutine(ref lifecycleRoutine);
            lifecycleRoutine = StartCoroutine(BeginExit(exitDuration, onBeginExit, onReleased));
        }

        /// <summary>풀로 반환되기 직전 호출된다 - 위치/알파/스케일/텍스트/타이머/애니메이션 상태를 모두
        /// 지운다. 다음 EnterAt이 이전 상태를 전혀 물려받지 않도록 한다.</summary>
        public void ResetForPool()
        {
            StopTrackedRoutine(ref lifecycleRoutine);
            StopTrackedRoutine(ref shiftRoutine);
            exiting = false;
            canvasGroup.alpha = 0f;
            rect.anchoredPosition = Vector2.zero;
            rect.localScale = baseScale;
            if (text != null)
            {
                text.text = string.Empty;
            }
            if (icon != null)
            {
                icon.sprite = null;
                icon.enabled = false;
            }
        }

        private IEnumerator Lifecycle(float enterDuration, float visibleDuration, float exitDuration,
            Action<ToastInstance> onBeginExit, Action<ToastInstance> onReleased)
        {
            yield return Fade(0f, 1f, enterDuration);

            float elapsed = 0f;
            while (elapsed < visibleDuration)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            yield return BeginExit(exitDuration, onBeginExit, onReleased);
            lifecycleRoutine = null;
        }

        private IEnumerator BeginExit(float exitDuration, Action<ToastInstance> onBeginExit, Action<ToastInstance> onReleased)
        {
            if (exiting) yield break;
            exiting = true;

            onBeginExit?.Invoke(this);
            yield return Fade(canvasGroup.alpha, 0f, exitDuration);
            onReleased?.Invoke(this);
        }

        private IEnumerator ShiftRoutine(Vector2 target, float duration)
        {
            Vector2 start = rect.anchoredPosition;
            if (duration <= 0f)
            {
                rect.anchoredPosition = target;
                shiftRoutine = null;
                yield break;
            }

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                rect.anchoredPosition = Vector2.Lerp(start, target, Mathf.Clamp01(elapsed / duration));
                yield return null;
            }

            rect.anchoredPosition = target;
            shiftRoutine = null;
        }

        private IEnumerator Fade(float from, float to, float duration)
        {
            canvasGroup.alpha = from;
            if (duration <= 0f)
            {
                canvasGroup.alpha = to;
                yield break;
            }

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                canvasGroup.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / duration));
                yield return null;
            }
            canvasGroup.alpha = to;
        }

        private void StopTrackedRoutine(ref Coroutine routine)
        {
            if (routine != null)
            {
                StopCoroutine(routine);
                routine = null;
            }
        }
    }
}
