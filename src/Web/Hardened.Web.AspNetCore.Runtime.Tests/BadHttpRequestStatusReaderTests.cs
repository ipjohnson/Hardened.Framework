using Microsoft.AspNetCore.Http;
using Xunit;

namespace Hardened.Web.AspNetCore.Runtime.Tests;

/// <summary>
/// The status the server put on a request it refused while the pipeline read the body.
/// </summary>
public class BadHttpRequestStatusReaderTests
{
    private static readonly BadHttpRequestStatusReader Reader = new();

    [Theory]
    [InlineData(413)]
    [InlineData(400)]
    public void TheServersRefusalCarriesItsStatus(int status)
    {
        Assert.Equal(status, Reader.StatusOf(new BadHttpRequestException("refused", status)));
    }

    [Fact]
    public void AnyOtherExceptionIsNotRead()
    {
        Assert.Null(Reader.StatusOf(new InvalidOperationException("broken")));
    }
}
