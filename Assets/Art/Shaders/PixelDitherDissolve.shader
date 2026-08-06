// 픽셀 아트용 디더 디졸브. 알파를 낮춰 반투명해지는 페이드가 아니라, 픽셀을 통째로 버려서
// "남아 있는 픽셀 수가 줄어들다가 사라지는" 고전 픽셀 게임의 소멸 연출을 낸다.
//
// 핵심 두 가지:
//  1) clip()으로 버린다 - 남는 픽셀은 항상 원래 색/알파 100%다. 반투명 구간이 생기지 않는다.
//  2) 디더 패턴을 <b>텍스처 공간</b>에 찍는다 - 원본 아트의 텍셀 좌표 격자에 맞춰 버리므로,
//     스프라이트가 몇 배로 확대돼 있든 "그 그림 고유의 픽셀"이 빠지는 것처럼 보인다. 화면 픽셀
//     기준으로 찍으면 배율이 바뀔 때마다 방충망을 덧씌운 느낌이 나고 오브젝트가 움직일 때 패턴이
//     미끄러진다(이 프로젝트는 PPU에 Actor/Stage 배율이 곱해져 아트 1픽셀과 화면 1픽셀이 절대
//     같지 않다). 격자 정보(_DitherTexels/_DitherCellTexels)는 셰이더가 추측하지 않고 C#이 각
//     스프라이트에서 읽어 넣어준다 - 이유는 아래 프로퍼티 주석에 적어두었다.
//
// Bayer 4x4 오더드 디더를 쓴다 - 임계값이 규칙적으로 흩어져 있어서 100% -> 체커보드 -> 성긴 점 ->
// 소멸 순으로 고르게 얇아진다. 랜덤 노이즈로 바꾸면 지글거리며 흩어지는 다른 결이 되는데, 그건
// _NoiseDither를 켜서 고를 수 있게 해뒀다.
//
// Sprites-Default와 같은 렌더 상태(Transparent/컬링 없음/ZWrite 없음/알파 블렌드)를 그대로 쓰므로
// _DissolveAmount가 0이면 기존 스프라이트와 완전히 같은 그림이 나온다.
Shader "KeyBuddy/PixelDitherDissolve"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        // 0 = 온전한 그림, 1 = 완전히 사라짐. 렌더러마다 MaterialPropertyBlock으로 따로 먹인다.
        _DissolveAmount ("Dissolve Amount", Range(0,1)) = 0

        // 이 스프라이트가 쓰는 텍스처의 픽셀 크기(width, height). Unity의 _MainTex_TexelSize를 쓰지
        // 않고 C#(PixelDissolveGroup)이 직접 넣어준다 - _MainTex가 [PerRendererData]라 텍스처를
        // 렌더러가 넣어주는데, 그 경로에서는 _MainTex_TexelSize가 <b>갱신되지 않아</b> 머티리얼에
        // 꽂힌 텍스처(여기서는 없음 = 더미) 기준의 엉뚱한 값이 들어온다. 그러면 스프라이트 전체가
        // 몇 개의 거대한 블록으로 쪼개져 "픽셀이 빠진다"가 아니라 "덩어리가 뭉텅 사라진다"가 된다.
        [HideInInspector] _DitherTexels ("Texture Size (px)", Vector) = (512,512,0,0)

        // 디더 한 칸이 원본 텍셀 몇 개인지. C#이 "월드 유닛 기준 칸 크기 x 그 스프라이트의 PPU"로
        // 계산해서 넣으므로, PPU가 200인 몬스터와 32인 마을 프롭이 화면에서 같은 굵기로 사라진다.
        [HideInInspector] _DitherCellTexels ("Dither Cell (texels)", Float) = 1

        // 켜면 오더드 디더 대신 해시 노이즈로 흩어진다(지글거리는 결).
        [Toggle(_NOISE_DITHER)] _NoiseDither ("Noise Dither", Float) = 0

        [HideInInspector] _RendererColor ("RendererColor", Color) = (1,1,1,1)
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

        Cull Off
        Lighting Off
        ZWrite Off
        Blend One OneMinusSrcAlpha

        Pass
        {
        CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local _NOISE_DITHER
            #include "UnityCG.cginc"

            // GPU 인스턴싱을 쓰지 않는다. 인스턴싱이 켜지면 SpriteRenderer가 flipX/flipY를 메시에
            // 굽지 않고 _Flip으로 넘기는데, 그걸 셰이더가 직접 적용해야 한다 - 그 처리를 빠뜨리면
            // 뒤집힌 스프라이트가 원래 방향으로 렌더된다(몬스터가 전환 순간 반대로 보이던 원인).
            // 이 연출의 대상은 많아야 수십 개라 인스턴싱으로 얻을 이득이 없으므로, Sprites-Default와
            // 완전히 같은(= 플립이 메시에 구워지는) 경로만 남긴다.

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            fixed4 _Color;
            fixed4 _RendererColor;
            float _DissolveAmount;
            float4 _DitherTexels;
            float _DitherCellTexels;

            v2f vert(appdata_t IN)
            {
                v2f OUT;
                OUT.vertex = UnityObjectToClipPos(IN.vertex);
                OUT.texcoord = IN.texcoord;
                OUT.color = IN.color * _Color * _RendererColor;
                return OUT;
            }

            sampler2D _MainTex;

            // Bayer 4x4 임계값 행렬(0~15를 16으로 나눈 값). 값이 낮은 자리부터 먼저 사라진다.
            static const float BayerThreshold[16] = {
                 0.0 / 16.0,  8.0 / 16.0,  2.0 / 16.0, 10.0 / 16.0,
                12.0 / 16.0,  4.0 / 16.0, 14.0 / 16.0,  6.0 / 16.0,
                 3.0 / 16.0, 11.0 / 16.0,  1.0 / 16.0,  9.0 / 16.0,
                15.0 / 16.0,  7.0 / 16.0, 13.0 / 16.0,  5.0 / 16.0
            };

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, IN.texcoord) * IN.color;

                float dissolve = _DissolveAmount;
                if (dissolve > 0.0)
                {
                    // 아트의 텍셀 좌표. _DitherTexels는 이 스프라이트가 실제로 쓰는 텍스처 크기라,
                    // 아틀라스로 묶인 스프라이트에서도 원본 도트 격자를 그대로 따라간다.
                    float cell = max(1.0, _DitherCellTexels);
                    float2 texel = floor(IN.texcoord * _DitherTexels.xy / cell);

                #ifdef _NOISE_DITHER
                    float threshold = Hash21(texel);
                #else
                    int ix = (int)fmod(texel.x, 4.0);
                    int iy = (int)fmod(texel.y, 4.0);
                    float threshold = BayerThreshold[iy * 4 + ix];
                #endif

                    // dissolve가 임계값을 넘어선 픽셀부터 버린다. 1이면 모든 픽셀이 버려진다.
                    clip(threshold - dissolve);
                }

                c.rgb *= c.a;
                return c;
            }
        ENDCG
        }
    }

    Fallback "Sprites/Default"
}
