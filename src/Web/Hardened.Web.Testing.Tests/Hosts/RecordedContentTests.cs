using System.Net.Http.Headers;
using System.Text;
using Xunit;

namespace Hardened.Web.Testing.Tests.Hosts;

/// <summary>
/// The content a socket host hands on: the body recorded when the client buffers it, and passed
/// through untouched when the client reads it as a stream.
/// </summary>
public class RecordedContentTests
{
    private static CancellationToken Token => Xunit.TestContext.Current.CancellationToken;

    private static ByteArrayContent Inner()
    {
        var inner = new ByteArrayContent(Encoding.UTF8.GetBytes("{\"n\":1}\n"));

        inner.Headers.ContentType = new MediaTypeHeaderValue("application/x-ndjson");

        return inner;
    }

    [Fact]
    public async Task ABufferedReadRecordsTheBody()
    {
        byte[]? recorded = null;

        using var content = new RecordedContent(Inner(), body => recorded = body);

        var read = await content.ReadAsByteArrayAsync(Token);

        Assert.Equal("{\"n\":1}\n", Encoding.UTF8.GetString(read));
        Assert.Equal(read, recorded);
    }

    [Fact]
    public async Task AStreamedReadRecordsNothing()
    {
        byte[]? recorded = null;

        using var content = new RecordedContent(Inner(), body => recorded = body);
        using var reader = new StreamReader(await content.ReadAsStreamAsync(Token));

        Assert.Equal("{\"n\":1}", await reader.ReadLineAsync(Token));
        Assert.Null(recorded);
    }

    /// <summary>
    /// The inner content's headers come along, and a length it never stated is not invented.
    /// </summary>
    [Fact]
    public void TheHeadersAreTheInnerContents()
    {
        using var content = new RecordedContent(Inner(), _ => { });

        Assert.Equal("application/x-ndjson", content.Headers.ContentType?.MediaType);
        Assert.Null(content.Headers.ContentLength);
    }
}
