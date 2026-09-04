using Hardened.Requests.Abstract.Headers;
using Microsoft.Extensions.Primitives;

namespace Hardened.Requests.Abstract.Tests.Headers;

/// <summary>
/// Writing the one date format HTTP has, and reading back the three a client may send.
///
/// <para>
/// Both halves are wire format, and the failure in either direction is quiet: a date written
/// with its offset unconverted is off by hours, and a date a client sent in an older form and
/// nothing here could read is a full body where a 304 was due.
/// </para>
/// </summary>
public class HttpDateTests {

    private static readonly DateTimeOffset Noon =
        new(2026, 8, 18, 12, 0, 0, TimeSpan.Zero);

    #region formatting

    [Fact]
    public void FormatsTheFixedForm() {
        Assert.Equal("Tue, 18 Aug 2026 12:00:00 GMT", HttpDate.Format(Noon));
    }

    /// <summary>
    /// The format writes <c>GMT</c> whatever offset the value carries and does not shift the time
    /// to match, so the value has to be converted first.
    /// </summary>
    [Fact]
    public void FormatConvertsToUtcFirst() {
        var local = new DateTimeOffset(2026, 8, 18, 14, 0, 0, TimeSpan.FromHours(2));

        Assert.Equal("Tue, 18 Aug 2026 12:00:00 GMT", HttpDate.Format(local));
    }

    [Fact]
    public void FormatDropsAnythingBelowASecond() {
        Assert.Equal("Tue, 18 Aug 2026 12:00:00 GMT", HttpDate.Format(Noon.AddMilliseconds(750)));
    }

    #endregion

    #region truncating

    [Fact]
    public void TruncateDropsAnythingBelowASecond() {
        Assert.Equal(Noon, HttpDate.Truncate(Noon.AddMilliseconds(999)));
        Assert.Equal(Noon, HttpDate.Truncate(Noon.AddTicks(1)));
    }

    [Fact]
    public void TruncateConvertsToUtc() {
        var local = new DateTimeOffset(2026, 8, 18, 14, 0, 0, 500, TimeSpan.FromHours(2));

        var truncated = HttpDate.Truncate(local);

        Assert.Equal(Noon, truncated);
        Assert.Equal(TimeSpan.Zero, truncated.Offset);
    }

    #endregion

    #region parsing

    [Fact]
    public void ParsesTheFixedForm() {
        Assert.True(HttpDate.TryParse(new StringValues("Tue, 18 Aug 2026 12:00:00 GMT"), out var parsed));
        Assert.Equal(Noon, parsed);
        Assert.Equal(TimeSpan.Zero, parsed.Offset);
    }

    /// <summary>
    /// The obsolete forms RFC 9110 §5.6.7 says a recipient must still accept. asctime pads a
    /// single-digit day with a space rather than a zero.
    /// </summary>
    [Theory]
    [InlineData("Tuesday, 18-Aug-26 12:00:00 GMT", 18)]
    [InlineData("Tue Aug 18 12:00:00 2026", 18)]
    [InlineData("Sat Aug  8 12:00:00 2026", 8)]
    public void ParsesTheObsoleteForms(string header, int day) {
        Assert.True(HttpDate.TryParse(new StringValues(header), out var parsed));
        Assert.Equal(new DateTimeOffset(2026, 8, day, 12, 0, 0, TimeSpan.Zero), parsed);
    }

    /// <summary>
    /// What the static content writer accepted before the parser moved here, kept so a date it
    /// read then is still read now.
    /// </summary>
    [Fact]
    public void ParsesWhatTheInvariantCultureParses() {
        Assert.True(HttpDate.TryParse(new StringValues("2026-08-18T12:00:00Z"), out var parsed));
        Assert.Equal(Noon, parsed);
    }

    /// <summary>
    /// What a value written by <see cref="HttpDate.Format"/> reads back as, which is the
    /// round trip every conditional request makes.
    /// </summary>
    [Fact]
    public void AFormattedDateReadsBackAsItself() {
        Assert.True(HttpDate.TryParse(new StringValues(HttpDate.Format(Noon)), out var parsed));
        Assert.Equal(Noon, parsed);
    }

    /// <summary>
    /// A conditional header that arrived twice is ignored rather than picked from, per RFC 9110
    /// §13.1.3.
    /// </summary>
    [Fact]
    public void MoreThanOneMemberIsNotADate() {
        var two = new StringValues(["Tue, 18 Aug 2026 12:00:00 GMT", "Tue, 18 Aug 2026 13:00:00 GMT"]);

        Assert.False(HttpDate.TryParse(two, out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a date")]
    [InlineData("\"OybX3FuqNfSKoSm+h1FJqQ==\"")]
    public void AnythingElseIsNotADate(string? header) {
        Assert.False(HttpDate.TryParse(header == null ? StringValues.Empty : new StringValues(header), out _));
    }

    #endregion
}
