using UnityEngine;
using UnityEngine.UI;

namespace Common
{
    /// <summary>
    /// <see cref="SlantedProgressBar"/>가 실제 Fill Image의 메시마다 사선 클리핑 데이터를 기록하는
    /// 내부 효과다. 진행도는 Material property가 아니라 정점 UV에 있으므로 uGUI Mask가 만든 Stencil
    /// material clone에서도 값이 유실되지 않는다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Graphic))]
    public sealed class SlantedProgressBarMeshEffect : BaseMeshEffect
    {
        private float progress = 1f;
        private float fillDirection = 1f;
        private float signedSlantPixels;
        private float pixelSnap = 1f;
        private float texelWidth = 1f;
        private float texelHeight = 1f;

        /// <summary>셰이더가 소비할 값을 갱신하고 Fill 메시를 다시 만들도록 요청한다.</summary>
        public void Configure(
            float normalizedProgress,
            float direction,
            float signedSlantWidthPixels,
            bool snapToPixels,
            int fillTexelWidth,
            int fillTexelHeight)
        {
            progress = Mathf.Clamp01(normalizedProgress);
            fillDirection = direction >= 0f ? 1f : -1f;
            signedSlantPixels = signedSlantWidthPixels;
            pixelSnap = snapToPixels ? 1f : 0f;
            texelWidth = Mathf.Max(1, fillTexelWidth);
            texelHeight = Mathf.Max(1, fillTexelHeight);
            graphic.SetVerticesDirty();
        }

        public override void ModifyMesh(VertexHelper vertexHelper)
        {
            if (!IsActive() || vertexHelper.currentVertCount == 0) return;

            // Image.OnPopulateMesh가 실제로 만든 정점 범위를 기준으로 0~1 좌표를 구한다. 따라서
            // Image.Type=Sliced의 고정 캡/늘어난 중앙부 모두에서 사선이 표시 폭 기준으로 이동한다.
            Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            Vector2 max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            UIVertex vertex = default;
            for (int i = 0; i < vertexHelper.currentVertCount; i++)
            {
                vertexHelper.PopulateUIVertex(ref vertex, i);
                min = Vector2.Min(min, vertex.position);
                max = Vector2.Max(max, vertex.position);
            }

            Vector2 size = max - min;
            size.x = Mathf.Max(size.x, 0.0001f);
            size.y = Mathf.Max(size.y, 0.0001f);

            for (int i = 0; i < vertexHelper.currentVertCount; i++)
            {
                vertexHelper.PopulateUIVertex(ref vertex, i);
                Vector2 localUv = (Vector2)vertex.position - min;
                localUv = new Vector2(localUv.x / size.x, localUv.y / size.y);

                // uv0.xy는 Sprite 샘플링에 예약되어 있으므로 유지한다. 추가 Canvas Shader Channel은
                // uv1/uv2만 필요하다; uv0.zw는 Mesh가 원래 보유하는 TEXCOORD0의 나머지 성분이다.
                vertex.uv0.z = progress;
                vertex.uv0.w = fillDirection;
                vertex.uv1 = new Vector4(localUv.x, localUv.y, signedSlantPixels, pixelSnap);
                vertex.uv2 = new Vector4(texelWidth, texelHeight, 0f, 0f);
                vertexHelper.SetUIVertex(vertex, i);
            }
        }
    }
}
