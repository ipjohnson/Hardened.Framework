using System.Text;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Serializer;
using Hardened.Requests.Runtime.Serializer;
using Hardened.Requests.Runtime.Tests.Support;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Serializer;

/// <summary>
/// Writing a value that is already bytes, and deciding when that is what the client wanted.
/// </summary>
public class RawResponseSerializerTests {

    private static IExecutionContext ContextFor(object? value, string? committed = null) {
        var context = Pipeline.Context();

        context.Response.ResponseValue = value;

        if (committed != null) {
            context.Response.ContentType = committed;
        }

        return context;
    }

    private static async Task<string> Write(object? value, string? committed = null) {
        var context = ContextFor(value, committed);

        await new RawResponseSerializer().SerializeResponse(context);

        context.Response.Body.Position = 0;

        using var reader = new StreamReader(context.Response.Body);

        return await reader.ReadToEndAsync();
    }

    // ── it never volunteers ────────────────────────────────────────────

    /// <summary>
    /// Nothing is written for a response that committed to no content type, whatever its value and
    /// whatever the client asked for.
    /// </summary>
    /// <remarks>
    /// A bare string used to be offered as <c>text/plain</c> here, with this serializer ordered
    /// behind JSON so that a client expressing no preference still got JSON. An operation that
    /// declares nothing now takes the service default outright, so there is nothing left to
    /// volunteer into and nothing left for an order to separate. A handler that wants text says
    /// <c>[Produces("text/plain")]</c>.
    /// </remarks>
    [Theory]
    [InlineData("text/plain")]
    [InlineData("application/json")]
    [InlineData("*/*")]
    public void CanProduce_NothingIsVolunteeredWithoutACommittedContentType(string mediaType) {
        var serializer = new RawResponseSerializer();

        Assert.False(serializer.CanProduce(mediaType, ContextFor("hello")));
        Assert.False(serializer.CanProduce(mediaType, ContextFor(new byte[] { 1, 2 })));
        Assert.False(serializer.CanProduce(mediaType, ContextFor(Stream.Null)));
    }

    [Fact]
    public void CanProduce_FalseForAValueThatIsNotAlreadyBytes() {
        Assert.False(
            new RawResponseSerializer().CanProduce("text/csv", ContextFor(new { Name = "x" }, "text/csv")));
    }

    [Fact]
    public void CanProduce_FalseForANullResponseValue() {
        Assert.False(new RawResponseSerializer().CanProduce("*/*", ContextFor(null, "text/plain")));
    }

    // ── a committed content type ───────────────────────────────────────

    /// <summary>
    /// Committed, it writes any of the three shapes as whatever was committed to - the point of
    /// <c>[RawResponse("application/pdf")]</c> is that the bytes go out as a PDF.
    /// </summary>
    [Theory]
    [InlineData("text/csv")]
    [InlineData("application/pdf")]
    public void CanProduce_ACommittedContentTypeIsHonoured(string committed) {
        var serializer = new RawResponseSerializer();

        Assert.True(serializer.CanProduce(committed, ContextFor("a,b", committed)));
        Assert.True(serializer.CanProduce(committed, ContextFor(new byte[] { 1 }, committed)));
        Assert.True(serializer.CanProduce(committed, ContextFor(Stream.Null, committed)));
    }

    /// <summary>
    /// And the committed type replaces text/plain rather than adding to it - a response committed to
    /// text/csv is not also offered as text/plain.
    /// </summary>
    [Fact]
    public void CanProduce_ACommittedContentTypeReplacesTheDefaultOffer() {
        Assert.False(new RawResponseSerializer().CanProduce("text/plain", ContextFor("a,b", "text/csv")));
    }

    // ── writing ────────────────────────────────────────────────────────

    [Fact]
    public async Task SerializeResponse_WritesAStringUnquoted() {
        Assert.Equal("Hello, World!", await Write("Hello, World!"));
    }

    /// <summary>
    /// StreamWriter's parameterless UTF8 encoding emits a byte order mark, which lands ahead of the
    /// body and is invisible to any assertion made on a decoded string.
    /// </summary>
    [Fact]
    public async Task SerializeResponse_WritesNoByteOrderMark() {
        var context = ContextFor("Hello");

        await new RawResponseSerializer().SerializeResponse(context);

        var body = ((MemoryStream)context.Response.Body).ToArray();

        Assert.Equal((byte)'H', body[0]);
    }

    [Fact]
    public async Task SerializeResponse_WritesBytesUnchanged() {
        Assert.Equal("ab", await Write(Encoding.UTF8.GetBytes("ab"), "application/octet-stream"));
    }

    [Fact]
    public async Task SerializeResponse_CopiesAStream() {
        Assert.Equal("streamed",
            await Write(new MemoryStream(Encoding.UTF8.GetBytes("streamed")), "text/plain"));
    }

    [Fact]
    public async Task SerializeResponse_SetsTextPlainWhenNothingWasCommitted() {
        var context = ContextFor("hello");

        await new RawResponseSerializer().SerializeResponse(context);

        Assert.Equal("text/plain", context.Response.ContentType);
    }

    [Fact]
    public async Task SerializeResponse_LeavesACommittedContentTypeAlone() {
        var context = ContextFor("a,b", "text/csv");

        await new RawResponseSerializer().SerializeResponse(context);

        Assert.Equal("text/csv", context.Response.ContentType);
    }

    // ── position in the set ────────────────────────────────────────────

    /// <summary>
    /// The tag it registers under, which is the one media type it can be asked for by name. It
    /// writes any committed type, and reaches those through the response value's shape instead.
    /// </summary>
    [Fact]
    public void ContentType_IsTextPlain() {
        IResponseSerializer raw = new RawResponseSerializer();

        Assert.Equal("text/plain", raw.ContentType);
        Assert.Equal(RawResponseSerializer.DefaultContentType, raw.ContentType);
    }

    [Fact]
    public void IsDefaultSerializer_IsFalse() {
        Assert.False(new RawResponseSerializer().IsDefaultSerializer);
    }
}
