using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.Serializer;
using Hardened.Requests.Runtime.Configuration;
using Hardened.Requests.Runtime.Serializer;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using NSubstitute;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Serializer;

/// <summary>
/// Both buffered JSON response serializers, held to the same table since the implementations are
/// duplicated rather than shared.
/// </summary>
/// <remarks>
/// This file was <c>ResponseSerializerCompressionTests</c> while the serializers carried a gzip
/// branch behind <c>IExecutionResponse.ShouldCompress</c>. That branch wrote gzip bytes with no
/// <c>Content-Encoding</c>, was set by nothing outside its tests, and left every other serializer
/// uncompressed. Compression is now one filter around the whole body, and
/// <see cref="AJsonSerializerWritesIdentityBytesWhateverTheClientAccepts"/> pins that these write
/// what they are given.
/// </remarks>
public class JsonResponseSerializerTests {

    private static IOptions<IJsonSerializerConfiguration> Config() =>
        Options.Create<IJsonSerializerConfiguration>(new JsonSerializerConfiguration {
            SerializeOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        });

    private static (IExecutionContext context, MemoryStream body) Context(
        object? responseValue, string? accept = "application/json", string? acceptEncoding = null) {
        var context = Substitute.For<IExecutionContext>();
        var request = Substitute.For<IExecutionRequest>();
        var response = Substitute.For<IExecutionResponse>();
        var body = new MemoryStream();
        var headers = new Dictionary<string, StringValues>();

        if (acceptEncoding != null) {
            headers[KnownHeaders.AcceptEncoding] = acceptEncoding;
        }

        request.Accept.Returns(accept);
        request.Headers.Returns(headers);
        response.Body.Returns(body);
        response.ResponseValue.Returns(responseValue);
        context.Request.Returns(request);
        context.Response.Returns(response);

        return (context, body);
    }

    private static IEnumerable<IResponseSerializer> Serializers() {
        yield return new SystemTextJsonResponseSerializer(
            Config(), Array.Empty<IJsonTypeInfoResolver>());
        yield return new AotResponseSerializer(
            Config(), new IJsonTypeInfoResolver[] { PayloadContext.Default });
    }

    public static TheoryData<string> SerializerNames => new() {
        nameof(SystemTextJsonResponseSerializer),
        nameof(AotResponseSerializer)
    };

    private static IResponseSerializer SerializerNamed(string name) =>
        Serializers().First(s => s.GetType().Name == name);

    [Theory]
    [MemberData(nameof(SerializerNames))]
    public async Task AResponseIsWrittenAsPlainJson(string serializerName) {
        var (context, body) = Context(new Payload("hello", 42));

        await SerializerNamed(serializerName).SerializeResponse(context);

        var json = System.Text.Encoding.UTF8.GetString(body.ToArray());
        var payload = JsonSerializer.Deserialize<Payload>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal("hello", payload!.Name);
        Assert.Equal(42, payload.Value);
    }

    /// <summary>
    /// A serializer writes identity bytes. What the client accepts is the compression filter's
    /// business, one stage further out, and a serializer that compressed on its own would put a
    /// second encoder inside the filter's.
    /// </summary>
    [Theory]
    [MemberData(nameof(SerializerNames))]
    public async Task AJsonSerializerWritesIdentityBytesWhateverTheClientAccepts(string serializerName) {
        var (context, body) = Context(new Payload("plain", 7), acceptEncoding: "gzip, deflate, br");

        await SerializerNamed(serializerName).SerializeResponse(context);

        var written = body.ToArray();

        Assert.Equal((byte)'{', written[0]);
        Assert.Contains("\"plain\"", System.Text.Encoding.UTF8.GetString(written));
    }

    [Theory]
    [MemberData(nameof(SerializerNames))]
    public async Task NullResponseValueWritesNothing(string serializerName) {
        var (context, body) = Context(responseValue: null);

        await SerializerNamed(serializerName).SerializeResponse(context);

        Assert.Empty(body.ToArray());
    }

    [Theory]
    [MemberData(nameof(SerializerNames))]
    public async Task ContentTypeIsSetToApplicationJson(string serializerName) {
        var (context, _) = Context(new Payload("x", 1));

        await SerializerNamed(serializerName).SerializeResponse(context);

        context.Response.Received().ContentType = "application/json";
    }

    /// <summary>
    /// A JSON serializer produces JSON, and says so about the media type it is asked about rather
    /// than by reading the request.
    /// </summary>
    /// <remarks>
    /// The wildcard cases are the ones that used to be wrong. This asked
    /// <c>Request.Accept?.Contains("application/json")</c>, which is false for <c>*/*</c> and for a
    /// request with no Accept header - so the serializer declined the two most common request shapes
    /// there are, and they were served only because it is also the default. The fallback meant for
    /// genuine mismatches was carrying ordinary traffic.
    /// </remarks>
    [Theory]
    [MemberData(nameof(SerializerNames))]
    public void CanProduceAnswersForTheMediaTypeItIsAskedAbout(string serializerName) {
        var serializer = SerializerNamed(serializerName);
        var (context, _) = Context(new Payload("x", 1), accept: "application/json");

        Assert.True(serializer.CanProduce("application/json", context));
        Assert.True(serializer.CanProduce("*/*", context));
        Assert.True(serializer.CanProduce("application/*", context));
        Assert.False(serializer.CanProduce("text/html", context));
        Assert.False(serializer.CanProduce("text/*", context));
    }

    [Theory]
    [MemberData(nameof(SerializerNames))]
    public void BothAreRegisteredAsDefaultSerializers(string serializerName) {
        Assert.True(SerializerNamed(serializerName).IsDefaultSerializer);
    }
}

internal record Payload(string Name, int Value);

/// <summary>
/// Metadata for <see cref="Payload"/>, source generated.
/// </summary>
/// <remarks>
/// The AOT serializer used to be handed no resolver at all, and worked: the reflection overload it
/// called installs a default reflection resolver when the options carry none. So the test covering
/// the AOT path was exercising reflection, and would have gone on passing however AOT-hostile the
/// serializer became. It now takes real source-generated metadata, which is what an AOT application
/// supplies and what the serializer finds at run time.
///
/// At namespace scope because System.Text.Json's generator does not emit for a context nested
/// inside a type that is not itself partial.
/// </remarks>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(Payload))]
internal partial class PayloadContext : JsonSerializerContext { }
