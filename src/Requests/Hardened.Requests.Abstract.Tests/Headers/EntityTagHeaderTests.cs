using Hardened.Requests.Abstract.Headers;
using Microsoft.Extensions.Primitives;

namespace Hardened.Requests.Abstract.Tests.Headers;

/// <summary>
/// Formatting an entity-tag, and reading an <c>If-None-Match</c> that names one.
///
/// <para>
/// Both halves are wire format. A tag written without quotes is not an entity-tag and a client is
/// entitled to discard it; a list read with string equality answers correctly for a lone strong tag
/// and for nothing else. The failure in either direction is quiet - a full body where a 304 was due
/// - so the exact strings are asserted rather than the shape.
/// </para>
/// </summary>
public class EntityTagHeaderTests {

    #region formatting

    /// <summary>
    /// Quoted, because <c>opaque-tag = DQUOTE *etagc DQUOTE</c>. The hash arrives as base64, which
    /// contains characters that may not appear in an unquoted header value of this shape.
    /// </summary>
    [Fact]
    public void AnOpaqueValueIsQuoted() {
        Assert.Equal("\"OybX3FuqNfSKoSm+h1FJqQ==\"", EntityTagHeader.Format("OybX3FuqNfSKoSm+h1FJqQ=="));
    }

    /// <summary>
    /// A variant's tag differs from the resource's, inside the quotes. Two representations sharing
    /// one validator is what tells a cache holding both that they are interchangeable.
    /// </summary>
    [Fact]
    public void AVariantGetsItsOwnTag() {
        var plain = EntityTagHeader.Format("abc");
        var gzip = EntityTagHeader.Format("abc", "gzip");
        var brotli = EntityTagHeader.Format("abc", "br");

        Assert.Equal("\"abc-gzip\"", gzip);
        Assert.Equal("\"abc-br\"", brotli);
        Assert.NotEqual(plain, gzip);
        Assert.NotEqual(gzip, brotli);
    }

    #endregion

    #region matching

    [Fact]
    public void TheSameTagMatches() {
        Assert.True(EntityTagHeader.Matches(new StringValues("\"abc\""), "\"abc\""));
    }

    [Fact]
    public void ADifferentTagDoesNotMatch() {
        Assert.False(EntityTagHeader.Matches(new StringValues("\"xyz\""), "\"abc\""));
    }

    /// <summary>
    /// A tag and its variant are different representations and must not match each other, which is
    /// the whole reason the variant is in the opaque part.
    /// </summary>
    [Fact]
    public void AVariantTagDoesNotMatchTheResourceTag() {
        Assert.False(EntityTagHeader.Matches(new StringValues("\"abc-gzip\""), "\"abc\""));
        Assert.False(EntityTagHeader.Matches(new StringValues("\"abc\""), "\"abc-gzip\""));
    }

    /// <summary>
    /// A client holding several representations sends them all. Splitting on commas is the obvious
    /// reading; not splitting at all is what string equality does, and it answers false for every
    /// list.
    /// </summary>
    [Theory]
    [InlineData("\"one\", \"abc\", \"three\"")]
    [InlineData("\"abc\",\"two\"")]
    [InlineData("\"one\", \"abc\"")]
    [InlineData("  \"abc\"  ")]
    public void ATagAnywhereInTheListMatches(string header) {
        Assert.True(EntityTagHeader.Matches(new StringValues(header), "\"abc\""));
    }

    [Fact]
    public void AListWithoutTheTagDoesNotMatch() {
        Assert.False(EntityTagHeader.Matches(new StringValues("\"one\", \"two\""), "\"abc\""));
    }

    /// <summary>
    /// <c>*</c> asks whether the resource exists at all. Reaching this code means it does.
    /// </summary>
    [Theory]
    [InlineData("*")]
    [InlineData(" * ")]
    public void TheWildcardMatches(string header) {
        Assert.True(EntityTagHeader.Matches(new StringValues(header), "\"abc\""));
    }

    /// <summary>
    /// RFC 9110 §13.1.2 requires weak comparison for <c>If-None-Match</c> specifically, so a client
    /// that was handed a weak validator and one that was handed a strong one are asking the same
    /// question.
    /// </summary>
    [Theory]
    [InlineData("W/\"abc\"", "\"abc\"")]
    [InlineData("\"abc\"", "W/\"abc\"")]
    [InlineData("W/\"abc\"", "W/\"abc\"")]
    [InlineData("\"one\", W/\"abc\"", "\"abc\"")]
    public void WeakAndStrongCompareEqual(string header, string etag) {
        Assert.True(EntityTagHeader.Matches(new StringValues(header), etag));
    }

    /// <summary>Two header lines carrying one tag each.</summary>
    [Fact]
    public void ATagInEitherOfTwoValuesMatches() {
        Assert.True(EntityTagHeader.Matches(
            new StringValues(new[] { "\"one\"", "\"abc\"" }), "\"abc\""));
    }

    #endregion

    #region nothing to match against

    [Fact]
    public void NoHeaderDoesNotMatch() {
        Assert.False(EntityTagHeader.Matches(StringValues.Empty, "\"abc\""));
    }

    [Fact]
    public void NoTagToCompareAgainstDoesNotMatch() {
        Assert.False(EntityTagHeader.Matches(new StringValues("*"), ""));
    }

    /// <summary>
    /// Malformed input answers false rather than throwing. It arrives from the network, and a
    /// header nobody can parse is a header that says nothing about what the client holds.
    /// </summary>
    [Theory]
    [InlineData("abc")]
    [InlineData("\"unterminated")]
    [InlineData("W/")]
    [InlineData(",,,")]
    [InlineData("\"one\", garbage, \"abc\"")]
    public void MalformedInputDoesNotMatch(string header) {
        Assert.False(EntityTagHeader.Matches(new StringValues(header), "\"abc\""));
    }

    /// <summary>
    /// An entity-tag's opaque part may contain a comma, so the list is walked rather than split.
    /// Ours never would - they are base64 - but a validator arriving here was written by whatever
    /// served the resource last.
    /// </summary>
    [Fact]
    public void ATagContainingACommaIsNotCutInHalf() {
        Assert.True(EntityTagHeader.Matches(new StringValues("\"a,b\""), "\"a,b\""));
        Assert.False(EntityTagHeader.Matches(new StringValues("\"a,b\""), "\"a\""));
    }

    #endregion
}
