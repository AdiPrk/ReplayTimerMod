using ReplayTimerMod;
using Xunit;

namespace ReplayTimerMod.Tests
{
    public class TimeUtilTests
    {
        [Theory]
        [InlineData(0f, "0:00.00")]
        [InlineData(5.25f, "0:05.25")]
        [InlineData(65.43f, "1:05.43")]
        [InlineData(600f, "10:00.00")]
        public void Format(float t, string expected) =>
            Assert.Equal(expected, TimeUtil.Format(t));

        [Theory]
        [InlineData(0f, false, "+0.00")]
        [InlineData(0f, true, "+00.00")]
        [InlineData(3.5f, false, "+3.50")]
        [InlineData(3.5f, true, "+03.50")]
        [InlineData(-3.5f, false, "-3.50")]
        [InlineData(65.43f, false, "+1:05.43")]
        [InlineData(-65.43f, false, "-1:05.43")]
        public void FormatDelta(float t, bool pad, string expected) =>
            Assert.Equal(expected, TimeUtil.FormatDelta(t, pad));
    }
}
