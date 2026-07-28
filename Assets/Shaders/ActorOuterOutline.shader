// 캐릭터/몬스터 본체 SpriteRenderer 전용 외곽선 셰이더(Built-in Render Pipeline).
//
// 투명 데스크톱 윈도우에서는 캐릭터 뒤에 어떤 화면이 오는지 알 수 없으므로, 배경을 읽어서 대비를
// 계산하는 방식(GrabPass 등)은 쓸 수 없다 - 대신 스프라이트 자신의 알파만 보고 실루엣 바깥쪽
// 투명 픽셀에 고정 색을 찍는다.
//
// 규칙(작업지시서 4항):
//   1) 현재 픽셀이 불투명하면 원본 Sprite를 Sprites/Default와 완전히 동일하게 출력한다.
//   2) 현재 픽셀이 투명하지만 주변에 불투명 픽셀이 있으면 외곽선 색을 출력한다.
//   3) 현재 픽셀과 주변이 모두 투명하면 완전히 투명하게 출력한다.
//
// 반투명 픽셀(0 < a < 1)은 "원본을 외곽선 위에 얹는" 프리멀티플라이드 합성으로 처리한다 -
// 원본 색을 절대 덮어쓰지 않으면서(작업지시서 3항) 경계에 구멍이 생기지도 않는다.
//
// 주의: 외곽선은 Sprite 메시가 실제로 래스터화하는 영역 안에서만 그릴 수 있다. 스프라이트
// 임포트 설정이 Mesh Type = Tight면 메시가 알파에 딱 붙어 있어 바깥쪽 외곽선이 잘린다 -
// 이 셰이더를 쓰는 스프라이트는 Mesh Type = Full Rect여야 한다. 또한 원본 이미지에서
// 스프라이트가 텍스처 가장자리에 붙어 있으면 그쪽 외곽선은 그려질 자리가 없어 잘린다
// (현재 캐릭터/몬스터 프레임은 투명 여백이 충분해 문제되지 않는다).
Shader "KeyBuddy/Actor Outer Outline"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        [MaterialToggle] PixelSnap ("Pixel snap", Float) = 0
        [HideInInspector] _RendererColor ("RendererColor", Color) = (1,1,1,1)
        [HideInInspector] _Flip ("Flip", Vector) = (1,1,1,1)
        [PerRendererData] _AlphaTex ("External Alpha", 2D) = "white" {}
        [PerRendererData] _EnableExternalAlpha ("Enable External Alpha", Float) = 0

        [Header(Outline)]
        [MaterialToggle] _OutlineEnabled ("Outline Enabled", Float) = 1
        _OutlineColor ("Outline Color", Color) = (0.86, 0.93, 1, 0.85)
        _OutlineWidth ("Outline Width (texture pixels)", Range(1, 2)) = 1
        _OutlineAlphaCutoff ("Source Alpha Cutoff", Range(0.01, 1)) = 0.1
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        // Sprites/Default와 동일한 프리멀티플라이드 알파 블렌딩 - 투명 윈도우 합성 결과가
        // 기존 스프라이트와 완전히 같아야 하므로 절대 바꾸지 않는다.
        Blend One OneMinusSrcAlpha

        Pass
        {
        CGPROGRAM
            #pragma vertex SpriteVert
            #pragma fragment ActorOutlineFrag
            #pragma target 3.0
            #pragma multi_compile_instancing
            #pragma multi_compile_local _ PIXELSNAP_ON
            #pragma multi_compile _ ETC1_EXTERNAL_ALPHA
            #include "UnitySprites.cginc"

            // UnitySprites.cginc는 _MainTex만 선언하고 TexelSize는 선언하지 않는다.
            float4 _MainTex_TexelSize;

            fixed4 _OutlineColor;
            float _OutlineWidth;
            float _OutlineEnabled;
            float _OutlineAlphaCutoff;

            // 이 픽셀이 불투명하다고 볼 기준. 이보다 진하면 주변 검사를 아예 건너뛴다(원본 그대로 출력).
            #define OUTLINE_OPAQUE_SKIP 0.999

            /// 주변 픽셀의 알파만 읽는다. tex2D가 아니라 tex2Dlod를 쓰는 이유는 이 샘플링이 "이 픽셀이
            /// 투명한가"에 따라 갈라지는 분기 안에서 일어나기 때문이다 - 분기 안의 tex2D는 밉 레벨을
            /// 정하는 미분값이 정의되지 않아 컴파일러가 경고하거나 거부할 수 있다. 스프라이트 텍스처는
            /// 밉맵이 없으므로(Generate Mip Maps 꺼짐) LOD 0 고정 샘플링이 정확히 같은 결과다.
            float SampleSpriteAlphaLod(float2 uv)
            {
                float a = tex2Dlod(_MainTex, float4(uv, 0, 0)).a;
            #if ETC1_EXTERNAL_ALPHA
                float externalAlpha = tex2Dlod(_AlphaTex, float4(uv, 0, 0)).r;
                a = lerp(a, externalAlpha, _EnableExternalAlpha);
            #endif
                return a;
            }

            /// 3x3 이웃의 테두리(중심 제외 8칸) 중 가장 큰 알파. 이것이 곧 두께 1의 "주변 8방향 검사"이고,
            /// 두께 2에서도 안쪽 한 겹으로 그대로 쓰인다.
            float ActorOutlineInnerRingMaxAlpha(float2 uv, float2 texel)
            {
                float a = 0;
                a = max(a, SampleSpriteAlphaLod(uv + float2( texel.x,        0)));
                a = max(a, SampleSpriteAlphaLod(uv + float2(-texel.x,        0)));
                a = max(a, SampleSpriteAlphaLod(uv + float2(       0,  texel.y)));
                a = max(a, SampleSpriteAlphaLod(uv + float2(       0, -texel.y)));
                a = max(a, SampleSpriteAlphaLod(uv + float2( texel.x,  texel.y)));
                a = max(a, SampleSpriteAlphaLod(uv + float2( texel.x, -texel.y)));
                a = max(a, SampleSpriteAlphaLod(uv + float2(-texel.x,  texel.y)));
                a = max(a, SampleSpriteAlphaLod(uv + float2(-texel.x, -texel.y)));
                return a;
            }

            /// 5x5 이웃의 바깥 테두리(체비쇼프 거리가 정확히 2인 16칸) 중 가장 큰 알파.
            /// 안쪽 링 8칸과 합치면 5x5 영역 24칸을 빠짐없이 검사하게 된다 - 축/모서리 8칸만 보던
            /// 이전 방식에서는 (±2,±1)과 (±1,±2) 방향이 비어 있어, 그 방향으로만 이웃한 얇거나
            /// 비스듬한 실루엣에서 두께 2 외곽선에 구멍이 생겼다.
            /// unit은 "1칸"에 해당하는 UV 거리다(두께 2에서 정확히 1텍셀).
            float ActorOutlineOuterRingMaxAlpha(float2 uv, float2 unit)
            {
                float2 one = unit;
                float2 two = unit * 2.0;

                float a = 0;
                // 축 방향 4칸: (±2,0), (0,±2)
                a = max(a, SampleSpriteAlphaLod(uv + float2( two.x,      0)));
                a = max(a, SampleSpriteAlphaLod(uv + float2(-two.x,      0)));
                a = max(a, SampleSpriteAlphaLod(uv + float2(     0,  two.y)));
                a = max(a, SampleSpriteAlphaLod(uv + float2(     0, -two.y)));
                // 모서리 4칸: (±2,±2)
                a = max(a, SampleSpriteAlphaLod(uv + float2( two.x,  two.y)));
                a = max(a, SampleSpriteAlphaLod(uv + float2( two.x, -two.y)));
                a = max(a, SampleSpriteAlphaLod(uv + float2(-two.x,  two.y)));
                a = max(a, SampleSpriteAlphaLod(uv + float2(-two.x, -two.y)));
                // 이전에 빠져 있던 8칸: (±2,±1)
                a = max(a, SampleSpriteAlphaLod(uv + float2( two.x,  one.y)));
                a = max(a, SampleSpriteAlphaLod(uv + float2( two.x, -one.y)));
                a = max(a, SampleSpriteAlphaLod(uv + float2(-two.x,  one.y)));
                a = max(a, SampleSpriteAlphaLod(uv + float2(-two.x, -one.y)));
                // 이전에 빠져 있던 8칸: (±1,±2)
                a = max(a, SampleSpriteAlphaLod(uv + float2( one.x,  two.y)));
                a = max(a, SampleSpriteAlphaLod(uv + float2(-one.x,  two.y)));
                a = max(a, SampleSpriteAlphaLod(uv + float2( one.x, -two.y)));
                a = max(a, SampleSpriteAlphaLod(uv + float2(-one.x, -two.y)));
                return a;
            }

            fixed4 ActorOutlineFrag(v2f IN) : SV_Target
            {
                fixed4 tex = SampleSpriteTexture(IN.texcoord);

                // IN.color = 정점 색 * _Color * _RendererColor. FlashOnCue의 색 변경과 처치/리젠
                // Fade의 알파가 전부 여기에 들어 있으므로, 원본 출력 경로는 Sprites/Default와 동일하다.
                fixed4 c = tex * IN.color;
                c.rgb *= c.a;

                if (_OutlineEnabled < 0.5 || tex.a >= OUTLINE_OPAQUE_SKIP)
                {
                    return c;
                }

                float2 texel = _MainTex_TexelSize.xy;
                // 두께 1은 3x3 한 겹만 본다 - 기본값의 기존 비주얼을 그대로 유지하기 위해 이 경로는 건드리지 않는다.
                float neighborAlpha = ActorOutlineInnerRingMaxAlpha(IN.texcoord, texel);
                if (_OutlineWidth > 1.0)
                {
                    // 바깥 겹의 "1칸" 크기. 두께 2에서 unit = 1텍셀이 되어 5x5가 정확히 텍셀 격자에 맞고,
                    // 그 사이 값에서는 같은 5x5 배치가 비례해서 좁혀진다.
                    float2 unit = texel * (_OutlineWidth * 0.5);
                    neighborAlpha = max(neighborAlpha, ActorOutlineOuterRingMaxAlpha(IN.texcoord, unit));
                }

                // 임계값으로 딱 잘라 판정한다 - 필터링 때문에 알파가 부드럽게 번져도 출력은 항상
                // "외곽선 색" 아니면 "없음"이라 블러 진 그라데이션이 생기지 않는다.
                fixed4 outlineColor = _OutlineColor;
                // 외곽선 RGB는 Flash 등 본체 Tint의 영향을 받지 않고 설정값을 그대로 유지한다.
                // 알파만 SpriteRenderer 전체 알파를 따라가므로, 몬스터가 Fade-out되면 외곽선도 같이 사라진다.
                outlineColor.a *= IN.color.a * step(_OutlineAlphaCutoff, neighborAlpha);
                outlineColor.rgb *= outlineColor.a;

                // 원본을 외곽선 "위에" 얹는다(프리멀티플라이드 Over) - 불투명 영역은 c.a가 1이라
                // 외곽선 기여분이 정확히 0이 되어 원본 색을 전혀 덮지 않는다.
                return c + outlineColor * (1.0 - c.a);
            }
        ENDCG
        }
    }

    Fallback "Sprites/Default"
}
