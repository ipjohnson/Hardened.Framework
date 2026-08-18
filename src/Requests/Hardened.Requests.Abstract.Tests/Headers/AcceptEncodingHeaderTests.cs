using Hardened.Requests.Abstract.Headers;
using Microsoft.Extensions.Primitives;

namespace Hardened.Requests.Abstract.Tests.Headers;

/// <summary>
/// Whether a request takes a content coding.
///
/// <para>
/// The header this reads decides between writing bytes that are already compressed and inflating
/// them per request, so a wrong answer is not a wrong response - it is the right response produced
/// the most expensive way available, silently, forever. The cases that matter are the ones a real
/// client sends, which is why the browser header appears verbatim below rather than as a lone
/// coding.
/// </para>
/// </summary>
public class AcceptEncodingHeaderTests {

    #region what real clients send

    /// <summary>
    /// The header Chrome, Firefox and Safari all send. It arrives as one value listing four
    /// codings, and reading it as a collection of codings - which is what
    /// <c>StringValues.Contains</c> does - answers false for every one of them.
    /// </summary>
    [Theory]
    [InlineData("gzip, deflate, br, zstd")]
    [InlineData("gzip, deflate, br")]
    [InlineData("gzip, deflate")]
    [InlineData("gzip")]
    [InlineData("deflate, gzip")]
    public void AListContainingGZipAcceptsGZip(string header) {
        Assert.True(AcceptEncodingHeader.Accepts(new StringValues(header), KnownEncoding.GZip));
    }

    [Theory]
    [InlineData("gzip, deflate, br, zstd")]
    [InlineData("br")]
    [InlineData("deflate, br")]
    public void AListContainingBrotliAcceptsBrotli(string header) {
        Assert.True(AcceptEncodingHeader.Accepts(new StringValues(header), KnownEncoding.Br));
    }

    /// <summary>Two header lines rather than one, which is equally legal.</summary>
    [Fact]
    public void ACodingInEitherOfTwoValuesIsFound() {
        var header = new StringValues(new[] { "deflate", "gzip, br" });

        Assert.True(AcceptEncodingHeader.Accepts(header, KnownEncoding.GZip));
        Assert.True(AcceptEncodingHeader.Accepts(header, KnownEncoding.Br));
    }

    #endregion

    #region what is not a match

    [Theory]
    [InlineData("deflate, br, zstd")]
    [InlineData("identity")]
    [InlineData("")]
    public void AListWithoutGZipDoesNotAcceptIt(string header) {
        Assert.False(AcceptEncodingHeader.Accepts(new StringValues(header), KnownEncoding.GZip));
    }

    [Fact]
    public void NoHeaderAtAllDoesNotAccept() {
        Assert.False(AcceptEncodingHeader.Accepts(StringValues.Empty, KnownEncoding.GZip));
    }

    /// <summary>
    /// A coding whose name merely contains the one being looked for is a different coding. This is
    /// the case a substring search gets wrong, and it is why the search is bounded on both sides.
    /// </summary>
    [Theory]
    [InlineData("x-gzip")]
    [InlineData("gzip2")]
    [InlineData("notgzip")]
    [InlineData("x-gzip, deflate")]
    public void ANeighbouringCodingIsNotAMatch(string header) {
        Assert.False(AcceptEncodingHeader.Accepts(new StringValues(header), KnownEncoding.GZip));
    }

    /// <summary>
    /// <c>br</c> is two characters and appears inside several ordinary coding names, so it is the
    /// coding most exposed to an unbounded search.
    /// </summary>
    [Theory]
    [InlineData("brotli")]
    [InlineData("x-br")]
    public void ATwoCharacterCodingIsStillBounded(string header) {
        Assert.False(AcceptEncodingHeader.Accepts(new StringValues(header), KnownEncoding.Br));
    }

    #endregion

    #region shape

    /// <summary>Case is not significant in a coding name.</summary>
    [Theory]
    [InlineData("GZIP")]
    [InlineData("GZip, Deflate")]
    public void MatchingIsCaseInsensitive(string header) {
        Assert.True(AcceptEncodingHeader.Accepts(new StringValues(header), KnownEncoding.GZip));
    }

    /// <summary>
    /// A quality value is ignored rather than parsed - <c>gzip;q=0</c> still reads as accepted.
    /// Documented rather than fixed: the cost of being wrong here is a response compressed for a
    /// client that would rather have had it plain, and the parser that would avoid it is larger
    /// than the problem.
    /// </summary>
    [Fact]
    public void AQualityValueIsIgnored() {
        Assert.True(AcceptEncodingHeader.Accepts(new StringValues("gzip;q=0"), KnownEncoding.GZip));
    }

    [Fact]
    public void AnEmptyCodingNeverMatches() {
        Assert.False(AcceptEncodingHeader.Accepts(new StringValues("gzip, deflate"), ""));
    }

    #endregion
}
