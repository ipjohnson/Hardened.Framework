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
    /// The span the deadline tests add to a timestamp. Long enough that no plausible pause between
    /// two adjacent statements is a measurable part of it, so a deadline built from it is still in
    /// the future when it is read back on the next line.
    /// </summary>
    private const int DeadlineMilliseconds = 60_000;

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
        Assert.Throws<Exception>(() => default(MachineTimestamp).GetRemainingMilliseconds());
        Assert.Throws<Exception>(() => default(MachineTimestamp).GetRemainingSeconds());
        Assert.Throws<Exception>(() => default(MachineTimestamp).Past);
        Assert.Throws<Exception>(() => default(MachineTimestamp).Present);
        Assert.Throws<Exception>(() => default(MachineTimestamp).Future);
        Assert.Throws<Exception>(() => default(MachineTimestamp).AddMs(1000));
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
        Assert.True(MachineTimestamp.FromTicks(ticks) == MachineTimestamp.FromTicks(ticks));
        Assert.True(MachineTimestamp.FromTicks(ticks) != MachineTimestamp.FromTicks(ticks + 1));
        Assert.Equal(
            MachineTimestamp.FromTicks(ticks).GetHashCode(),
            MachineTimestamp.FromTicks(ticks).GetHashCode());

        // The boxed overload is the one a non-generic collection reaches, and it is reached by
        // nothing else in this file.
        Assert.True(MachineTimestamp.FromTicks(ticks).Equals((object)MachineTimestamp.FromTicks(ticks)));
        Assert.False(MachineTimestamp.FromTicks(ticks).Equals((object)MachineTimestamp.FromTicks(ticks + 1)));
        Assert.False(MachineTimestamp.FromTicks(ticks).Equals("not a timestamp"));
        Assert.False(MachineTimestamp.FromTicks(ticks).Equals(null));
    }
    /// <summary>
    /// Adding milliseconds puts the timestamp that far ahead of where it was. The conversion runs
    /// the other way from the elapsed readings above, milliseconds into machine ticks, so it is its
    /// own chance at a unit error and gets its own wall clock check.
    /// </summary>
    /// <remarks>
    /// The reading is corrected by the wall clock rather than compared to the added value directly.
    /// Time passes between taking the timestamp and reading the deadline back, and on a loaded
    /// machine it can be a lot of time. That shows up in both clocks, so the sum of the two is what
    /// stays put.
    /// </remarks>
    [Fact]
    public void AddMsPutsTheTimestampThatFarAhead() {
        var wallStart = DateTime.UtcNow;
        var deadline = MachineTimestamp.Now.AddMs(DeadlineMilliseconds);

        var remaining = deadline.GetRemainingMilliseconds();
        var wallElapsed = (DateTime.UtcNow - wallStart).TotalMilliseconds;

        Assert.InRange(
            remaining + wallElapsed,
            DeadlineMilliseconds - ToleranceMilliseconds,
            DeadlineMilliseconds + ToleranceMilliseconds);
    }

    /// <summary>
    /// A negative value moves the timestamp back, and the reading it produces carries the sign that
    /// says so.
    /// </summary>
    [Fact]
    public void AddingNegativeMillisecondsPutsTheTimestampBehind() {
        var wallStart = DateTime.UtcNow;
        var passed = MachineTimestamp.Now.AddMs(-DeadlineMilliseconds);

        var remaining = passed.GetRemainingMilliseconds();
        var wallElapsed = (DateTime.UtcNow - wallStart).TotalMilliseconds;

        Assert.InRange(
            remaining + wallElapsed,
            -DeadlineMilliseconds - ToleranceMilliseconds,
            -DeadlineMilliseconds + ToleranceMilliseconds);
    }

    /// <summary>
    /// A deadline is in the future until its span has passed and in the past afterwards. Waiting it
    /// out is the only assertion here that needs the clock to advance, and a machine that stalls
    /// only makes it more true.
    /// </summary>
    [Fact]
    public void ADeadlineIsPastOnceItsSpanHasElapsed() {
        var deadline = MachineTimestamp.Now.AddMs(SleepMilliseconds);

        Assert.True(deadline.Future, "A deadline is in the future before its span elapses.");

        Thread.Sleep(SleepMilliseconds * 2);

        Assert.True(deadline.Past, "A deadline is in the past once its span has elapsed.");
        Assert.True(
            deadline.GetRemainingMilliseconds() < 0,
            $"{deadline.GetRemainingMilliseconds()} was not negative for an elapsed deadline.");
    }

    /// <summary>
    /// Exactly one of the three holds, whatever the timestamp. Anything else and a caller that tests
    /// them in turn either takes two branches or takes none.
    /// </summary>
    [Fact]
    public void ATimestampIsPastPresentOrFutureAndOnlyOne() {
        foreach (var timestamp in new[] {
                     MachineTimestamp.Now.AddMs(DeadlineMilliseconds),
                     MachineTimestamp.Now.AddMs(-DeadlineMilliseconds),
                     MachineTimestamp.Now
                 }) {
            var held = new[] { timestamp.Past, timestamp.Present, timestamp.Future }.Count(x => x);

            Assert.True(held == 1, $"{held} of Past, Present and Future held at once.");
        }
    }

    /// <summary>
    /// One reading in two units, the same pairing the elapsed accessors have, and the same factor of
    /// a thousand between a right answer and a wrong one.
    /// </summary>
    [Fact]
    public void TheTwoRemainingAccessorsReportTheSameTime() {
        var deadline = MachineTimestamp.Now.AddMs(DeadlineMilliseconds);

        var milliseconds = deadline.GetRemainingMilliseconds();
        var seconds = deadline.GetRemainingSeconds();

        Assert.InRange(
            seconds * 1000,
            milliseconds - ToleranceMilliseconds,
            milliseconds + ToleranceMilliseconds);
    }

    /// <summary>
    /// Time remaining and time elapsed are the same distance measured from opposite ends, so on a
    /// timestamp that has gone by they differ only in sign.
    /// </summary>
    [Fact]
    public void RemainingIsElapsedNegated() {
        var timestamp = MachineTimestamp.Now;

        Thread.Sleep(20);

        var elapsed = timestamp.GetElapsedMilliseconds();
        var remaining = timestamp.GetRemainingMilliseconds();

        Assert.InRange(
            remaining,
            -elapsed - ToleranceMilliseconds,
            -elapsed + ToleranceMilliseconds);
    }
    /// <summary>
    /// Timestamps order by the moment they were taken, which is what lets a caller hold several and
    /// ask which came first without reading elapsed time off each one at some later moment.
    /// </summary>
    [Fact]
    public void TimestampsOrderByWhenTheyWereTaken() {
        var ticks = Stopwatch.GetTimestamp();
        var earlier = MachineTimestamp.FromTicks(ticks);
        var later = MachineTimestamp.FromTicks(ticks + 1);

        Assert.True(earlier < later);
        Assert.True(later > earlier);
        Assert.True(earlier <= later);
        Assert.True(later >= earlier);
        Assert.True(earlier <= MachineTimestamp.FromTicks(ticks));
        Assert.True(earlier >= MachineTimestamp.FromTicks(ticks));
        Assert.False(later < earlier);
        Assert.False(earlier > later);

        Assert.True(earlier.CompareTo(later) < 0);
        Assert.True(later.CompareTo(earlier) > 0);
        Assert.Equal(0, earlier.CompareTo(MachineTimestamp.FromTicks(ticks)));
    }

    /// <summary>
    /// Subtracting one timestamp from another gives the time between them. The wall clock brackets
    /// the same pair, so it is what says the machine ticks were converted to a time span correctly.
    /// </summary>
    [Fact]
    public void SubtractingTwoTimestampsGivesTheTimeBetweenThem() {
        var wallStart = DateTime.UtcNow;
        var earlier = MachineTimestamp.Now;

        Thread.Sleep(SleepMilliseconds);

        var later = MachineTimestamp.Now;
        var wallElapsed = DateTime.UtcNow - wallStart;

        Assert.InRange(
            (later - earlier).TotalMilliseconds,
            wallElapsed.TotalMilliseconds - ToleranceMilliseconds,
            wallElapsed.TotalMilliseconds + ToleranceMilliseconds);
    }

    /// <summary>
    /// The difference runs negative the other way round, so a caller subtracting in the wrong order
    /// is told rather than handed a plausible positive number.
    /// </summary>
    [Fact]
    public void SubtractingInTheOtherOrderIsNegated() {
        var ticks = Stopwatch.GetTimestamp();
        var earlier = MachineTimestamp.FromTicks(ticks);
        var later = earlier.AddMs(DeadlineMilliseconds);

        Assert.Equal(-(later - earlier), earlier - later);
        Assert.True((earlier - later).Ticks < 0);
    }

    /// <summary>
    /// Subtracting <c>Now</c> from a timestamp is the reading <see cref="MachineTimestamp.GetElapsedTime"/>
    /// gives, negated. Two ways to the same measurement, so they must not disagree about it.
    /// </summary>
    [Fact]
    public void TheDifferenceFromNowIsTheElapsedTime() {
        var timestamp = MachineTimestamp.Now;

        Thread.Sleep(20);

        var elapsed = timestamp.GetElapsedTime();
        var difference = MachineTimestamp.Now - timestamp;

        Assert.InRange(
            difference.TotalMilliseconds,
            elapsed.TotalMilliseconds - ToleranceMilliseconds,
            elapsed.TotalMilliseconds + ToleranceMilliseconds);
    }

    /// <summary>
    /// Subtraction is a reading like the others, so an uninitialized timestamp is refused on either
    /// side of it rather than measured from the moment the machine started. Ordering and equality
    /// are deliberately not guarded this way, because a sort or a dictionary must not throw.
    /// </summary>
    [Fact]
    public void SubtractingADefaultTimestampThrows() {
        var timestamp = MachineTimestamp.Now;

        Assert.Throws<Exception>(() => timestamp - default(MachineTimestamp));
        Assert.Throws<Exception>(() => default(MachineTimestamp) - timestamp);

        Assert.False(default(MachineTimestamp) == timestamp);
        Assert.True(default(MachineTimestamp) < timestamp);
    }
}
