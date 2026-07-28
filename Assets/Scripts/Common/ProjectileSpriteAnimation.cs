using System;
using UnityEngine;

namespace Common
{
    /// <summary>
    /// 발사체의 기본 시각 표현. Sprite 배열을 비행 진행도(0~1)에 균등 분배해서 재생한다 - 자체 FPS나
    /// 타이머가 없으므로 비행 시간이 길든 짧든 항상 "출발할 때 첫 장, 도착할 때 마지막 장"이 보장된다.
    ///
    /// 배열에 Sprite가 한 장뿐이면 그 한 장을 계속 보여주는 단일 이미지 발사체가 되고, 여러 장이면
    /// 그대로 애니메이션 발사체가 된다 - 이동 시스템(ProjectileMover)은 둘을 구분하지 않는다.
    /// frames가 비어 있으면 SpriteRenderer에 이미 붙어 있는 Sprite를 그대로 쓴다(프리팹에 스프라이트만
    /// 꽂아두고 배열은 비워두는 가장 단순한 구성도 정상 동작한다).
    ///
    /// Fade는 기본적으로 꺼져 있다(0/0) - 기본 공격처럼 비행 시간이 짧을 때 페이드가 오히려 발사체를
    /// 안 보이게 만들기 때문이다. 켜더라도 비행 시간이 minFadeFlightDuration보다 짧으면 이번 비행에는
    /// 적용하지 않는다.
    /// </summary>
    [DisallowMultipleComponent]
    public class ProjectileSpriteAnimation : MonoBehaviour, IProjectileVisual
    {
        [Tooltip("비워두면 이 오브젝트의 SpriteRenderer를 자동으로 사용한다.")]
        [SerializeField] private SpriteRenderer spriteRenderer;

        [Tooltip("비행 시간에 균등 분배해서 순서대로 재생할 Sprite 배열. 한 장만 넣으면 단일 이미지 발사체, " +
                 "비워두면 SpriteRenderer에 이미 설정된 Sprite를 그대로 유지한다.")]
        [SerializeField] private Sprite[] frames = Array.Empty<Sprite>();

        [Header("Fade (선택)")]
        [Tooltip("비행 시작 구간에서 알파가 0 -> 1로 올라오는 비율(0~0.5). 0이면 Fade In 없음.")]
        [Range(0f, 0.5f)]
        [SerializeField] private float fadeInRatio;

        [Tooltip("비행 종료 구간에서 알파가 1 -> 0으로 내려가는 비율(0~0.5). 0이면 Fade Out 없음.")]
        [Range(0f, 0.5f)]
        [SerializeField] private float fadeOutRatio;

        [Tooltip("비행 시간이 이 값(초)보다 짧으면 Fade를 아예 적용하지 않는다 - 짧은 기본 공격에서 " +
                 "발사체가 보이지도 않고 사라지는 것을 막는다.")]
        [Min(0f)]
        [SerializeField] private float minFadeFlightDuration = 0.12f;

        // 프리팹 원본 상태 - 풀에서 재사용될 때마다 여기로 되돌린다.
        private Sprite originalSprite;
        private float originalAlpha = 1f;

        private bool fadeEnabledForCurrentFlight;

        private void Awake()
        {
            if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();
            if (spriteRenderer == null) return;

            originalSprite = spriteRenderer.sprite;
            originalAlpha = spriteRenderer.color.a;
        }

        public void BeginFlight(float flightDuration)
        {
            // Fade 설정이 하나라도 켜져 있고, 비행 시간이 눈에 보일 만큼 길 때만 이번 비행에 Fade를 적용한다.
            fadeEnabledForCurrentFlight = (fadeInRatio > 0f || fadeOutRatio > 0f) && flightDuration >= minFadeFlightDuration;
            SetFlightProgress(0f);
        }

        public void SetFlightProgress(float normalizedProgress)
        {
            if (spriteRenderer == null) return;

            float progress = Mathf.Clamp01(normalizedProgress);

            if (frames.Length > 0)
            {
                // 진행도 1.0이 배열 밖을 가리키지 않도록 마지막 인덱스로 클램프한다.
                int index = Mathf.Clamp(Mathf.FloorToInt(progress * frames.Length), 0, frames.Length - 1);
                Sprite frame = frames[index];
                if (frame != null && !ReferenceEquals(spriteRenderer.sprite, frame))
                {
                    spriteRenderer.sprite = frame;
                }
            }

            ApplyAlpha(originalAlpha * ResolveFadeMultiplier(progress));
        }

        public void ResetVisual()
        {
            if (spriteRenderer == null) return;

            fadeEnabledForCurrentFlight = false;
            spriteRenderer.sprite = originalSprite;
            ApplyAlpha(originalAlpha);
        }

        private float ResolveFadeMultiplier(float progress)
        {
            if (!fadeEnabledForCurrentFlight) return 1f;

            if (fadeInRatio > 0f && progress < fadeInRatio)
            {
                return progress / fadeInRatio;
            }
            if (fadeOutRatio > 0f && progress > 1f - fadeOutRatio)
            {
                return (1f - progress) / fadeOutRatio;
            }
            return 1f;
        }

        private void ApplyAlpha(float alpha)
        {
            Color color = spriteRenderer.color;
            color.a = Mathf.Clamp01(alpha);
            spriteRenderer.color = color;
        }
    }
}
