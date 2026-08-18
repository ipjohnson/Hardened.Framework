using Hardened.Shared.Runtime.Utilities;
using Xunit;

namespace Hardened.Shared.Runtime.Tests.Utilities;

/// <summary>
/// Truncation and epoch conversion, neither of which had a test.
/// </summary>
/// <remarks>
/// <c>Floor</c> is what a partition key or a metric bucket is built from, so the two properties that
/// matter are that it truncates rather than rounds — a value one tick before the next second must
/// not land in it — and that it preserves <see cref="DateTimeKind"/>. Losing the kind turns a UTC
/// timestamp into an unspecified one, which the next conversion reads as local.
/// </remarks>
public class DateTimeExtensionsTests {

    [Theory]
    [InlineData(DateTimePrecision.Millisecond)]
    [InlineData(DateTimePrecision.Second)]
    [InlineData(DateTimePrecision.Minute)]
    [InlineData(DateTimePrecision.Hour)]
    [InlineData(DateTimePrecision.Day)]
    public void FloorLeavesNoRemainderAtItsPrecision(DateTimePrecision precision) {
        var value = new DateTime(2026, 8, 18, 13, 47, 29, 856, DateTimeKind.Utc).AddTicks(4321);

        Assert.Equal(0, value.Floor(precision).Ticks % (long)precision);
    }

    [Fact]
    public void FloorTruncatesRatherThanRounds() {
        var value = new DateTime(2026, 8, 18, 13, 47, 29, DateTimeKind.Utc)
            .AddTicks(TimeSpan.TicksPerSecond - 1);

        Assert.Equal(
            new DateTime(2026, 8, 18, 13, 47, 29, DateTimeKind.Utc),
            value.Floor(DateTimePrecision.Second));
    }

    [Fact]
    public void FloorOfAnAlreadyFlooredValueIsItself() {
        var value = new DateTime(2026, 8, 18, 13, 0, 0, DateTimeKind.Utc);

        Assert.Equal(value, value.Floor(DateTimePrecision.Hour));
    }

    /// <summary>
    /// Dropping the kind is how a UTC timestamp becomes one the next conversion reads as local.
    /// </summary>
    [Theory]
    [InlineData(DateTimeKind.Utc)]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void FloorPreservesTheKind(DateTimeKind kind) {
        var value = new DateTime(2026, 8, 18, 13, 47, 29, 856, kind);

        Assert.Equal(kind, value.Floor(DateTimePrecision.Minute).Kind);
    }

    [Fact]
    public void ToEpochOfTheEpochIsZero() {
        Assert.Equal(0, new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).ToEpoch());
    }

    [Fact]
    public void ToEpochCountsWholeSeconds() {
        Assert.Equal(
            1_000_000_000, new DateTime(2001, 9, 9, 1, 46, 40, DateTimeKind.Utc).ToEpoch());
    }

    [Fact]
    public void ToEpochIsNegativeBeforeTheEpoch() {
        Assert.Equal(-86_400, new DateTime(1969, 12, 31, 0, 0, 0, DateTimeKind.Utc).ToEpoch());
    }

    [Fact]
    public void ToEpochMillisecondsKeepsTheSubSecondPart() {
        Assert.Equal(
            1_000_000_000_250,
            new DateTime(2001, 9, 9, 1, 46, 40, 250, DateTimeKind.Utc).ToEpochMilliseconds());
    }

    /// <summary>
    /// The two conversions have to agree, or a record written with one and read with the other
    /// lands a second apart.
    /// </summary>
    [Fact]
    public void ToEpochAndToEpochMillisecondsAgree() {
        var value = new DateTime(2026, 8, 18, 13, 47, 29, DateTimeKind.Utc);

        Assert.Equal(value.ToEpoch() * 1000, value.ToEpochMilliseconds());
    }
}
