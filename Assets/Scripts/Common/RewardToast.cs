using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Common
{
    /// <summary>
    /// HUD 위에 짧은 보상 토스트("+1 EXP", "LEVEL UP!" 등)를 표시하는 재사용 가능한 단일 토스트.
    /// 인스턴스를 새로 생성하지 않고 이 오브젝트 하나를 계속 재사용한다(풀 크기 1인 풀과 동일한 효과).
    /// 표시 중 새 요청이 들어오면 큐에 쌓아두고 순차적으로 재생해서 여러 토스트가 겹치지 않는다.
    /// GameObject 자체는 항상 활성 상태로 두고 CanvasGroup.alpha로만 보이고/숨긴다 - ComboDisplay와
    /// 같은 이유로, GameObject를 비활성화하면 코루틴과 이벤트 구독이 함께 끊기기 때문이다.
    /// PlayerProgress.OnExpGained/OnLevelUp을 직접 구독해서 별도 연결 코드 없이 동작한다.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class RewardToast : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("비워두면 자식에서 TextMeshProUGUI를 자동으로 찾는다.")]
        [SerializeField] private TextMeshProUGUI text;

        [Header("Motion")]
        [Tooltip("표시된 뒤 위로 떠오르는 거리(px)")]
        [SerializeField] private float riseDistance = 20f;
        [Tooltip("떠오르는 동안 유지되는 시간(초)")]
        [SerializeField] private float showDuration = 0.6f;
        [Tooltip("떠오른 뒤 페이드아웃되는 시간(초)")]
        [SerializeField] private float fadeDuration = 0.4f;

        [Header("Text")]
        [Tooltip("TMP 폰트 크기. 값을 올리면 그대로 직접 커진다.")]
        [SerializeField] private float fontSize = 36f;

        private RectTransform rect;
        private CanvasGroup canvasGroup;
        private Vector2 basePosition;
        private readonly Queue<string> pending = new Queue<string>();
        private bool playing;

        private void Awake()
        {
            rect = GetComponent<RectTransform>();

            if (text == null)
            {
                text = GetComponentInChildren<TextMeshProUGUI>();
            }
            if (text != null)
            {
                text.fontSize = fontSize;
            }

            canvasGroup = GetComponent<CanvasGroup>();
            if (canvasGroup == null)
            {
                canvasGroup = gameObject.AddComponent<CanvasGroup>();
            }

            basePosition = rect.anchoredPosition;
            canvasGroup.alpha = 0f;
            canvasGroup.blocksRaycasts = false;
            canvasGroup.interactable = false;
        }

        private void OnEnable()
        {
            PlayerProgress.OnExpGained += HandleExpGained;
            PlayerProgress.OnLevelUp += HandleLevelUp;
        }

        private void OnDisable()
        {
            PlayerProgress.OnExpGained -= HandleExpGained;
            PlayerProgress.OnLevelUp -= HandleLevelUp;
        }

        private void HandleExpGained(int amount)
        {
            ShowToast($"+{amount} EXP");
        }

        private void HandleLevelUp(int newLevel)
        {
            ShowToast("LEVEL UP!");
        }

        /// <summary>다른 보상 토스트에도 재사용할 수 있는 진입점.</summary>
        public void ShowToast(string message)
        {
            pending.Enqueue(message);
            if (!playing)
            {
                StartCoroutine(PlayQueue());
            }
        }

        private IEnumerator PlayQueue()
        {
            playing = true;

            while (pending.Count > 0)
            {
                string message = pending.Dequeue();
                yield return PlayOne(message);
            }

            playing = false;
        }

        private IEnumerator PlayOne(string message)
        {
            text.text = message;

            // ContentSizeFitter는 자식(lb_toast)의 preferredSize가 먼저 갱신된 뒤에야 부모(bg_RewardToast)를
            // 다시 계산한다. text.text만 바꾸고 다음 레이아웃 패스를 그냥 기다리면, 짧은 문구로 바뀔 때
            // 캐시된 이전(더 큰) 크기가 그대로 남아 배경이 줄어들지 않는다 - 자식→부모 순서로 강제 리빌드한다.
            LayoutRebuilder.ForceRebuildLayoutImmediate(text.rectTransform);
            LayoutRebuilder.ForceRebuildLayoutImmediate(rect);

            rect.anchoredPosition = basePosition;
            canvasGroup.alpha = 1f;

            float elapsed = 0f;
            while (elapsed < showDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / showDuration);
                rect.anchoredPosition = basePosition + Vector2.up * (riseDistance * t);
                yield return null;
            }

            elapsed = 0f;
            while (elapsed < fadeDuration)
            {
                elapsed += Time.deltaTime;
                canvasGroup.alpha = 1f - Mathf.Clamp01(elapsed / fadeDuration);
                yield return null;
            }

            canvasGroup.alpha = 0f;
            rect.anchoredPosition = basePosition;
        }
    }
}
