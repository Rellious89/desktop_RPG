// 고정 크기 uGUI Fill을 사선 경계로만 잘라내는 셰이더.
// Image의 메시 로컬 좌표를 사용하므로, Sliced Image여도 진행률에 따라 Sprite/RectTransform을
// 축소하지 않는다. UI/Default의 Stencil/RectMask2D 경로를 그대로 유지한다.
Shader "KeyBuddy/UI/Slanted Progress Fill"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        // UI/Default와 같은 이름을 유지해야 Mask/RectMask2D가 이 머티리얼의 변형본에 Stencil 값을 넣는다.
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float4 texcoord : TEXCOORD0;
                float4 texcoord1 : TEXCOORD1;
                float4 texcoord2 : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex        : SV_POSITION;
                fixed4 color         : COLOR;
                float2 texcoord      : TEXCOORD0;
                float4 clipData      : TEXCOORD1;
                float2 localUv       : TEXCOORD2;
                float2 texelCount    : TEXCOORD3;
                float4 worldPosition : TEXCOORD4;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4 _Color;
            float4 _ClipRect;
            // C# SlantedProgressBar.PixelRoundEpsilon과 같은 값. .5 경계의 IEEE 부동소수점
            // 오차 때문에 고정 행 offset이 한 칸 아래로 반올림되는 것을 막는다.
            static const float kPixelRoundEpsilon = 0.00001;
            v2f vert(appdata_t IN)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.worldPosition = IN.vertex;
                OUT.vertex = UnityObjectToClipPos(OUT.worldPosition);
                OUT.texcoord = IN.texcoord.xy;
                // SlantedProgressBarMeshEffect가 이 값을 각 Fill 메시의 정점에 기록한다. Stencil
                // 머티리얼은 base material의 float 값을 복사할 뿐 이후 변경을 받지 않으므로, 진행도는
                // 머티리얼 값이 아니라 정점 데이터여야 Mask가 켜져도 항상 최신 상태를 그릴 수 있다.
                OUT.clipData = float4(IN.texcoord.z, IN.texcoord.w, IN.texcoord1.z, IN.texcoord1.w);
                OUT.localUv = IN.texcoord1.xy;
                OUT.texelCount = IN.texcoord2.xy;
                OUT.color = IN.color * _Color;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                fixed4 color = tex2D(_MainTex, IN.texcoord) * IN.color;

                float progress = IN.clipData.x;
                float fillDirection = IN.clipData.y;
                float signedSlantPixels = IN.clipData.z;
                float pixelSnap = IN.clipData.w;

                // 0/1은 별도 처리한다. 사선 폭 때문에 0에서 모서리 한 점이 남거나 1에서 한 점이
                // 사라지는 일을 막아, API의 끝값 의미를 정확하게 보장한다.
                if (progress <= 0.0)
                {
                    clip(-1.0);
                }
                else if (progress < 1.0)
                {
                    float texelWidth = max(1.0, IN.texelCount.x);
                    float texelHeight = max(1.0, IN.texelCount.y);
                    float horizontal = IN.localUv.x;

                    // 기준 진행 경계와 행별 사선 오프셋을 한 식으로 합쳐 반올림하면 부동소수점
                    // 경계에서 행마다 다른 방향으로 튈 수 있다. 기준 열은 진행값만으로 한 번
                    // 스냅하고, 행 offset은 progress와 완전히 독립적으로 고정한다.
                    if (pixelSnap > 0.5)
                    {
                        float horizontalPixel = floor(horizontal * texelWidth);
                        float row = min(texelHeight - 1.0, floor(IN.localUv.y * texelHeight));
                        float baseEdge = fillDirection > 0.0 ? progress : 1.0 - progress;
                        float baseEdgePixel = floor(baseEdge * texelWidth + 0.5 + kPixelRoundEpsilon);
                        float rowCenter = (row + 0.5) / texelHeight - 0.5;
                        float rowOffsetPixel = floor(signedSlantPixels * rowCenter + 0.5 + kPixelRoundEpsilon);
                        float edgePixel = baseEdgePixel + rowOffsetPixel;

                        // edgePixel은 채워진 마지막 텍셀이 아니라 다음 경계 열이다. 따라서
                        // horizontalPixel을 중심 좌표로 다시 바꾸지 않고 직접 비교한다.
                        clip(fillDirection * (edgePixel - horizontalPixel - 0.5));
                    }
                    else
                    {
                        float edge = (fillDirection > 0.0 ? progress : 1.0 - progress) +
                                     (signedSlantPixels / texelWidth) * (IN.localUv.y - 0.5);
                        clip(fillDirection * (edge - horizontal));
                    }
                }

                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif

                return color;
            }
            ENDCG
        }
    }
}
