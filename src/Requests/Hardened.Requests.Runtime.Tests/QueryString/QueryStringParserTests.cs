using Hardened.Requests.Runtime.QueryString;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.QueryString;

/// <summary>
/// The one query string parser both the Kestrel host and the test host read through.
/// </summary>
/// <remarks>
/// It became one implementation because the two had drifted: the test host split on every
/// <c>'='</c> and stored the raw substring, so an encoded value answered 400 under test and 200 on
/// Kestrel. The cases below are the ones that told them apart.
/// </remarks>
public class QueryStringParserTests {

    [Fact]
    public void Parse_DecodesAPercentEscapedValue() {
        var encoded = Uri.EscapeDataString("2026-09-10T09:00:00+00:00");

        var result = QueryStringParser.Parse("asOf=" + encoded);

        Assert.Equal("2026-09-10T09:00:00+00:00", result.Get("asOf").ToString());
    }

    [Fact]
    public void Parse_DecodesAPercentEscapedKey() {
        var result = QueryStringParser.Parse("as%20of=now");

        Assert.Equal("now", result.Get("as of").ToString());
    }

    /// <summary>
    /// Base64 pads with <c>'='</c>, which the old test-host parser dropped outright.
    /// </summary>
    [Fact]
    public void Parse_KeepsAValueContainingAnEqualsSign() {
        var result = QueryStringParser.Parse("cursor=YWJjZA==");

        Assert.Equal("YWJjZA==", result.Get("cursor").ToString());
    }

    [Fact]
    public void Parse_ReadsAPairWithNoValueAsAFlag() {
        var result = QueryStringParser.Parse("includeArchived&page=2");

        Assert.Equal("", result.Get("includeArchived").ToString());
        Assert.Equal("2", result.Get("page").ToString());
    }

    /// <summary>
    /// A form-encoded query writes a space as <c>'+'</c>; a literal plus arrives as <c>%2B</c>.
    /// </summary>
    [Fact]
    public void Parse_ReadsAPlusAsASpaceAndAnEscapedPlusAsItself() {
        var result = QueryStringParser.Parse("title=East+of+Eden&offset=%2B2");

        Assert.Equal("East of Eden", result.Get("title").ToString());
        Assert.Equal("+2", result.Get("offset").ToString());
    }

    [Fact]
    public void Parse_AcceptsALeadingQuestionMark() {
        var result = QueryStringParser.Parse("?page=2");

        Assert.Equal("2", result.Get("page").ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("?")]
    public void Parse_IsEmptyForNothingToParse(string? raw) {
        Assert.Equal(0, QueryStringParser.Parse(raw).Count);
    }

    [Fact]
    public void Parse_SkipsAnEmptyPair() {
        var result = QueryStringParser.Parse("page=2&&size=10");

        Assert.Equal(2, result.Count);
        Assert.Equal("10", result.Get("size").ToString());
    }

    [Fact]
    public void Parse_KeepsAnEmptyValue() {
        var result = QueryStringParser.Parse("q=");

        Assert.Equal(1, result.Count);
        Assert.Equal("", result.Get("q").ToString());
    }

    [Fact]
    public void ParseFromPath_TakesTheQueryFromAWholeRequestTarget() {
        var result = QueryStringParser.ParseFromPath("/reports/overdue?asOf=2026-09-10&page=2");

        Assert.Equal("2026-09-10", result.Get("asOf").ToString());
        Assert.Equal("2", result.Get("page").ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/reports/overdue")]
    [InlineData("/reports/overdue?")]
    public void ParseFromPath_IsEmptyWithoutAQuery(string? path) {
        Assert.Equal(0, QueryStringParser.ParseFromPath(path).Count);
    }

    #region a repeated key

    /// <summary>
    /// A repeated key used to overwrite, so the array style OpenAPI defaults to arrived as its last
    /// value alone - and the loss happened here, before binding could see there was a list.
    /// </summary>
    [Fact]
    public void ARepeatedKeyKeepsEveryValue() {
        Assert.Equal(["EUR", "GBP"], QueryStringParser.Parse("symbols=EUR&symbols=GBP").Get("symbols"));
    }

    /// <summary>In the order they were sent, which is the order the handler receives them.</summary>
    [Fact]
    public void ARepeatedKeyKeepsItsOrder() {
        Assert.Equal(["3", "1", "2"], QueryStringParser.Parse("id=3&id=1&id=2").Get("id"));
    }

    /// <summary>One key however often it repeats, so Count stays the number of names.</summary>
    [Fact]
    public void ARepeatedKeyIsCountedOnce() {
        Assert.Equal(1, QueryStringParser.Parse("symbols=EUR&symbols=GBP").Count);
    }

    [Fact]
    public void ARepeatedKeyKeepsAnEmptyValue() {
        Assert.Equal(["EUR", ""], QueryStringParser.Parse("symbols=EUR&symbols=").Get("symbols"));
    }

    [Fact]
    public void EachRepeatIsDecodedOnItsOwn() {
        Assert.Equal(
            ["a b", "c+d"],
            QueryStringParser.Parse("q=a+b&q=c%2Bd").Get("q"));
    }

    #endregion

    /// <summary>
    /// The divergence itself: both hosts now answer the same for the same request target.
    /// </summary>
    [Fact]
    public void ParseFromPath_AgreesWithParseOnTheSameQuery() {
        var encoded = Uri.EscapeDataString("2026-09-10T09:00:00+00:00");

        var fromPath = QueryStringParser.ParseFromPath("/reports/overdue?asOf=" + encoded);
        var fromQuery = QueryStringParser.Parse("asOf=" + encoded);

        Assert.Equal(fromQuery.Get("asOf").ToString(), fromPath.Get("asOf").ToString());
    }
}
