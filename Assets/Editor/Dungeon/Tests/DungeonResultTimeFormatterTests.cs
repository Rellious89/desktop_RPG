using Dungeon;
using NUnit.Framework;

namespace DungeonEditor.Tests
{
    public sealed class DungeonResultTimeFormatterTests
    {
        [TestCase(0d, "00:00:00")]
        [TestCase(65d, "00:01:05")]
        [TestCase(3600d, "01:00:00")]
        [TestCase(86399d, "23:59:59")]
        public void UnderOneDay_UsesTwoDigitClock(double elapsedSeconds, string expected)
        {
            Assert.IsTrue(DungeonResultPanel.TryFormatElapsedTime(elapsedSeconds, out string actual));
            Assert.AreEqual(expected, actual);
        }

        [Test]
        public void AtLeastOneDay_RequiresLocalizedDayOrMoreText()
        {
            Assert.IsFalse(DungeonResultPanel.TryFormatElapsedTime(86400d, out string actual));
            Assert.IsNull(actual);
        }

        [TestCase(-1d)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        public void InvalidElapsedTime_IsSafelyDisplayedAsZero(double elapsedSeconds)
        {
            Assert.IsTrue(DungeonResultPanel.TryFormatElapsedTime(elapsedSeconds, out string actual));
            Assert.AreEqual("00:00:00", actual);
        }
    }
}
