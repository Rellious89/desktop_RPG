#if UNITY_EDITOR || DEVELOPMENT_BUILD
using DevelopmentTools;
using NUnit.Framework;

namespace DevelopmentEditor.Tests
{
    public sealed class TmpReadabilityComparisonOverlayTests
    {
        [Test]
        public void ComparisonSizes_AreTheRequestedSmallTextSamples()
        {
            CollectionAssert.AreEqual(new[] { 8, 10, 12 },
                TmpReadabilityComparisonOverlay.ComparisonFontSizes);
        }

        [Test]
        public void OutlineWidth_IsClampedToSafeShaderRange()
        {
            Assert.AreEqual(0f, TmpReadabilityComparisonOverlay.ClampOutlineWidth(-1f));
            Assert.AreEqual(0.25f, TmpReadabilityComparisonOverlay.ClampOutlineWidth(0.25f));
            Assert.AreEqual(1f, TmpReadabilityComparisonOverlay.ClampOutlineWidth(2f));
        }

        [Test]
        public void CandidateConfig_UsesReadableComparisonDefaults()
        {
            var config = new TmpReadabilityComparisonOverlay.CandidateConfig();

            Assert.AreEqual(0.28f, config.strongerOutlineWidth);
            Assert.AreEqual(0.12f, config.thinOutlineWidth);
            Assert.AreEqual(0f, config.hardUnderlayDilate);
            Assert.AreEqual(0.5f, config.hardUnderlayOffsetX);
            Assert.AreEqual(-0.5f, config.hardUnderlayOffsetY);
            Assert.AreEqual(0.9f, config.hardUnderlayColor.a);
        }

        [Test]
        public void Overlay_IsDevelopmentOnlyBuildFeature()
        {
            Assert.IsTrue(TmpReadabilityComparisonOverlay.IsBuildEnabled);
        }
    }
}
#endif
