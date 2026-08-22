using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Serializer;
using Hardened.Requests.Runtime.Configuration;
using Hardened.Requests.Runtime.Serializer;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Serializer;

/// <summary>
/// Both JSON response serializers share a compression branch. These tests cover it for each
/// of them, since the implementations are duplicated rather than shared.
/// </summary>
public class ResponseSerializerCompressionTests {


    private static IOptions<IJsonSerializerConfiguration> Config() =>
        Options.Create<IJsonSerializerConfiguration>(new JsonSerializerConfiguration {
            SerializeOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        });

    private static (IExecutionContext context, MemoryStream body) Context(
        object? responseValue, bool shouldCompress, string? accept = "application/json") {
        var context = Substitute.For<IExecutionContext>();
        var request = Substitute.For<IExecutionRequest>();
        var response = Substitute.For<IExecutionResponse>();
        var body = new MemoryStream();

        request.Accept.Returns(accept);
        response.Body.Returns(body);
        response.ResponseValue.Returns(responseValue);
        response.ShouldCompress.Returns(shouldCompress);
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
    public async Task UncompressedResponseIsPlainJson(string serializerName) {
        var (context, body) = Context(new Payload("hello", 42), shouldCompress: false);

        await SerializerNamed(serializerName).SerializeResponse(context);

        var json = System.Text.Encoding.UTF8.GetString(body.ToArray());
        var payload = JsonSerializer.Deserialize<Payload>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal("hello", payload!.Name);
        Assert.Equal(42, payload.Value);
    }

    [Theory]
    [MemberData(nameof(SerializerNames))]
    public async Task NullResponseValueWritesNothing(string serializerName) {
        var (context, body) = Context(responseValue: null, shouldCompress: false);

        await SerializerNamed(serializerName).SerializeResponse(context);

        Assert.Empty(body.ToArray());
    }

    [Theory]
    [MemberData(nameof(SerializerNames))]
    public async Task ContentTypeIsSetToApplicationJson(string serializerName) {
        var (context, _) = Context(new Payload("x", 1), shouldCompress: false);

        await SerializerNamed(serializerName).SerializeResponse(context);

        context.Response.Received().ContentType = "application/json";
    }

    /// <summary>
    /// When ShouldCompress is set the body must actually be gzip data. Writing plain JSON
    /// while a GZipStream is open over the same body produces output that is not
    /// decompressible, so this asserts the bytes round-trip through GZipStream.
    /// </summary>
    [Theory]
    [MemberData(nameof(SerializerNames))]
    public async Task CompressedResponseIsActuallyGzipped(string serializerName) {
        var (context, body) = Context(new Payload("compress me", 7), shouldCompress: true);

        await SerializerNamed(serializerName).SerializeResponse(context);

        var written = body.ToArray();
        Assert.NotEmpty(written);

        // gzip magic number - if the body is plain JSON this fails immediately with a
        // clearer signal than a decompression exception.
        Assert.True(written.Length >= 2 && written[0] == 0x1f && written[1] == 0x8b,
            $"{serializerName}: response body is not gzip data (first bytes: " +
            $"{string.Join(" ", written.Take(4).Select(b => b.ToString("x2")))})");

        using var input = new MemoryStream(written);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);

        var payload = JsonSerializer.Deserialize<Payload>(
            await reader.ReadToEndAsync(TestContext.Current.CancellationToken), new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal("compress me", payload!.Name);
        Assert.Equal(7, payload.Value);
    }

    [Theory]
    [MemberData(nameof(SerializerNames))]
    public async Task CompressionIsSkippedForNullResponseValue(string serializerName) {
        var (context, body) = Context(responseValue: null, shouldCompress: true);

        await SerializerNamed(serializerName).SerializeResponse(context);

        Assert.Empty(body.ToArray());
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
        var (context, _) = Context(new Payload("x", 1), false, accept: "application/json");

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
