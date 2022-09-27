using Hardened.Shared.Runtime.Diagnostics;
using Xunit;

namespace Hardened.Shared.Runtime.Tests.Diagnostic
{
    public class MachineTimestampTests
    {
        [Fact]
        public void MillisecondsTest()
        {
            var timestamp = MachineTimestamp.Now;

            Thread.Sleep(100);

            var milliseconds = timestamp.GetElapsedMilliseconds();

            Assert.True(milliseconds > 100);
            Assert.True(milliseconds < 200);
        }

        [Fact]
        public void TimeSpanTest()
        {
            var timestamp = MachineTimestamp.Now;

            Thread.Sleep(100);

            var elapsedTime = timestamp.GetElapsedTime();

            Assert.True(elapsedTime.TotalMilliseconds > 100);
            Assert.True(elapsedTime.TotalMilliseconds < 200);
        }
    }
}
