using Hardened.Requests.Abstract.Headers;
using Microsoft.Extensions.Primitives;

namespace Hardened.Requests.Abstract.Tests.Headers;

/// <summary>
/// The rule for answering a GET or HEAD with a 304, and the order RFC 9110 §13.2.1 fixes for the
/// two conditionals.
///
/// <para>
/// The rule is shared by <c>[ConditionalGet]</c> and the static content writer, so a case missed
/// here is a case both get wrong the same way.
/// </para>
/// </summary>
public class PreconditionTests {

    private const string Tag = "\"abc\"";

    private static readonly DateTimeOffset Noon =
        new(2026, 8, 18, 12, 0, 0, TimeSpan.Zero);

    private static StringValues Date(DateTimeOffset when) => new(HttpDate.Format(when));

    #region If-None-Match

    [Fact]
    public void AMatchingTagIsNotModified() {
        Assert.True(Precondition.NotModified(new StringValues(Tag), StringValues.Empty, Tag, null));
    }

    [Fact]
    public void ADifferentTagIsModified() {
        Assert.False(Precondition.NotModified(new StringValues("\"xyz\""), StringValues.Empty, Tag, null));
    }

    /// <summary>
    /// A response with no validator is never a 304, whatever the caller claims to hold - the
    /// wildcard included, since nothing was ever given to hold.
    /// </summary>
    [Theory]
    [InlineData("\"abc\"")]
    [InlineData("*")]
    public void IfNoneMatchAgainstAResponseWithNoTagIsModified(string header) {
        Assert.False(Precondition.NotModified(new StringValues(header), StringValues.Empty, null, Noon));
    }

    /// <summary>
    /// The validator wins outright when it is present, including when it does not match. A client
    /// that sent both meant the validator, and the date is not consulted at all.
    /// </summary>
    [Fact]
    public void AMismatchedTagIsModifiedWhateverTheDateSays() {
        Assert.False(Precondition.NotModified(new StringValues("\"xyz\""), Date(Noon), Tag, Noon));
    }

    #endregion

    #region If-Modified-Since

    /// <summary>
    /// Equality counts as unchanged: the header has one-second precision, so "the same second" is
    /// as close to "the same" as it can express.
    /// </summary>
    [Fact]
    public void IfModifiedSinceAtLastModifiedIsNotModified() {
        Assert.True(Precondition.NotModified(StringValues.Empty, Date(Noon), null, Noon));
    }

    [Fact]
    public void IfModifiedSinceAfterLastModifiedIsNotModified() {
        Assert.True(Precondition.NotModified(StringValues.Empty, Date(Noon.AddHours(1)), null, Noon));
    }

    [Fact]
    public void IfModifiedSinceBeforeLastModifiedIsModified() {
        Assert.False(Precondition.NotModified(StringValues.Empty, Date(Noon.AddSeconds(-1)), null, Noon));
    }

    /// <summary>
    /// A modification time carrying milliseconds is compared to the second, because that is what
    /// the client was given. Without this the sub-second remainder makes every conditional request
    /// against the resource miss, forever.
    /// </summary>
    [Fact]
    public void ASubSecondLastModifiedIsComparedToTheSecond() {
        Assert.True(Precondition.NotModified(StringValues.Empty, Date(Noon), null, Noon.AddMilliseconds(750)));
    }

    /// <summary>
    /// The date is meaningless without a modification time to compare it to, and RFC 9110 §13.1.3
    /// says to ignore it then.
    /// </summary>
    [Fact]
    public void IfModifiedSinceAgainstAResponseWithNoLastModifiedIsModified() {
        Assert.False(Precondition.NotModified(StringValues.Empty, Date(Noon), Tag, null));
    }

    [Fact]
    public void AnUnparseableIfModifiedSinceIsModified() {
        Assert.False(Precondition.NotModified(StringValues.Empty, new StringValues("yesterday"), null, Noon));
    }

    #endregion

    [Fact]
    public void NoConditionalIsModified() {
        Assert.False(Precondition.NotModified(StringValues.Empty, StringValues.Empty, Tag, Noon));
    }
}
