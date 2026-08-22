using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.Serializer;
using Hardened.Requests.Runtime.Configuration;
using Hardened.Requests.Runtime.Errors;
using Hardened.Requests.Runtime.Serializer;
using Hardened.Requests.Runtime.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Serializer;

/// <summary>
/// Compressed request bodies. A client that sets <c>Content-Encoding</c> is telling the
/// deserializer the bytes are not JSON yet, and a deserializer that ignores the header reads
/// gzip's magic number as the first character of a document.
///
/// <para>
/// Both deserializers implement this the same way in duplicated code, so both are held to the
/// same table.
/// </para>
/// </summary>
public class RequestDeserializerContentEncodingTests {

    private record Payload(string Name, int Value);

    private static IOptions<IJsonSerializerConfiguration> Config() =>
        Options.Create<IJsonSerializerConfiguration>(new JsonSerializerConfiguration {
            DeSerializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        });

    private static IRequestDeserializer DeserializerNamed(string name) => name switch {
        nameof(SystemTextJsonRequestDeserializer) =>
            new SystemTextJsonRequestDeserializer(
                Config(),
                NullLogger<SystemTextJsonRequestDeserializer>.Instance,
                Array.Empty<IJsonTypeInfoResolver>()),
        nameof(AotRequestDeserializer) =>
            new AotRequestDeserializer(
                Config(),
                NullLogger<AotRequestDeserializer>.Instance,
                new IJsonTypeInfoResolver[] { new DefaultJsonTypeInfoResolver() }),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "unknown deserializer")
    };

    public static TheoryData<string> DeserializerNames => new() {
        nameof(SystemTextJsonRequestDeserializer),
        nameof(AotRequestDeserializer)
    };

    private const string Json = """{"name":"encoded","value":7}""";

    private static IExecutionContext Context(byte[] body, string? contentEncoding) {
        var context = Pipeline.Context(method: "POST", body: body);

        context.Request.Headers[KnownHeaders.ContentType] = new StringValues("application/json");

        if (contentEncoding is not null) {
            context.Request.Headers[KnownHeaders.ContentEncoding] = new StringValues(contentEncoding);
        }

        return context;
    }

    private static byte[] GZipped(string content) {
        var output = new MemoryStream();

        using (var gzip = new GZipStream(output, CompressionLevel.Fastest, true)) {
            var bytes = Encoding.UTF8.GetBytes(content);

            gzip.Write(bytes, 0, bytes.Length);
        }

        return output.ToArray();
    }

    private static byte[] Brotlied(string content) {
        var output = new MemoryStream();

        using (var brotli = new BrotliStream(output, CompressionLevel.Fastest, true)) {
            var bytes = Encoding.UTF8.GetBytes(content);

            brotli.Write(bytes, 0, bytes.Length);
        }

        return output.ToArray();
    }

    [Theory]
    [MemberData(nameof(DeserializerNames))]
    public async Task AnUncompressedBodyIsReadAsPlainJson(string deserializerName) {
        var payload = await DeserializerNamed(deserializerName)
            .DeserializeRequestBody<Payload>(Context(Encoding.UTF8.GetBytes(Json), null));

        Assert.Equal("encoded", payload!.Name);
        Assert.Equal(7, payload.Value);
    }

    [Theory]
    [MemberData(nameof(DeserializerNames))]
    public async Task AGzippedBodyIsDecompressedBeforeItIsRead(string deserializerName) {
        var payload = await DeserializerNamed(deserializerName)
            .DeserializeRequestBody<Payload>(Context(GZipped(Json), KnownEncoding.GZip));

        Assert.Equal("encoded", payload!.Name);
        Assert.Equal(7, payload.Value);
    }

    [Theory]
    [MemberData(nameof(DeserializerNames))]
    public async Task ABrotliBodyIsDecompressedBeforeItIsRead(string deserializerName) {
        var payload = await DeserializerNamed(deserializerName)
            .DeserializeRequestBody<Payload>(Context(Brotlied(Json), KnownEncoding.Br));

        Assert.Equal("encoded", payload!.Name);
        Assert.Equal(7, payload.Value);
    }

    /// <summary>
    /// <c>Content-Encoding</c> may carry several values; the encoding is recognised from
    /// anywhere in the list rather than only when it stands alone.
    /// </summary>
    [Theory]
    [MemberData(nameof(DeserializerNames))]
    public async Task AnEncodingIsRecognisedWhenItArrivesAlongsideOthers(string deserializerName) {
        var context = Pipeline.Context(method: "POST", body: GZipped(Json));

        context.Request.Headers[KnownHeaders.ContentType] = new StringValues("application/json");
        context.Request.Headers[KnownHeaders.ContentEncoding] =
            new StringValues(new[] { "identity", KnownEncoding.GZip });

        var payload = await DeserializerNamed(deserializerName).DeserializeRequestBody<Payload>(context);

        Assert.Equal("encoded", payload!.Name);
    }

    /// <summary>
    /// An encoding the deserializers cannot read is the client's mistake, so it is refused by
    /// name rather than producing a parse error from halfway through a compressed stream.
    /// </summary>
    [Theory]
    [InlineData(nameof(SystemTextJsonRequestDeserializer), "deflate")]
    [InlineData(nameof(SystemTextJsonRequestDeserializer), "compress")]
    [InlineData(nameof(SystemTextJsonRequestDeserializer), "zstd")]
    [InlineData(nameof(AotRequestDeserializer), "deflate")]
    [InlineData(nameof(AotRequestDeserializer), "compress")]
    [InlineData(nameof(AotRequestDeserializer), "zstd")]
    public async Task AnUnsupportedContentEncodingIsRefusedByName(
        string deserializerName, string encoding) {

        var exception = await Assert.ThrowsAsync<BadContentEncodingException>(async () =>
            await DeserializerNamed(deserializerName)
                .DeserializeRequestBody<Payload>(Context(Encoding.UTF8.GetBytes(Json), encoding)));

        Assert.Contains(encoding, exception.Message);
    }

    /// <summary>
    /// <see cref="BadContentEncodingException"/> is a client error by type. It reached 400
    /// only by having "Bad" in its name until it was given
    /// <see cref="BadRequestException"/> as a base; a rename would have made it a 500.
    /// </summary>
    [Fact]
    public void AnUnsupportedContentEncodingIsAClientErrorByType() {
        Assert.IsAssignableFrom<BadRequestException>(new BadContentEncodingException("deflate"));
    }

    /// <summary>
    /// A body that claims gzip and is not gzip fails as a malformed stream rather than being
    /// read as text.
    /// </summary>
    [Theory]
    [MemberData(nameof(DeserializerNames))]
    public async Task ABodyThatLiesAboutItsEncodingFails(string deserializerName) {
        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await DeserializerNamed(deserializerName)
                .DeserializeRequestBody<Payload>(
                    Context(Encoding.UTF8.GetBytes(Json), KnownEncoding.GZip)));
    }

    [Theory]
    [MemberData(nameof(DeserializerNames))]
    public void ADeserializerHandlesAJsonContentTypeAndNothingElse(string deserializerName) {
        var deserializer = DeserializerNamed(deserializerName);

        Assert.True(deserializer.CanProcessContext(
            Context(Encoding.UTF8.GetBytes(Json), null)));

        var formEncoded = Pipeline.Context(method: "POST");
        formEncoded.Request.Headers[KnownHeaders.ContentType] =
            new StringValues("application/x-www-form-urlencoded");

        Assert.False(deserializer.CanProcessContext(formEncoded));
        Assert.False(deserializer.CanProcessContext(Pipeline.Context(method: "POST")));
    }

    /// <summary>
    /// Both are default serializers, which is what lets a request with no usable
    /// <c>Content-Type</c> still be read as JSON rather than rejected.
    /// </summary>
    [Theory]
    [MemberData(nameof(DeserializerNames))]
    public void BothDeserializersOfferThemselvesAsTheDefault(string deserializerName) {
        Assert.True(DeserializerNamed(deserializerName).IsDefaultSerializer);
    }
}
