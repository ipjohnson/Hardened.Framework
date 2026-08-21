using System.Diagnostics;
using Hardened.Shared.Runtime.Diagnostics;
using Xunit;

namespace Hardened.Shared.Runtime.Tests.Diagnostic;

/// <summary>
/// The elapsed-time reading the pipeline times requests with.
/// </summary>
/// <remarks>
/// <para>
/// These used to sleep 100ms and assert the reading landed between 100 and 200. The upper bound was
/// the problem: it asserts the operating system scheduled the thread back within 100ms of slack,
/// which is a claim about what else the machine is doing rather than about this type. It failed
/// twice in one session, both times on that bound.
/// </para>
/// <para>
/// <b>Checked against <see cref="DateTime"/>, not against <see cref="Stopwatch"/>.</b>
/// <c>MachineTimestamp</c> is a wrapper over <c>Stopwatch.GetTimestamp</c>, so a stopwatch is the
/// same clock read a second way and agreeing with it says little - the two would be wrong together
/// if the clock source were. A wall clock is genuinely independent, so it is what can say the ticks
/// were converted to milliseconds correctly.
/// </para>
/// <para>
/// Both clocks bracket the same sleep, so a machine that stalls inflates both readings and the
/// comparison still holds. That is what makes this load-independent where a fixed upper bound was
/// not.
/// </para>
/// </remarks>
public class MachineTimestampTests {

    /// <summary>
    /// Long enough that the wall clock's own resolution is small against it - roughly a millisecond
    /// here and about fifteen on Windows - and short enough not to slow the suite.
    /// </summary>
    private const int SleepMilliseconds = 200;

    /// <summary>
    /// How far the two clocks may disagree. Generous, because it absorbs each clock's resolution and
    /// the ordinary execution between the two pairs of readings; far too tight to hide a unit error,
    /// which is what this is here to catch and which would be out by a factor of a thousand.
    /// </summary>
    private const double ToleranceMilliseconds = 50;

    [Fact]
    public void GetElapsedMilliseconds_AgreesWithTheWallClock() {
        var wallStart = DateTime.UtcNow;
        var timestamp = MachineTimestamp.Now;

        Thread.Sleep(SleepMilliseconds);

        var measured = timestamp.GetElapsedMilliseconds();
        var wallElapsed = (DateTime.UtcNow - wallStart).TotalMilliseconds;

        Assert.InRange(
            measured,
            wallElapsed - ToleranceMilliseconds,
            wallElapsed + ToleranceMilliseconds);
    }

    [Fact]
    public void GetElapsedTime_AgreesWithTheWallClock() {
        var wallStart = DateTime.UtcNow;
        var timestamp = MachineTimestamp.Now;

        Thread.Sleep(SleepMilliseconds);

        var measured = timestamp.GetElapsedTime();
        var wallElapsed = DateTime.UtcNow - wallStart;

        Assert.InRange(
            measured.TotalMilliseconds,
            wallElapsed.TotalMilliseconds - ToleranceMilliseconds,
            wallElapsed.TotalMilliseconds + ToleranceMilliseconds);
    }

    /// <summary>
    /// The two accessors are one measurement in two units, so they must not disagree about it. This
    /// is what a unit error in either conversion looks like from the inside, and it needs no clock
    /// of its own to find one.
    /// </summary>
    [Fact]
    public void TheTwoAccessorsReportTheSameElapsedTime() {
        var timestamp = MachineTimestamp.Now;

        Thread.Sleep(20);

        var milliseconds = timestamp.GetElapsedMilliseconds();
        var elapsed = timestamp.GetElapsedTime();

        Assert.InRange(
            elapsed.TotalMilliseconds,
            milliseconds - ToleranceMilliseconds,
            milliseconds + ToleranceMilliseconds);
    }

    /// <summary>
    /// Time does not run backwards. A second reading of one timestamp is never less than the first,
    /// whatever the machine was doing in between - which is the guarantee a monotonic clock exists
    /// to give over a wall clock, and the one thing the wall clock above cannot be asked to confirm.
    /// </summary>
    [Fact]
    public void ElapsedTimeIsMonotonic() {
        var timestamp = MachineTimestamp.Now;

        var first = timestamp.GetElapsedMilliseconds();
        var second = timestamp.GetElapsedMilliseconds();
        var third = timestamp.GetElapsedMilliseconds();

        Assert.True(second >= first, $"{second} < {first}");
        Assert.True(third >= second, $"{third} < {second}");
    }

    /// <summary>
    /// <c>default</c> carries no reading, and the type says so rather than reporting the time since
    /// the machine started.
    /// </summary>
    [Fact]
    public void ADefaultTimestampRefusesToBeRead() {
        Assert.Throws<Exception>(() => default(MachineTimestamp).GetElapsedMilliseconds());
        Assert.Throws<Exception>(() => default(MachineTimestamp).GetElapsedTime());
    }

    /// <summary>
    /// A timestamp taken later has a smaller elapsed reading than one taken earlier, which is what
    /// says <c>Now</c> reads the clock rather than returning a constant.
    /// </summary>
    [Fact]
    public void ALaterTimestampHasElapsedLess() {
        var earlier = MachineTimestamp.Now;

        Thread.Sleep(20);

        var later = MachineTimestamp.Now;

        Assert.True(
            later.GetElapsedMilliseconds() < earlier.GetElapsedMilliseconds(),
            "A timestamp taken later must have less elapsed time than one taken earlier.");
    }

    /// <summary>
    /// Two timestamps of the same tick count are the same timestamp, which is what lets a caller
    /// compare them directly rather than comparing two readings taken at different moments.
    /// </summary>
    [Fact]
    public void TimestampsOfTheSameTickCountAreEqual() {
        var ticks = Stopwatch.GetTimestamp();

        Assert.Equal(MachineTimestamp.FromTicks(ticks), MachineTimestamp.FromTicks(ticks));
        Assert.NotEqual(MachineTimestamp.FromTicks(ticks), MachineTimestamp.FromTicks(ticks + 1));
    }
}
