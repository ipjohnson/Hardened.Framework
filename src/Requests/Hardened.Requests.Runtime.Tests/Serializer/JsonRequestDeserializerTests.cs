using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.Serializer;
using Hardened.Requests.Runtime.Configuration;
using Hardened.Requests.Runtime.Serializer;
using Hardened.Requests.Runtime.Tests.Support;
using Hardened.Requests.Runtime.Validation;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Serializer;

/// <summary>
/// Both JSON request deserializers, held to the same table. They share <c>JsonRequestBody</c> and
/// differ in how they resolve the type's metadata.
/// </summary>
/// <remarks>
/// This file was <c>RequestDeserializerContentEncodingTests</c> while the two deserializers
/// unwrapped gzip and Brotli themselves, which left a form body, a Newtonsoft body and a raw body
/// unable to arrive compressed at all. That table moved unchanged to
/// <c>RequestDecompressionFilterTests</c>, and
/// <see cref="ACompressedBodyIsNotDecodedByTheDeserializer"/> pins that the branch is gone from
/// here rather than duplicated.
/// </remarks>
public class JsonRequestDeserializerTests
{
    private record Payload(string Name, int Value);

    private static IOptions<IJsonSerializerConfiguration> Config() =>
        Options.Create<IJsonSerializerConfiguration>(
            new JsonSerializerConfiguration
            {
                DeSerializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web),
            }
        );

    private static IRequestDeserializer DeserializerNamed(string name) =>
        name switch
        {
            nameof(SystemTextJsonRequestDeserializer) => new SystemTextJsonRequestDeserializer(
                Config(),
                Array.Empty<IJsonTypeInfoResolver>()
            ),
            nameof(AotRequestDeserializer) => new AotRequestDeserializer(
                Config(),
                NullLogger<AotRequestDeserializer>.Instance,
                new IJsonTypeInfoResolver[] { new DefaultJsonTypeInfoResolver() }
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(name), name, "unknown deserializer"),
        };

    public static TheoryData<string> DeserializerNames =>
        new() { nameof(SystemTextJsonRequestDeserializer), nameof(AotRequestDeserializer) };

    private const string Json = """{"name":"encoded","value":7}""";

    private static IExecutionContext Context(byte[] body, string? contentEncoding = null)
    {
        var context = Pipeline.Context(method: "POST", body: body);

        context.Request.Headers[KnownHeaders.ContentType] = new StringValues("application/json");

        if (contentEncoding is not null)
        {
            context.Request.Headers[KnownHeaders.ContentEncoding] = new StringValues(
                contentEncoding
            );
        }

        return context;
    }

    private static byte[] GZipped(string content)
    {
        var output = new MemoryStream();

        using (var gzip = new GZipStream(output, CompressionLevel.Fastest, true))
        {
            var bytes = Encoding.UTF8.GetBytes(content);

            gzip.Write(bytes, 0, bytes.Length);
        }

        return output.ToArray();
    }

    [Theory]
    [MemberData(nameof(DeserializerNames))]
    public async Task ABodyIsReadAsPlainJson(string deserializerName)
    {
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
    public async Task ACompressedBodyIsNotDecodedByTheDeserializer(string deserializerName)
    {
        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await DeserializerNamed(deserializerName)
                .DeserializeRequestBody<Payload>(Context(GZipped(Json), KnownEncoding.GZip))
        );
    }

    [Theory]
    [MemberData(nameof(DeserializerNames))]
    public void ADeserializerHandlesAJsonContentTypeAndNothingElse(string deserializerName)
    {
        var deserializer = DeserializerNamed(deserializerName);

        Assert.True(deserializer.CanProcessContext(Context(Encoding.UTF8.GetBytes(Json))));

        var formEncoded = Pipeline.Context(method: "POST");
        formEncoded.Request.Headers[KnownHeaders.ContentType] = new StringValues(
            "application/x-www-form-urlencoded"
        );

        Assert.False(deserializer.CanProcessContext(formEncoded));
        Assert.False(deserializer.CanProcessContext(Pipeline.Context(method: "POST")));
    }

    /// <summary>
    /// Both are default serializers, which is what lets a request with no usable
    /// <c>Content-Type</c> still be read as JSON rather than rejected.
    /// </summary>
    [Theory]
    [MemberData(nameof(DeserializerNames))]
    public void BothDeserializersOfferThemselvesAsTheDefault(string deserializerName)
    {
        Assert.True(DeserializerNamed(deserializerName).IsDefaultSerializer);
    }

    [JsonConverter(typeof(ColorConverter))]
    public enum Color
    {
        Red,
        Blue,
    }

    /// <summary>What the generated enum converters do with a value they do not declare.</summary>
    public sealed class ColorConverter : JsonConverter<Color>
    {
        public override Color Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options
        )
        {
            var value = reader.GetString();

            return value switch
            {
                "red" => Color.Red,
                "blue" => Color.Blue,
                _ => UndeclaredValue.Refuse<Color>(
                    "'" + value + "' is not a value Color declares."
                ),
            };
        }

        public override void Write(
            Utf8JsonWriter writer,
            Color value,
            JsonSerializerOptions options
        ) => writer.WriteStringValue(value == Color.Red ? "red" : "blue");
    }

    public sealed class Paint
    {
        public Color Color { get; set; }

        public int Coats { get; set; }

        public List<Color> Layers { get; set; } = new();
    }

    private static Task<Paint?> ReadPaint(string deserializerName, string json) =>
        DeserializerNamed(deserializerName)
            .DeserializeRequestBody<Paint>(Context(Encoding.UTF8.GetBytes(json)))
            .AsTask();

    /// <summary>
    /// Every undeclared value is named, at its own path, and the rest of the body is read so its
    /// constraints can be checked.
    /// </summary>
    [Theory]
    [MemberData(nameof(DeserializerNames))]
    public async Task EveryUndeclaredValueIsReportedWithTheBodyReadPastIt(string deserializerName)
    {
        var failure = await Assert.ThrowsAsync<BodyBindingException>(() =>
            ReadPaint(deserializerName, """{"color":"green","coats":2,"layers":["blue","pink"]}""")
        );

        Assert.Equal(
            new[]
            {
                ("body.color", "invalid", "'green' is not a value Color declares."),
                ("body.layers[1]", "invalid", "'pink' is not a value Color declares."),
            },
            failure.ValidationResult.Errors.Select(error =>
                (error.Field, error.Code, error.Message)
            )
        );

        var paint = Assert.IsType<Paint>(failure.Body);

        Assert.Equal(2, paint.Coats);
        Assert.Equal(new[] { Color.Blue, Color.Red }, paint.Layers);
        Assert.Equal("body", failure.Field);
    }

    /// <summary>
    /// A later value of the wrong type is reported beside the undeclared one. Nothing past it was
    /// read, so there is no body to check constraints on.
    /// </summary>
    [Theory]
    [MemberData(nameof(DeserializerNames))]
    public async Task AFailureAfterAnUndeclaredValueEndsTheRead(string deserializerName)
    {
        var failure = await Assert.ThrowsAsync<BodyBindingException>(() =>
            ReadPaint(deserializerName, """{"color":"green","coats":"many"}""")
        );

        Assert.Equal(
            new[] { ("body.color", "invalid"), ("body.coats", "invalid") },
            failure.ValidationResult.Errors.Select(error => (error.Field, error.Code))
        );
        Assert.Equal(
            "The value is not an integer this field can hold.",
            failure.ValidationResult.Errors[1].Message
        );
        Assert.Null(failure.Body);
    }

    [Theory]
    [MemberData(nameof(DeserializerNames))]
    public async Task TheUndeclaredValuesReportedFromOneBodyAreBounded(string deserializerName)
    {
        var layers = string.Join(",", Enumerable.Repeat("\"pink\"", 40));

        var failure = await Assert.ThrowsAsync<BodyBindingException>(() =>
            ReadPaint(deserializerName, "{\"layers\":[" + layers + "]}")
        );

        Assert.Equal(16, failure.ValidationResult.Errors.Count);
        Assert.Null(failure.Body);
        Assert.Throws<UndeclaredValueException>(() => UndeclaredValue.Refuse<Color>("after"));
    }

    /// <summary>
    /// A body with no undeclared value fails as it did when it was streamed.
    /// </summary>
    [Theory]
    [MemberData(nameof(DeserializerNames))]
    public async Task ABodyWithNoUndeclaredValueThrowsTheReadersException(string deserializerName)
    {
        var failure = await Assert.ThrowsAsync<JsonException>(() =>
            ReadPaint(deserializerName, """{"coats":"many"}""")
        );

        Assert.Equal("$.coats", failure.Path);
    }

    /// <summary>
    /// The span reader refuses a byte order mark that the stream reader skipped.
    /// </summary>
    [Theory]
    [MemberData(nameof(DeserializerNames))]
    public async Task AByteOrderMarkIsSkipped(string deserializerName)
    {
        var body = new byte[] { 0xEF, 0xBB, 0xBF }
            .Concat(Encoding.UTF8.GetBytes(Json))
            .ToArray();

        var payload = await DeserializerNamed(deserializerName)
            .DeserializeRequestBody<Payload>(Context(body));

        Assert.Equal(7, payload!.Value);
    }

    /// <summary>
    /// A body longer than the first buffer is read whole.
    /// </summary>
    [Theory]
    [MemberData(nameof(DeserializerNames))]
    public async Task ABodyLongerThanTheFirstBufferIsReadWhole(string deserializerName)
    {
        var name = new string('n', 10_000);

        var payload = await DeserializerNamed(deserializerName)
            .DeserializeRequestBody<Payload>(
                Context(Encoding.UTF8.GetBytes("{\"name\":\"" + name + "\",\"value\":3}"))
            );

        Assert.Equal(name, payload!.Name);
        Assert.Equal(3, payload.Value);
    }
}
