using Common;
using NUnit.Framework;

namespace CommonEditor.Tests
{
    public sealed class SessionElapsedCompanionReactionTests
    {
        [Test]
        public void Threshold_WaitsForActiveActor_AndFiresOnlyOnce()
        {
            Assert.IsFalse(SessionElapsedCompanionReaction.ShouldShow(4.9f, 5f, false, true));
            Assert.IsFalse(SessionElapsedCompanionReaction.ShouldShow(5f, 5f, false, false));
            Assert.IsTrue(SessionElapsedCompanionReaction.ShouldShow(5f, 5f, false, true));
            Assert.IsFalse(SessionElapsedCompanionReaction.ShouldShow(10f, 5f, true, true));
        }

    }
}
