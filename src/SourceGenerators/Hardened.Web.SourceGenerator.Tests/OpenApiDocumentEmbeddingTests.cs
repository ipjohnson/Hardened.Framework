using System.Text.Json;
using Xunit;
using Hardened.SourceGeneration.Testing;
using Hardened.Web.SourceGenerator.Tests.Routing;

namespace Hardened.Web.SourceGenerator.Tests;

/// <summary>
/// How the document is carried, as distinct from what it says.
/// </summary>
/// <remarks>
/// It is emitted gzipped into a <c>ReadOnlySpan&lt;byte&gt;</c> rather than as a string literal,
/// because a C# string literal lives in the assembly's <c>#US</c> heap as UTF-16 and so costs two
/// bytes per ASCII character. Measured on a 279,276 byte document: 562,688 bytes of assembly as a
/// string, 92,672 gzipped and base64'd, 37,376 as a span over a metadata blob.
/// </remarks>
public class OpenApiDocumentEmbeddingTests {

    private static string App(string extra) =>
        $$"""
        using Hardened.Shared.Runtime.Attributes;
        using Hardened.Web.Runtime.Attributes;

        namespace TestApp;

        [HardenedModule]
        {{extra}}
        public partial class TestApplication { }

        public class OrderController {
            [Get("/orders")]
            public string All() => "";
        }
        """;

    private static GeneratorResult Run(string extra) {
        var result = GeneratorTestHarness.Run(
            new Dictionary<string, string> { ["Test.cs"] = App(extra) },
            new[] { new WebLibrarySourceGenerator() },
            GeneratedRoutingTable.Anchors);

        result.AssertNoErrors();

        return result;
    }

    /// <summary>
    /// No marker, no document. An application that does not serve one does not carry one, which is
    /// the point of making it opt-in rather than only making the route opt-in.
    /// </summary>
    [Fact]
    public void NoDocumentIsEmittedWithoutTheMarker() {
        var result = Run("");

        Assert.DoesNotContain(
            result.GeneratedSources.Keys, key => key.Contains("OpenApiDocument"));
    }

    [Fact]
    public void TheDocumentIsEmittedWithTheMarker() {
        var result = Run(GeneratedOpenApiDocument.EnableAttribute);

        Assert.Contains(result.GeneratedSources.Keys, key => key.Contains("OpenApiDocument"));
    }

    /// <summary>
    /// Compressed bytes rather than a string literal, and they inflate back to the document.
    /// </summary>
    [Fact]
    public void TheDocumentIsCarriedAsGZippedBytes() {
        var source = Run(GeneratedOpenApiDocument.EnableAttribute)
            .SourceContaining("OpenApiDocument");

        Assert.Contains("ReadOnlySpan<byte>", source);
        Assert.Contains("new byte[]", source);

        using var document = JsonDocument.Parse(GeneratedOpenApiDocument.Extract(source));

        Assert.True(document.RootElement.TryGetProperty("openapi", out _));
        Assert.True(document.RootElement.GetProperty("paths").TryGetProperty("/orders", out _));
    }

    /// <summary>
    /// The same input compresses to the same bytes.
    /// </summary>
    /// <remarks>
    /// Incremental generation and reproducible builds both require it, and a compressor stamping a
    /// timestamp into its header would break both silently - the output would still be a valid
    /// document, it would just differ on every run. <c>GZipStream</c> writes MTIME as zero, which is
    /// what makes this hold; this is here so that remains true rather than remaining assumed.
    /// </remarks>
    [Fact]
    public void TheEmittedBytesAreTheSameOnEveryRun() {
        var first = Run(GeneratedOpenApiDocument.EnableAttribute)
            .SourceContaining("OpenApiDocument");

        var second = Run(GeneratedOpenApiDocument.EnableAttribute)
            .SourceContaining("OpenApiDocument");

        Assert.Equal(first, second);
    }

    /// <summary>
    /// And the registration comes with it, so no application writes an <c>AddSingleton</c> by hand.
    /// </summary>
    /// <remarks>
    /// It has to be emitted here because this is the only place both halves exist: the document is a
    /// member of the entry point's own generated partial, and it is <c>static readonly</c> rather
    /// than <c>const</c> - so it is not a compile-time constant and could never be carried as an
    /// attribute argument.
    /// </remarks>
    [Fact]
    public void TheProviderIsRegisteredWithTheDocument() {
        var routing = Run(GeneratedOpenApiDocument.EnableAttribute).SourceContaining("Routing");

        Assert.Contains("OpenApiDocumentProvider", routing);
        Assert.Contains("TestApplication.OpenApiDocument.GZip", routing);
        Assert.Contains("\"/openapi.json\"", routing);
    }

    [Fact]
    public void NoProviderIsRegisteredWithoutTheMarker() {
        Assert.DoesNotContain("OpenApiDocumentProvider", Run("").SourceContaining("Routing"));
    }

    /// <summary>
    /// A marker of the application's own, serving the document somewhere else. The generator reads
    /// the facet rather than the marker's name, so this needs no change to it.
    /// </summary>
    [Fact]
    public void AnApplicationsOwnMarkerChoosesThePath() {
        const string source =
            """
            using Hardened.Shared.Runtime.Attributes;
            using Hardened.Web.Runtime.Attributes;
            using Hardened.Web.Runtime.OpenApi;

            namespace TestApp;

            [OpenApiDocumentPath("/spec.json")]
            public sealed class SpecEndpoint { }

            [HardenedModule]
            [Enable<SpecEndpoint>]
            public partial class TestApplication { }

            public class OrderController {
                [Get("/orders")]
                public string All() => "";
            }
            """;

        var result = GeneratorTestHarness.Run(
            new Dictionary<string, string> { ["Test.cs"] = source },
            new[] { new WebLibrarySourceGenerator() },
            GeneratedRoutingTable.Anchors);

        result.AssertNoErrors();

        var routing = result.SourceContaining("Routing");

        Assert.Contains("\"/spec.json\"", routing);
        Assert.Contains("OpenApiDocumentProvider", routing);
        Assert.Contains(result.GeneratedSources.Keys, key => key.Contains("OpenApiDocument"));
    }
}
