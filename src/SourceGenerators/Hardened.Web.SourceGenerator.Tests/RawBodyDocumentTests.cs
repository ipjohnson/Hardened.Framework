using System.Text.Json;
using Hardened.Requests.Abstract.Attributes;
using Hardened.SourceGeneration.Testing;
using Hardened.Web.Runtime.Attributes;
using Xunit;

namespace Hardened.Web.SourceGenerator.Tests;

/// <summary>
/// What an operation whose body is bytes publishes.
///
/// <para>
/// It published <c>application/json</c> with a schema of <c>format: binary</c> under it, which is
/// two statements that cannot both be true. Refitter turned that into a <c>StreamPart</c> sent with
/// <c>Content-Type: application/json</c>: a multipart part labelled JSON, for a route whose body is
/// a blob.
/// </para>
/// </summary>
public class RawBodyDocumentTests
{
    private static readonly Type[] Anchors = [typeof(GetAttribute), typeof(FromBodyAttribute)];

    private static JsonElement Document(string handlers)
    {
        var result = GeneratorTestHarness.Run(
            $$"""
            using System.IO;
            using System.Threading.Tasks;
            using Hardened.Requests.Abstract.Attributes;
            using Hardened.Shared.Runtime.Attributes;
            using Hardened.Web.Runtime.Attributes;

            namespace TestApp;

            [HardenedModule]
            {{GeneratedOpenApiDocument.EnableAttribute}}
            public partial class TestApplication { }

            public record Firmware(string Version);

            public class UploadController {
            {{handlers}}
            }
            """,
            new WebLibrarySourceGenerator(),
            Anchors
        );

        var source = result
            .AssertNoErrors()
            .GeneratedSources.First(pair => pair.Key.Contains("OpenApiDocument"))
            .Value;

        return JsonDocument.Parse(GeneratedOpenApiDocument.Extract(source)).RootElement;
    }

    private static JsonElement RequestBody(JsonElement document, string path) =>
        document
            .GetProperty("paths")
            .GetProperty(path)
            .GetProperty("put")
            .GetProperty("requestBody")
            .GetProperty("content");

    /// <summary>
    /// <c>application/octet-stream</c>, which is what the route reads. The media type was
    /// hardcoded to JSON for every code-first operation, so the one operation whose body cannot be
    /// JSON said it was.
    /// </summary>
    [Fact]
    public void AByteArrayBodyIsOctetStream()
    {
        var content = RequestBody(
            Document(
                """
                    [Put("/firmware")]
                    public Task<Firmware> Upload(byte[] image) => Task.FromResult(new Firmware("1"));
                """
            ),
            "/firmware"
        );

        var media = Assert.Single(content.EnumerateObject());

        Assert.Equal("application/octet-stream", media.Name);
        Assert.Equal("binary", media.Value.GetProperty("schema").GetProperty("format").GetString());
    }

    /// <summary>A <c>Stream</c> body says the same thing, because it is the same body.</summary>
    [Fact]
    public void AStreamBodyIsOctetStream()
    {
        var content = RequestBody(
            Document(
                """
                    [Put("/firmware")]
                    public Task<Firmware> Upload(Stream image) => Task.FromResult(new Firmware("1"));
                """
            ),
            "/firmware"
        );

        var media = Assert.Single(content.EnumerateObject());

        Assert.Equal("application/octet-stream", media.Name);
    }

    /// <summary>
    /// And a <c>Stream</c> is a binary payload rather than <c>System.IO.Stream</c> reflected onto
    /// the wire.
    /// </summary>
    /// <remarks>
    /// It published a component named <c>Stream</c> carrying <c>canRead</c>, <c>canSeek</c>,
    /// <c>length</c> and <c>position</c>, and Refitter generated a <c>Models.Stream</c> that does
    /// not compile beside <c>System.IO.Stream</c>.
    /// </remarks>
    [Fact]
    public void AStreamIsNotReflectedOntoTheWire()
    {
        var document = Document(
            """
                [Put("/firmware")]
                public Task<Firmware> Upload(Stream image) => Task.FromResult(new Firmware("1"));
            """
        );

        var schema = RequestBody(document, "/firmware")
            .GetProperty("application/octet-stream")
            .GetProperty("schema");

        Assert.Equal("string", schema.GetProperty("type").GetString());
        Assert.Equal("binary", schema.GetProperty("format").GetString());
        Assert.DoesNotContain("canSeek", document.ToString());
    }

    /// <summary>
    /// A model body is untouched. The media type follows the parameter rather than being asserted
    /// over the whole document.
    /// </summary>
    [Fact]
    public void AModelBodyIsStillJson()
    {
        var content = RequestBody(
            Document(
                """
                    [Put("/firmware")]
                    public Task<Firmware> Upload(Firmware body) => Task.FromResult(body);
                """
            ),
            "/firmware"
        );

        Assert.Equal("application/json", Assert.Single(content.EnumerateObject()).Name);
    }
}
