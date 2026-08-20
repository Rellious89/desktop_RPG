using Dungeon;
using NUnit.Framework;

namespace DungeonEditor.Tests
{
    public sealed class DungeonResultTimeFormatterTests
    {
        [TestCase(0d, "00:00:00")]
        [TestCase(0.999d, "00:00:00")]
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

        [TestCase("Time - HH:mm:ss", "00:00:00", "Time - 00:00:00")]
        [TestCase("진행시간 - HH:mm:ss", "01:23:45", "진행시간 - 01:23:45")]
        [TestCase("HH:mm:ss", "23:59:59", "23:59:59")]
        [TestCase("진행시간 - HH:mm:ss", "1일 이상", "진행시간 - 1일 이상")]
        [TestCase("Time - HH:mm:ss", "1 day or more", "Time - 1 day or more")]
        public void LocalizedFormat_ReplacesClockTokenWithoutChangingElapsedValue(
            string format, string elapsedValue, string expected)
        {
            Assert.AreEqual(expected,
                DungeonResultPanel.ApplyElapsedFormat(format, elapsedValue));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("Time")]
        public void MissingOrMalformedLocalizedFormat_FallsBackToElapsedValue(string format)
        {
            Assert.AreEqual("01:23:45",
                DungeonResultPanel.ApplyElapsedFormat(format, "01:23:45"));
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
