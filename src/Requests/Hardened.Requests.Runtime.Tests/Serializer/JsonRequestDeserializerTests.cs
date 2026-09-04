using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.Serializer;
using Hardened.Requests.Runtime.Configuration;
using Hardened.Requests.Runtime.Serializer;
using Hardened.Requests.Runtime.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Serializer;

/// <summary>
/// Both JSON request deserializers, held to the same table since the implementations are
/// duplicated rather than shared.
/// </summary>
/// <remarks>
/// This file was <c>RequestDeserializerContentEncodingTests</c> while the two deserializers
/// unwrapped gzip and Brotli themselves, which left a form body, a Newtonsoft body and a raw body
/// unable to arrive compressed at all. That table moved unchanged to
/// <c>RequestDecompressionFilterTests</c>, and
/// <see cref="ACompressedBodyIsNotDecodedByTheDeserializer"/> pins that the branch is gone from
/// here rather than duplicated.
/// </remarks>
public class JsonRequestDeserializerTests {

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

    private static IExecutionContext Context(byte[] body, string? contentEncoding = null) {
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

    [Theory]
    [MemberData(nameof(DeserializerNames))]
    public async Task ABodyIsReadAsPlainJson(string deserializerName) {
        var payload = await DeserializerNamed(deserializerName)
            .DeserializeRequestBody<Payload>(Context(Encoding.UTF8.GetBytes(Json)));

        Assert.Equal("encoded", payload!.Name);
        Assert.Equal(7, payload.Value);
    }

    /// <summary>
    /// The deserializer reads what it is handed. Decoding happens once, in
    /// <c>RequestDecompressionFilter</c> ahead of the bind, which also removes the header - so a
    /// deserializer that still looked at <c>Content-Encoding</c> would be reading a header that
    /// describes bytes it will never see.
    /// </summary>
    [Theory]
    [MemberData(nameof(DeserializerNames))]
    public async Task ACompressedBodyIsNotDecodedByTheDeserializer(string deserializerName) {
        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await DeserializerNamed(deserializerName)
                .DeserializeRequestBody<Payload>(Context(GZipped(Json), KnownEncoding.GZip)));
    }

    [Theory]
    [MemberData(nameof(DeserializerNames))]
    public void ADeserializerHandlesAJsonContentTypeAndNothingElse(string deserializerName) {
        var deserializer = DeserializerNamed(deserializerName);

        Assert.True(deserializer.CanProcessContext(Context(Encoding.UTF8.GetBytes(Json))));

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
