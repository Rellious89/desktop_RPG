using UnityEngine;
using UnityEngine.UI;

namespace Common
{
    /// <summary>
    /// 고정 크기 uGUI Image를 변형하지 않고, 셰이더의 움직이는 사선 경계만으로 진행도를 표시한다.
    /// Slider를 값 공급원으로 연결할 수 있지만 Slider의 Fill Rect를 움직일 필요는 없다.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class SlantedProgressBar : MonoBehaviour
    {
        public enum FillDirection
        {
            LeftToRight,
            RightToLeft,
        }

        public enum SlantDirection
        {
            /// <summary>진행 방향 기준으로 윗줄이 아랫줄보다 먼저 다음 픽셀에 도달한다.</summary>
            TopLeads,

            /// <summary>진행 방향 기준으로 아랫줄이 윗줄보다 먼저 다음 픽셀에 도달한다.</summary>
            BottomLeads,
        }

        private const string ShaderName = "KeyBuddy/UI/Slanted Progress Fill";
        // 셰이더의 kPixelRoundEpsilon과 반드시 같아야 한다. 6행/6px 사선처럼 .5 경계에
        // 정확히 놓이는 offset이 부동소수점 오차로 한 칸 아래로 내려가는 것을 막는다.
        private const float PixelRoundEpsilon = 0.00001f;

        [Header("Fill")]
        [Tooltip("항상 최대 길이를 유지할 Fill Image. 진행도에 따라 이 RectTransform이나 Sprite는 절대 바꾸지 않는다.")]
        [SerializeField] private Image fillImage;

        [Tooltip("선택 사항. 연결하면 Slider 값을 이 바의 값 공급원으로 읽는다.")]
        [SerializeField] private Slider sourceSlider;

        [Tooltip("켜면 Source Slider의 Fill Rect를 비운다. Slider가 Fill의 너비를 다시 바꾸는 기존 동작을 막을 때만 켠다.")]
        [SerializeField] private bool clearSourceSliderFillRect;

        [Header("Progress")]
        [Range(0f, 1f)]
        [SerializeField] private float normalizedValue = 1f;

        [SerializeField] private FillDirection fillDirection = FillDirection.LeftToRight;

        [SerializeField] private SlantDirection slantDirection = SlantDirection.TopLeads;

        [Min(0f)]
        [Tooltip("사선의 전체 가로 폭(원본 Fill Sprite의 픽셀 단위). 0이면 수직 경계가 된다.")]
        [SerializeField] private float slantWidthPixels = 5f;

        [Tooltip("경계와 사선을 원본 Fill Sprite의 텍셀 격자에 맞춘다. Point-filtered 픽셀 UI에서는 켜 둔다.")]
        [SerializeField] private bool pixelSnap = true;

        [Min(0)]
        [Tooltip("0이면 Fill Sprite의 가로 픽셀 수를 쓴다. 표시 폭과 아트 폭이 의도적으로 다를 때만 직접 지정한다.")]
        [SerializeField] private int pixelWidthOverride;

        private Material runtimeMaterial;
        private Material originalMaterial;
        private SlantedProgressBarMeshEffect meshEffect;
        private Slider subscribedSlider;
        private float lastSliderNormalizedValue = float.NaN;
        private bool missingShaderLogged;

        /// <summary>0~1로 정규화된 현재 진행도. 0은 완전 숨김, 1은 원본 Fill을 그대로 표시한다.</summary>
        public float NormalizedValue
        {
            get => normalizedValue;
            set => SetNormalizedValue(value, true);
        }

        private void Reset()
        {
            if (fillImage == null) fillImage = GetComponent<Image>();
            if (sourceSlider == null) sourceSlider = GetComponent<Slider>();
        }

        private void OnEnable()
        {
            ResolveReferences();
            SubscribeSlider();
            ApplyVisual();
        }

        private void OnDisable()
        {
            UnsubscribeSlider();
        }

        private void OnDestroy()
        {
            UnsubscribeSlider();
            ReleaseRuntimeMaterial();
        }

        private void OnValidate()
        {
            normalizedValue = Mathf.Clamp01(normalizedValue);
            if (!isActiveAndEnabled) return;

            ResolveReferences();
            SubscribeSlider();
            ApplyVisual();
        }

        private void OnRectTransformDimensionsChange()
        {
            if (isActiveAndEnabled) ApplyVisual();
        }

        private void Update()
        {
            // ProgressBarView는 기존 Slider 호환성을 위해 SetValueWithoutNotify를 쓴다. 이벤트가 오지
            // 않는 그 경로도 매 프레임 값 차이만 확인해 이 컴포넌트와 동기화한다.
            if (sourceSlider == null) return;

            float sliderValue = sourceSlider.normalizedValue;
            if (Mathf.Approximately(sliderValue, lastSliderNormalizedValue)) return;

            SetNormalizedValue(sliderValue, false);
        }

        /// <summary>현재값/최대값을 넣는다. 최대값이 0 이하이면 빈 바로 표시한다.</summary>
        public void SetValue(float current, float maximum)
        {
            NormalizedValue = NormalizeValue(current, maximum);
        }

        /// <summary>정수 값 호출부를 위한 편의 오버로드.</summary>
        public void SetValue(int current, int maximum)
        {
            SetValue((float)current, maximum);
        }

        /// <summary><paramref name="ratio"/>를 0~1로 제한해 즉시 표시한다.</summary>
        public void SetRatio(float ratio)
        {
            NormalizedValue = ratio;
        }

        /// <summary>0~1 진행값을 안전하게 계산한다. EditMode 테스트 및 다른 표시 컴포넌트에서도 재사용할 수 있다.</summary>
        public static float NormalizeValue(float current, float maximum)
        {
            return maximum <= 0f ? 0f : Mathf.Clamp01(current / maximum);
        }

        /// <summary>픽셀 스냅을 켰을 때 셰이더가 쓰는 가로 경계값을 계산한다.</summary>
        public static float SnapNormalizedValue(float value, int pixelWidth)
        {
            float clamped = Mathf.Clamp01(value);
            if (clamped <= 0f || clamped >= 1f || pixelWidth <= 0) return clamped;

            return GetSnappedProgressPixel(clamped, pixelWidth) / (float)pixelWidth;
        }

        /// <summary>
        /// 픽셀 스냅 사선이 사용할 진행 경계의 기준 열을 구한다. 행별 사선 오프셋과 이 값을
        /// 따로 반올림해야, 진행값이 한 픽셀 이동할 때 계단 전체가 그대로 한 칸 이동한다.
        /// </summary>
        public static int GetSnappedProgressPixel(float value, int pixelWidth)
        {
            if (pixelWidth <= 0) return 0;

            return Mathf.FloorToInt(Mathf.Clamp01(value) * pixelWidth + 0.5f + PixelRoundEpsilon);
        }

        /// <summary>
        /// 한 텍셀 행에 고정으로 붙는 사선 오프셋을 계산한다. 이 계산은 진행도와 독립적이므로
        /// progress base pixel이 1 증가하면 모든 행의 edge도 정확히 1씩 증가한다.
        /// </summary>
        public static int GetSlantedRowOffsetPixel(int row, int texelHeight, float signedSlantPixels)
        {
            int height = Mathf.Max(1, texelHeight);
            int clampedRow = Mathf.Clamp(row, 0, height - 1);
            float rowCenter = (clampedRow + 0.5f) / height - 0.5f;
            return Mathf.FloorToInt(signedSlantPixels * rowCenter + 0.5f + PixelRoundEpsilon);
        }

        /// <summary>테스트와 셰이더 수학 검증용: 스냅된 기준 열과 고정 행 오프셋을 합친 경계 열.</summary>
        public static int GetSnappedSlantedEdgePixel(
            float value,
            int pixelWidth,
            int row,
            int texelHeight,
            float signedSlantPixels)
        {
            return GetSnappedProgressPixel(value, pixelWidth) +
                   GetSlantedRowOffsetPixel(row, texelHeight, signedSlantPixels);
        }

        private void SetNormalizedValue(float value, bool syncSlider)
        {
            normalizedValue = Mathf.Clamp01(value);
            lastSliderNormalizedValue = normalizedValue;

            if (syncSlider && sourceSlider != null)
            {
                float sliderValue = Mathf.Lerp(sourceSlider.minValue, sourceSlider.maxValue, normalizedValue);
                sourceSlider.SetValueWithoutNotify(sliderValue);
            }

            ApplyVisual();
        }

        private void ResolveReferences()
        {
            if (fillImage == null) fillImage = GetComponent<Image>();
            if (sourceSlider == null) sourceSlider = GetComponent<Slider>();

            if (clearSourceSliderFillRect && sourceSlider != null && sourceSlider.fillRect != null)
            {
                sourceSlider.fillRect = null;
            }
        }

        private void SubscribeSlider()
        {
            if (subscribedSlider == sourceSlider) return;

            UnsubscribeSlider();
            if (sourceSlider == null) return;

            subscribedSlider = sourceSlider;
            subscribedSlider.onValueChanged.AddListener(HandleSliderValueChanged);
            // Source Slider가 연결된 경우 그 값이 초기 상태의 단일 진실 원천이다. 씬에 저장된 Preview
            // 값과 Slider 값이 다르더라도 Enable 첫 프레임부터 같은 상태로 그린다.
            normalizedValue = subscribedSlider.normalizedValue;
            lastSliderNormalizedValue = normalizedValue;
        }

        private void UnsubscribeSlider()
        {
            if (subscribedSlider == null) return;

            subscribedSlider.onValueChanged.RemoveListener(HandleSliderValueChanged);
            subscribedSlider = null;
        }

        private void HandleSliderValueChanged(float ignoredValue)
        {
            if (sourceSlider == null) return;

            SetNormalizedValue(sourceSlider.normalizedValue, false);
        }

        private void ApplyVisual()
        {
            ResolveReferences();
            if (fillImage == null) return;
            if (!EnsureRuntimeMaterial()) return;

            GetFillTexelCount(out int width, out int height);

            float snappedProgress = pixelSnap ? SnapNormalizedValue(normalizedValue, width) : normalizedValue;
            if (!EnsureMeshEffect()) return;

            float direction = fillDirection == FillDirection.LeftToRight ? 1f : -1f;
            // 사선 폭을 정규화 좌표가 아닌 실제 텍셀 열 수로 전달한다. 셰이더에서 base progress
            // pixel과 각 행의 offset을 독립적으로 반올림해 계단의 형태를 고정한다.
            float signedSlantPixels = direction *
                                      (slantDirection == SlantDirection.TopLeads ? 1f : -1f) *
                                      slantWidthPixels;
            meshEffect.Configure(snappedProgress, direction, signedSlantPixels, pixelSnap, width, height);

            // Mask/RectMask2D는 이 material을 바탕으로 Stencil 복제본을 만들 수 있다. 진행 데이터는
            // meshEffect의 정점 UV에 있어 복제 여부와 무관하게 최신 값이 적용된다.
            fillImage.SetMaterialDirty();
        }

        private bool EnsureMeshEffect()
        {
            if (meshEffect == null && fillImage != null)
            {
                meshEffect = fillImage.GetComponent<SlantedProgressBarMeshEffect>();
                if (meshEffect == null) meshEffect = fillImage.gameObject.AddComponent<SlantedProgressBarMeshEffect>();
            }

            EnsureCanvasShaderChannels();
            return meshEffect != null;
        }

        private void EnsureCanvasShaderChannels()
        {
            if (fillImage == null) return;

            Canvas canvas = fillImage.GetComponentInParent<Canvas>();
            if (canvas == null) return;

            const AdditionalCanvasShaderChannels required =
                AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.TexCoord2;
            if ((canvas.additionalShaderChannels & required) != required)
            {
                canvas.additionalShaderChannels |= required;
            }
        }

        private bool EnsureRuntimeMaterial()
        {
            if (runtimeMaterial != null)
            {
                if (fillImage.material != runtimeMaterial) fillImage.material = runtimeMaterial;
                return true;
            }

            Shader shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                if (!missingShaderLogged)
                {
                    missingShaderLogged = true;
                    Debug.LogError($"[SlantedProgressBar] '{name}': '{ShaderName}' 셰이더를 찾지 못했습니다.", this);
                }
                return false;
            }

            originalMaterial = fillImage.material;
            runtimeMaterial = new Material(shader)
            {
                name = $"{name} (Slanted Progress Runtime)",
                hideFlags = HideFlags.HideAndDontSave,
            };
            fillImage.material = runtimeMaterial;
            return true;
        }

        private void GetFillTexelCount(out int width, out int height)
        {
            Sprite sprite = fillImage != null ? fillImage.overrideSprite ?? fillImage.sprite : null;
            width = pixelWidthOverride > 0
                ? pixelWidthOverride
                : sprite != null ? Mathf.Max(1, Mathf.RoundToInt(sprite.rect.width)) : 1;
            height = sprite != null ? Mathf.Max(1, Mathf.RoundToInt(sprite.rect.height)) : 1;
        }

        private void ReleaseRuntimeMaterial()
        {
            if (runtimeMaterial == null) return;

            if (fillImage != null && fillImage.material == runtimeMaterial)
            {
                fillImage.material = originalMaterial;
                fillImage.SetMaterialDirty();
            }

            if (Application.isPlaying) Destroy(runtimeMaterial);
            else DestroyImmediate(runtimeMaterial);

            runtimeMaterial = null;
            originalMaterial = null;
        }
    }
}
