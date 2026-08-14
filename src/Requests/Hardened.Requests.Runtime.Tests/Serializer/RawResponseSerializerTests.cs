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

    // ── what it volunteers for ─────────────────────────────────────────

    [Fact]
    public void CanProduce_AStringIsOfferedAsTextPlain() {
        Assert.True(new RawResponseSerializer().CanProduce("text/plain", ContextFor("hello")));
    }

    /// <summary>
    /// A client asking for JSON gets JSON, even from a handler returning a string. That is the
    /// difference between offering a representation and forcing one.
    /// </summary>
    [Fact]
    public void CanProduce_AStringIsNotOfferedAsJson() {
        Assert.False(new RawResponseSerializer().CanProduce("application/json", ContextFor("hello")));
    }

    /// <summary>
    /// Bytes have no media type anyone could guess, so they are not volunteered at all. They are
    /// still written when the response says what they are - see the committed cases below.
    /// </summary>
    [Theory]
    [InlineData("*/*")]
    [InlineData("text/plain")]
    [InlineData("application/octet-stream")]
    public void CanProduce_BytesAreNotVolunteeredWithoutACommittedContentType(string mediaType) {
        Assert.False(new RawResponseSerializer().CanProduce(mediaType, ContextFor(new byte[] { 1, 2 })));
    }

    [Fact]
    public void CanProduce_FalseForAValueThatIsNotAlreadyBytes() {
        Assert.False(new RawResponseSerializer().CanProduce("text/plain", ContextFor(new { Name = "x" })));
    }

    [Fact]
    public void CanProduce_FalseForANullResponseValue() {
        Assert.False(new RawResponseSerializer().CanProduce("*/*", ContextFor(null)));
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
    /// Behind JSON, so a client that expressed no preference still gets JSON. Ahead of it, every
    /// handler returning a bare string would change what it answers.
    /// </summary>
    [Fact]
    public void Order_IsBehindTheJsonSerializers() {
        IResponseSerializer raw = new RawResponseSerializer();

        Assert.Equal((int)ResponseSerializerOrder.Deferred, raw.Order);
        Assert.True(raw.Order > (int)ResponseSerializerOrder.Normal);
    }

    [Fact]
    public void IsDefaultSerializer_IsFalse() {
        Assert.False(new RawResponseSerializer().IsDefaultSerializer);
    }
}
