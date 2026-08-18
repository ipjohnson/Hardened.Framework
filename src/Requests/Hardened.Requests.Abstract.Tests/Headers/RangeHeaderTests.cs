using Hardened.Requests.Abstract.Headers;
using Microsoft.Extensions.Primitives;

namespace Hardened.Requests.Abstract.Tests.Headers;

/// <summary>
/// Resolving a <c>Range</c> against a known length.
///
/// <para>
/// The distinction that matters throughout is between a range that must be <em>ignored</em> and one
/// that must be <em>refused</em>. RFC 9110 §14.2 says an unparseable range is ignored - the client
/// gets the whole entity - while a range that parsed and asks for bytes past the end is a 416. Both
/// look like "no partial content" from one side and are entirely different answers from the other,
/// so every case below says which it is.
/// </para>
/// </summary>
public class RangeHeaderTests {

    private const long Length = 1000;

    private static RangeResult Resolve(string? header, out ByteRange range) =>
        RangeHeader.Resolve(
            header == null ? StringValues.Empty : new StringValues(header), Length, out range);

    #region ranges that resolve

    [Theory]
    [InlineData("bytes=0-499", 0, 499)]
    [InlineData("bytes=500-999", 500, 999)]
    [InlineData("bytes=0-0", 0, 0)]
    [InlineData("bytes=999-999", 999, 999)]
    public void AClosedRangeResolvesToItself(string header, long from, long to) {
        Assert.Equal(RangeResult.Satisfiable, Resolve(header, out var range));
        Assert.Equal(from, range.From);
        Assert.Equal(to, range.To);
    }

    /// <summary>An open end means "to the end", which is what a media element sends first.</summary>
    [Fact]
    public void AnOpenEndedRangeRunsToTheEnd() {
        Assert.Equal(RangeResult.Satisfiable, Resolve("bytes=500-", out var range));
        Assert.Equal(500, range.From);
        Assert.Equal(999, range.To);
    }

    /// <summary>
    /// A suffix range is the last N bytes, not a range starting at minus N. It is the shape a client
    /// uses when it knows how much tail it wants and not how long the whole is.
    /// </summary>
    [Theory]
    [InlineData("bytes=-500", 500, 999)]
    [InlineData("bytes=-1", 999, 999)]
    public void ASuffixRangeIsTheEndOfTheResource(string header, long from, long to) {
        Assert.Equal(RangeResult.Satisfiable, Resolve(header, out var range));
        Assert.Equal(from, range.From);
        Assert.Equal(to, range.To);
    }

    /// <summary>A suffix longer than the resource is the whole resource, not an error.</summary>
    [Fact]
    public void ASuffixLongerThanTheResourceIsTheWholeResource() {
        Assert.Equal(RangeResult.Satisfiable, Resolve("bytes=-5000", out var range));
        Assert.Equal(0, range.From);
        Assert.Equal(999, range.To);
    }

    /// <summary>An end past the last byte is clamped rather than refused - the start was valid.</summary>
    [Fact]
    public void AnEndPastTheResourceIsClamped() {
        Assert.Equal(RangeResult.Satisfiable, Resolve("bytes=900-5000", out var range));
        Assert.Equal(900, range.From);
        Assert.Equal(999, range.To);
    }

    [Theory]
    [InlineData("bytes = 0-499")]
    [InlineData("BYTES=0-499")]
    [InlineData("  bytes=0-499  ")]
    public void WhitespaceAndCaseDoNotMatter(string header) {
        Assert.Equal(RangeResult.Satisfiable, Resolve(header, out _));
    }

    #endregion

    #region ranges that are refused

    /// <summary>
    /// A start past the end is the one shape that is an error rather than an omission: the client
    /// asked for bytes that do not exist, and telling it the length is more useful than sending the
    /// whole entity it did not ask for.
    /// </summary>
    [Theory]
    [InlineData("bytes=1000-")]
    [InlineData("bytes=1000-1500")]
    [InlineData("bytes=5000-6000")]
    public void AStartPastTheEndIsUnsatisfiable(string header) {
        Assert.Equal(RangeResult.Unsatisfiable, Resolve(header, out _));
    }

    /// <summary>
    /// A zero-length resource can satisfy no range at all, including the one that reads as
    /// "everything".
    /// </summary>
    [Theory]
    [InlineData("bytes=0-")]
    [InlineData("bytes=-100")]
    public void NoRangeOverAnEmptyResourceIsSatisfiable(string header) {
        Assert.Equal(
            RangeResult.Unsatisfiable,
            RangeHeader.Resolve(new StringValues(header), 0, out _));
    }

    /// <summary>The length a 416 names, which is how a client that guessed wrong learns what to ask.</summary>
    [Fact]
    public void AnUnsatisfiedRangeNamesTheLength() {
        Assert.Equal("bytes */1000", ByteRange.Unsatisfied(Length));
    }

    #endregion

    #region ranges that are ignored

    [Fact]
    public void NoHeaderIsNoRange() {
        Assert.Equal(RangeResult.None, Resolve(null, out _));
    }

    /// <summary>
    /// Multiple ranges are legal and answered with the whole entity. Serving the first as if it
    /// were the only one asked for would be a 206 the client did not request.
    /// </summary>
    [Theory]
    [InlineData("bytes=0-99,200-299")]
    [InlineData("bytes=0-99, 200-299, 400-499")]
    public void AMultipleRangeRequestIsIgnored(string header) {
        Assert.Equal(RangeResult.None, Resolve(header, out _));
    }

    /// <summary>
    /// Anything unparseable is ignored rather than refused, per §14.2. A 416 would tell the client
    /// its range was impossible when what happened is that it was not understood.
    /// </summary>
    [Theory]
    [InlineData("items=0-499")]
    [InlineData("bytes")]
    [InlineData("bytes=")]
    [InlineData("bytes=abc-def")]
    [InlineData("bytes=0")]
    [InlineData("bytes=-")]
    [InlineData("bytes=-0")]
    [InlineData("bytes=499-0")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bytes=-1.5")]
    [InlineData("bytes=1e3-")]
    public void AnUnparseableRangeIsIgnored(string header) {
        Assert.Equal(RangeResult.None, Resolve(header, out _));
    }

    #endregion

    #region what a 206 says

    [Fact]
    public void AContentRangeNamesTheSliceAndTheWhole() {
        Assert.Equal("bytes 500-999/1000", new ByteRange(500, 999).ContentRange(Length));
    }

    [Theory]
    [InlineData(0, 0, 1)]
    [InlineData(0, 499, 500)]
    [InlineData(500, 999, 500)]
    public void ARangeKnowsHowLongItIs(long from, long to, long expected) {
        Assert.Equal(expected, new ByteRange(from, to).Length);
    }

    #endregion
}
