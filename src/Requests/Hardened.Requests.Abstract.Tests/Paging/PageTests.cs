using Hardened.Requests.Abstract.Paging;

namespace Hardened.Requests.Abstract.Tests.Paging;

public class PageTests
{
    private static string TokenFor(int row) => "after-" + row;

    [Fact]
    public void ARowBeyondThePageIsDroppedAndSetsTheToken()
    {
        var page = Page.From(new[] { 1, 2, 3 }, 2, TokenFor);

        Assert.Equal(new[] { 1, 2 }, page.Items);
        Assert.Equal("after-2", page.NextPageToken);
    }

    /// <summary>
    /// The token is built from the last row on the page however many extra rows the query read.
    /// </summary>
    [Fact]
    public void SeveralRowsBeyondThePageStillTokenTheLastRowOnIt()
    {
        var page = Page.From(new[] { 1, 2, 3, 4, 5 }, 2, TokenFor);

        Assert.Equal(new[] { 1, 2 }, page.Items);
        Assert.Equal("after-2", page.NextPageToken);
    }

    /// <summary>
    /// A full last page has no token, so a client following tokens stops on it rather than asking
    /// for an empty page.
    /// </summary>
    [Fact]
    public void RowsThatFillThePageExactlyGetNoToken()
    {
        var page = Page.From(
            new[] { 1, 2 },
            2,
            _ => throw new InvalidOperationException("Not called.")
        );

        Assert.Equal(new[] { 1, 2 }, page.Items);
        Assert.Null(page.NextPageToken);
    }

    [Fact]
    public void FewerRowsThanThePageGetNoToken()
    {
        var page = Page.From(new[] { 1 }, 2, TokenFor);

        Assert.Equal(new[] { 1 }, page.Items);
        Assert.Null(page.NextPageToken);
    }

    [Fact]
    public void NoRowsIsAnEmptyLastPage()
    {
        var page = Page.From(Array.Empty<int>(), 2, TokenFor);

        Assert.Empty(page.Items);
        Assert.Null(page.NextPageToken);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void APageSizeBelowOneIsRefused(int pageSize)
    {
        var refused = Assert.Throws<ArgumentOutOfRangeException>(() =>
            Page.From(new[] { 1, 2 }, pageSize, TokenFor)
        );

        Assert.Equal("pageSize", refused.ParamName);
    }
}
