using UnityEngine;
using UnityEngine.UI;

namespace Common
{
    /// <summary>
    /// 경험치 바 프리팹의 <b>시각 구조만</b> 재사용하기 위한 범용 진행도 표시 컴포넌트.
    /// 값이 무엇을 뜻하는지(경험치인지 행동력인지), 어디서 오는지 전혀 알지 못한다 - 호출부가
    /// <see cref="SetValue"/>/<see cref="SetRatio"/>로 자기 값을 주입한다.
    ///
    /// <b>PlayerProgress를 구독하지 않는다.</b> 캐릭터 행동력 바가 경험치 이벤트에 반응하면 캐릭터를
    /// 처치할 때마다 모든 리스트 항목의 행동력 표시가 함께 움직인다 - 그래서 이 컴포넌트는 이벤트
    /// 구독 자체를 갖고 있지 않고, 값 변경도 호출된 시점에만 일어난다.
    ///
    /// 경험치 HUD는 지금까지대로 <see cref="PlayerProgressDisplay"/>가 그린다(레벨업 연출 큐를 갖고
    /// 있어 단순 대입과 동작이 다르다). 같은 GameObject에 둘을 함께 붙이면 같은 Slider를 서로
    /// 덮어쓰므로 OnEnable에서 오류로 막는다.
    ///
    /// Slider는 표시 전용으로 쓴다 - Fill Image가 Sliced 타입이면 fillAmount로 텍스처를 잘라내지 않고
    /// Fill Rect의 폭을 바꾸므로, 나인슬라이스 양끝 모양을 유지한 채 진행률을 표현할 수 있다.
    ///
    /// <b>구간별 색상은 선택 기능이다</b>(<see cref="useValueBandColors"/>). 켠 막대만 값 비율에 따라
    /// Fill Image의 color를 바꾸고, 끈 막대(경험치 HUD 등)는 색을 전혀 건드리지 않는다. 스프라이트는
    /// 어느 구간에서도 교체하지 않는다 - 한 장을 계속 쓰고 Tint만 바꾼다.
    /// </summary>
    [DisallowMultipleComponent]
    public class ProgressBarView : MonoBehaviour
    {
        /// <summary>Preview Fill을 이름으로 자동 탐색할 때 쓰는 이름(경험치 바 프리팹의 기존 이름).</summary>
        private const string PreviewFillName = "sp_ExpBar_Preview";

        /// <summary>값 비율을 나눠 색을 정하는 3단계 구간. 경계값은 <b>아래 구간에 포함</b>된다
        /// (Low 임계 0.30이면 정확히 30%는 Low, Mid 임계 0.80이면 정확히 80%는 Mid).</summary>
        public enum ValueBand
        {
            Low,
            Mid,
            High,
        }

        [Tooltip("표시 전용 Slider. Min Value 0, Max Value 1, Whole Numbers Off. 비워두면 같은 " +
                 "GameObject에서 자동으로 찾는다.")]
        [SerializeField] private Slider fillSlider;

        [Tooltip("Main Fill 뒤에 깔리는 연출용 Fill(경험치 바에서 가져온 구조). 진행도 표시에는 " +
                 "Main Fill과 같은 값을 그대로 적용한다. 비워두면 자식에서 '" + PreviewFillName + "'을 찾고, " +
                 "그것도 없으면 사용하지 않는다.")]
        [SerializeField] private RectTransform previewFill;

        [Header("Value Band Colors (기본 꺼짐 - 켠 막대에서만 색이 바뀐다)")]
        [Tooltip("켜면 값 비율에 따라 Fill Image의 color를 Low/Mid/High 색으로 바꾼다. 끄면 색을 한 번도 " +
                 "쓰지 않는다 - 경험치 HUD처럼 원래 색 그대로 두어야 하는 막대는 꺼 둔다.")]
        [SerializeField] private bool useValueBandColors;

        [Tooltip("색을 칠할 Main Fill Image. 비워두면 Slider의 Fill Rect에 붙은 Image를 쓴다.")]
        [SerializeField] private Image fillImage;

        [Tooltip("색을 칠할 Preview Fill Image. 비워두면 Preview Fill에 붙은 Image를 쓴다. 없으면 " +
                 "Main Fill에만 칠한다.")]
        [SerializeField] private Image previewFillImage;

        [Tooltip("Low 구간 색. 원본 스프라이트에 곱해지므로 흰색 계열 스프라이트에서만 의도한 색이 나온다.")]
        [SerializeField] private Color lowColor = new Color(0.90f, 0.24f, 0.24f, 1f);

        [Tooltip("Mid 구간 색.")]
        [SerializeField] private Color midColor = new Color(1f, 0.65f, 0.15f, 1f);

        [Tooltip("High 구간 색. 기본값은 기존 파란 게이지와 같은 계열(Cyan)이다.")]
        [SerializeField] private Color highColor = new Color(0f, 0.86f, 1f, 1f);

        [Tooltip("이 비율 이하가 Low(경계값 포함).")]
        [Range(0f, 1f)]
        [SerializeField] private float lowThreshold = 0.30f;

        [Tooltip("이 비율 이하가 Mid(경계값 포함). 초과하면 High.")]
        [Range(0f, 1f)]
        [SerializeField] private float midThreshold = 0.80f;

        private bool resolved;
        private bool missingFillImageLogged;

        private void OnEnable()
        {
            ResolveReferences();
        }

        /// <summary>현재값/최대값을 그대로 넣는다. 최대값이 0 이하면 빈 바로 그린다(0으로 나누지 않는다).</summary>
        public void SetValue(int current, int max)
        {
            SetRatio(ToRatio(current, max));
        }

        /// <summary>0~1 진행률을 즉시 적용한다(연출 없음). 구간별 색상이 켜져 있으면 <b>같은 호출에서</b>
        /// 색까지 함께 갱신한다 - 값과 색이 서로 다른 시점에 갱신돼 어긋나는 상태가 생기지 않는다.</summary>
        public void SetRatio(float ratio)
        {
            ResolveReferences();

            float clamped = Mathf.Clamp01(ratio);
            if (fillSlider != null) fillSlider.SetValueWithoutNotify(clamped);

            if (previewFill != null)
            {
                Vector2 anchorMax = previewFill.anchorMax;
                anchorMax.x = clamped;
                previewFill.anchorMax = anchorMax;
            }

            ApplyBandColor(clamped);
        }

        /// <summary>현재값/최대값을 0~1 비율로 바꾼다. 최대값이 0 이하면 0이다(0으로 나누지 않는다).
        /// <b>정수 퍼센트로 반올림하지 않는다</b> - 구간 판정도 이 원본 비율로 한다.</summary>
        public static float ToRatio(int current, int max)
        {
            return max <= 0 ? 0f : Mathf.Clamp01((float)current / max);
        }

        /// <summary>이 막대의 임계값 기준으로 비율이 어느 구간인지 알려준다. 호출부가 같은 판정을
        /// 복사해 쓰지 않도록 여기 한 곳에만 둔다.</summary>
        public ValueBand GetBand(float ratio)
        {
            float clamped = Mathf.Clamp01(ratio);
            if (clamped <= lowThreshold) return ValueBand.Low;
            if (clamped <= midThreshold) return ValueBand.Mid;
            return ValueBand.High;
        }

        /// <summary>구간에 대응하는 색. Inspector에서 구간마다 따로 조절한다.</summary>
        public Color GetBandColor(ValueBand band)
        {
            switch (band)
            {
                case ValueBand.Low: return lowColor;
                case ValueBand.Mid: return midColor;
                default: return highColor;
            }
        }

        /// <summary>스프라이트는 바꾸지 않고 color만 바꾼다. 참조가 없어도 예외를 내지 않는다 -
        /// 색이 없는 막대는 그냥 값만 움직인다.</summary>
        private void ApplyBandColor(float clampedRatio)
        {
            if (!useValueBandColors) return;

            Color color = GetBandColor(GetBand(clampedRatio));
            if (fillImage != null) fillImage.color = color;
            if (previewFillImage != null) previewFillImage.color = color;
        }

        private void ResolveReferences()
        {
            if (resolved) return;
            resolved = true;

            if (fillSlider == null) fillSlider = GetComponent<Slider>();
            if (fillSlider == null)
            {
                Debug.LogError($"[ProgressBarView] '{name}': Slider를 찾지 못했습니다 - 진행도 막대가 " +
                               "전혀 갱신되지 않습니다. Inspector에서 Fill Slider를 연결하세요.", this);
            }

            if (previewFill == null)
            {
                Transform found = FindDeepChild(transform, PreviewFillName);
                if (found != null) previewFill = found as RectTransform;
            }

            // 색을 칠할 Image는 값 표시에 쓰는 오브젝트에서 그대로 가져온다 - Inspector에서 직접
            // 연결했으면 그것을 우선한다(프리팹 구조가 다를 때를 위한 여지).
            if (fillImage == null && fillSlider != null && fillSlider.fillRect != null)
            {
                fillImage = fillSlider.fillRect.GetComponent<Image>();
            }
            if (previewFillImage == null && previewFill != null)
            {
                previewFillImage = previewFill.GetComponent<Image>();
            }

            if (useValueBandColors && fillImage == null && !missingFillImageLogged)
            {
                missingFillImageLogged = true;
                Debug.LogWarning($"[ProgressBarView] '{name}': 구간별 색상이 켜져 있는데 Fill Image를 " +
                                 "찾지 못했습니다 - 색이 바뀌지 않고 값만 움직입니다. Inspector에서 " +
                                 "Fill Image를 연결하세요.", this);
            }

            if (GetComponent<PlayerProgressDisplay>() != null)
            {
                Debug.LogError($"[ProgressBarView] '{name}': 같은 GameObject에 PlayerProgressDisplay가 함께 " +
                               "붙어 있습니다. 둘이 같은 Slider를 번갈아 덮어쓰고, 이 막대가 경험치 값으로 " +
                               "덮어씌워집니다 - 하나만 남기세요.", this);
            }
        }

        private static Transform FindDeepChild(Transform root, string childName)
        {
            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (child.name == childName) return child;

                Transform found = FindDeepChild(child, childName);
                if (found != null) return found;
            }
            return null;
        }
    }
}
