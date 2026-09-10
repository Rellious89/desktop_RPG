using Common;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace CommonEditor.Tests
{
    public sealed class SlantedProgressBarTests
    {
        [TestCase(-1f, 10f, 0f)]
        [TestCase(3f, 10f, 0.3f)]
        [TestCase(20f, 10f, 1f)]
        [TestCase(3f, 0f, 0f)]
        public void NormalizeValue_ClampsAndHandlesEmptyMaximum(float current, float maximum, float expected)
        {
            Assert.That(SlantedProgressBar.NormalizeValue(current, maximum), Is.EqualTo(expected).Within(0.0001f));
        }

        [Test]
        public void SnapNormalizedValue_RoundsToNearestFillPixel_WithoutChangingEndpoints()
        {
            Assert.That(SlantedProgressBar.SnapNormalizedValue(0f, 8), Is.EqualTo(0f));
            Assert.That(SlantedProgressBar.SnapNormalizedValue(1f, 8), Is.EqualTo(1f));
            Assert.That(SlantedProgressBar.SnapNormalizedValue(0.31f, 8), Is.EqualTo(0.25f));
            Assert.That(SlantedProgressBar.SnapNormalizedValue(0.32f, 8), Is.EqualTo(0.375f));
        }

        [Test]
        public void SnappedSlantedEdge_UsesFixedSixRowPatternAndRigidOnePixelTranslation()
        {
            const int width = 71;
            const int height = 6;
            const float slantPixels = 6f;

            // 6px 높이와 6px slant에서는 행 중심마다 정확히 한 칸 차이나는 계단이다.
            int[] expectedOffsets = { -2, -1, 0, 1, 2, 3 };

            for (int row = 0; row < height; row++)
            {
                Assert.That(SlantedProgressBar.GetSlantedRowOffsetPixel(row, height, slantPixels),
                    Is.EqualTo(expectedOffsets[row]));
            }

            // 여러 중간 진행값에서 pattern은 그대로이고, 기준 pixel만 매번 정확히 +1 이동한다.
            for (int basePixel = 10; basePixel <= 13; basePixel++)
            {
                float value = basePixel / (float)width;
                for (int row = 0; row < height; row++)
                {
                    int edge = SlantedProgressBar.GetSnappedSlantedEdgePixel(
                        value, width, row, height, slantPixels);
                    Assert.That(edge, Is.EqualTo(basePixel + expectedOffsets[row]));
                }
            }
        }

        [Test]
        public void EachBarOwnsItsRuntimeMaterial()
        {
            GameObject first = new GameObject("First", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            GameObject second = new GameObject("Second", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            Texture2D texture = new Texture2D(8, 1, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
            };
            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, 8f, 1f), new Vector2(0.5f, 0.5f));
            try
            {
                // pixelSnap 기본값을 끄지 않는다. 실제 픽셀 Fill처럼 8개 텍셀을 제공해야 0.25와
                // 0.75가 각각 정확히 2/8, 6/8 경계로 유지된다.
                first.GetComponent<Image>().sprite = sprite;
                second.GetComponent<Image>().sprite = sprite;

                SlantedProgressBar firstBar = first.AddComponent<SlantedProgressBar>();
                SlantedProgressBar secondBar = second.AddComponent<SlantedProgressBar>();

                firstBar.NormalizedValue = 0.25f;
                secondBar.NormalizedValue = 0.75f;

                Material firstMaterial = first.GetComponent<Image>().material;
                Material secondMaterial = second.GetComponent<Image>().material;
                Assert.That(firstMaterial, Is.Not.SameAs(secondMaterial));
                Assert.That(firstMaterial.shader.name, Is.EqualTo("KeyBuddy/UI/Slanted Progress Fill"));
                Assert.That(secondMaterial.shader.name, Is.EqualTo("KeyBuddy/UI/Slanted Progress Fill"));
            }
            finally
            {
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(second);
                Object.DestroyImmediate(sprite);
                Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void MeshEffect_EncodesProgressAndStaticLocalCoordinatesIntoUvChannels()
        {
            GameObject root = new GameObject("Fill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            try
            {
                SlantedProgressBarMeshEffect effect = root.AddComponent<SlantedProgressBarMeshEffect>();
                // 정확히 중간값도 Material이 아니라 네 정점 모두에 기록돼, Mask의 stencil material
                // clone을 거쳐도 절반 진행 상태가 사라지지 않는다.
                effect.Configure(0.5f, 1f, 1f, true, 8, 2);

                using (VertexHelper vertices = new VertexHelper())
                {
                    AddVertex(vertices, -4f, -1f);
                    AddVertex(vertices, 4f, -1f);
                    AddVertex(vertices, 4f, 1f);
                    AddVertex(vertices, -4f, 1f);
                    effect.ModifyMesh(vertices);

                    UIVertex bottomLeft = default;
                    UIVertex topRight = default;
                    vertices.PopulateUIVertex(ref bottomLeft, 0);
                    vertices.PopulateUIVertex(ref topRight, 2);

                    Assert.That(bottomLeft.uv0.z, Is.EqualTo(0.5f).Within(0.0001f));
                    Assert.That(bottomLeft.uv0.w, Is.EqualTo(1f).Within(0.0001f));
                    Assert.That(bottomLeft.uv1.x, Is.EqualTo(0f).Within(0.0001f));
                    Assert.That(bottomLeft.uv1.y, Is.EqualTo(0f).Within(0.0001f));
                    Assert.That(topRight.uv1.x, Is.EqualTo(1f).Within(0.0001f));
                    Assert.That(topRight.uv1.y, Is.EqualTo(1f).Within(0.0001f));
                    Assert.That(topRight.uv1.z, Is.EqualTo(1f).Within(0.0001f));
                    Assert.That(topRight.uv1.w, Is.EqualTo(1f).Within(0.0001f));
                    Assert.That(topRight.uv2.x, Is.EqualTo(8f).Within(0.0001f));
                    Assert.That(topRight.uv2.y, Is.EqualTo(2f).Within(0.0001f));
                }
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static void AddVertex(VertexHelper vertices, float x, float y)
        {
            UIVertex vertex = UIVertex.simpleVert;
            vertex.position = new Vector3(x, y, 0f);
            vertex.uv0 = Vector4.zero;
            vertices.AddVert(vertex);
        }
    }
}
