using System.Text;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Runtime.Serializer;
using Hardened.Requests.Runtime.Tests.Support;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Serializer;

/// <summary>
/// <c>[RawResponse]</c> writes the handler's return value to the body untouched - no JSON
/// envelope, no content negotiation. Only three shapes make sense as raw bytes, and the helper
/// refuses anything else rather than writing a type name.
/// </summary>
public class RawOutputHelperTests {

    private static string Body(IExecutionContext context) =>
        Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray());

    [Theory]
    [InlineData("text/plain")]
    [InlineData("text/html")]
    [InlineData("text/csv")]
    [InlineData("application/octet-stream")]
    public async Task TheConfiguredContentTypeIsWhatTheResponseAdvertises(string contentType) {
        var context = Pipeline.Context();
        context.Response.ResponseValue = "body";

        await RawOutputHelper.OutputFunc(contentType)(context);

        Assert.Equal(contentType, context.Response.ContentType);
    }

    [Fact]
    public async Task AStringIsWrittenAsUtf8Text() {
        var context = Pipeline.Context();
        context.Response.ResponseValue = "<html><body>hello</body></html>";

        await RawOutputHelper.OutputFunc("text/html")(context);

        Assert.Equal("<html><body>hello</body></html>", Body(context));
    }

    /// <summary>
    /// Non-ASCII text survives the write, which it would not if the helper narrowed each char
    /// to a byte.
    /// </summary>
    [Fact]
    public async Task NonAsciiTextIsWrittenAsUtf8RatherThanNarrowed() {
        var context = Pipeline.Context();
        context.Response.ResponseValue = "café — 日本語";

        await RawOutputHelper.OutputFunc("text/plain")(context);

        Assert.Equal("café — 日本語", Body(context));
    }

    [Fact]
    public async Task AByteArrayIsWrittenVerbatim() {
        var context = Pipeline.Context();
        var payload = new byte[] { 0x00, 0x1f, 0x8b, 0xff, 0x7f };

        context.Response.ResponseValue = payload;

        await RawOutputHelper.OutputFunc("application/octet-stream")(context);

        Assert.Equal(payload, ((MemoryStream)context.Response.Body).ToArray());
    }

    [Fact]
    public async Task AStreamIsCopiedIntoTheResponseBody() {
        var context = Pipeline.Context();
        context.Response.ResponseValue = new MemoryStream("streamed content"u8.ToArray());

        await RawOutputHelper.OutputFunc("text/plain")(context);

        Assert.Equal("streamed content", Body(context));
    }

    /// <summary>
    /// An empty string is a legitimate raw response and writes nothing rather than failing the
    /// type check.
    /// </summary>
    [Fact]
    public async Task AnEmptyStringWritesAnEmptyBody() {
        var context = Pipeline.Context();
        context.Response.ResponseValue = "";

        await RawOutputHelper.OutputFunc("text/plain")(context);

        Assert.Equal("", Body(context));
    }

    /// <summary>
    /// A handler that returned nothing closes the body rather than leaving the transport
    /// waiting for bytes that are not coming.
    /// </summary>
    [Fact]
    public async Task ANullResponseValueClosesTheBody() {
        var context = Pipeline.Context();

        await RawOutputHelper.OutputFunc("text/plain")(context);

        Assert.False(context.Response.Body.CanWrite);
    }

    /// <summary>
    /// Anything that is not text, bytes or a stream is a mistake in the handler's signature,
    /// and it says which three shapes are allowed rather than serializing a type name into the
    /// response.
    /// </summary>
    // Rows are TheoryDataRow rather than a TheoryData<object> collection initializer: for an object
    // element, Add(object) and Add(TheoryDataRow<object>) are both applicable and the call is
    // ambiguous under xunit.v3.
    public static IEnumerable<TheoryDataRow<object>> UnsupportedRawValues => [
        new(42),
        new(4.2),
        new(true),
        new(new[] { "an", "array", "of", "strings" }),
        new(new { Name = "an anonymous type" })
    ];

    [Theory]
    [MemberData(nameof(UnsupportedRawValues))]
    public async Task AnUnsupportedRawValueIsRefused(object value) {
        var context = Pipeline.Context();
        context.Response.ResponseValue = value;

        var exception = await Assert.ThrowsAsync<Exception>(
            () => RawOutputHelper.OutputFunc("text/plain")(context));

        Assert.Contains("must be string, byte[], or Stream", exception.Message);
    }
}
