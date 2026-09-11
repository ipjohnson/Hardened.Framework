using Hardened.Requests.Abstract.Attributes;
using Hardened.SourceGeneration.Testing;
using Hardened.SourceGenerator.Web;
using Hardened.Web.Runtime.Attributes;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Hardened.Web.SourceGenerator.Tests.ContentTypes;

/// <summary>
/// What an operation says it produces, checked against what could produce it.
///
/// <para>
/// Two findings at two severities, and the difference is what the build can know. Bytes with no
/// declaration is a fault entirely inside the code that wrote it, because nothing downstream can
/// supply a media type for bytes. A model declared as something nothing here writes may still be
/// right, because the host registers the serializer - so it is a warning, and a library that
/// declares <c>text/csv</c> still compiles without its host.
/// </para>
/// </summary>
public class ContentTypeDiagnosticTests {

    private static readonly Type[] Anchors = [
        typeof(GetAttribute),      // Hardened.Web.Runtime
        typeof(ProducesAttribute)  // Hardened.Requests.Abstract
    ];

    private static GeneratorResult Generate(string handler) =>
        GeneratorTestHarness.Run(
            $$"""
            using System.Collections.Generic;
            using System.IO;
            using System.Threading.Tasks;
            using Hardened.Requests.Abstract.Attributes;
            using Hardened.Shared.Runtime.Attributes;
            using Hardened.Web.Runtime.Attributes;

            namespace TestApp;

            [HardenedModule]
            public partial class TestApplication { }

            public record Reading(string Name);

            public class ReportController {
            {{handler}}
            }
            """,
            new WebLibrarySourceGenerator(),
            Anchors);

    private static IEnumerable<Diagnostic> Reported(GeneratorResult result, string id) =>
        result.GeneratorDiagnostics.Where(diagnostic => diagnostic.Id == id);

    // ── bytes with nothing to say what they are ────────────────────────

    /// <summary>
    /// Returning <c>byte[]</c> or <c>Stream</c> is the handler saying it writes its own response,
    /// so no serializer is consulted and nothing else can name the media type.
    /// </summary>
    [Theory]
    [InlineData("public byte[] Report() => new byte[] { 1 };")]
    [InlineData("public Stream Report() => Stream.Null;")]
    [InlineData("public Task<byte[]> Report() => Task.FromResult(new byte[] { 1 });")]
    [InlineData("public Task<Stream> Report() => Task.FromResult(Stream.Null);")]
    public void BytesWithNoDeclarationIsHRDR011(string handler) {
        var diagnostic = Assert.Single(
            Reported(
                Generate($"""
                    [Get("/report")]
                    {handler}
                    """),
                ContentTypeDiagnostics.MissingDeclarationId));

        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("ReportController.Report", diagnostic.GetMessage());
    }

    [Fact]
    public void BytesThatDeclareAMediaTypeAreFine() {
        Assert.Empty(
            Reported(
                Generate("""
                    [Get("/report")]
                    [Produces("application/pdf")]
                    public byte[] Report() => new byte[] { 1 };
                    """),
                ContentTypeDiagnostics.MissingDeclarationId));
    }

    /// <summary>
    /// A <c>string</c> is not one of them. It has a JSON reading, which is what a handler declaring
    /// nothing answers with, so there is nothing to refuse.
    /// </summary>
    [Fact]
    public void AStringWithNoDeclarationIsFine() {
        Assert.Empty(
            Reported(
                Generate("""
                    [Get("/report")]
                    public string Report() => "a,b";
                    """),
                ContentTypeDiagnostics.MissingDeclarationId));
    }

    /// <summary>
    /// A model called something ending in Stream is not a stream. The return type is read through
    /// the semantic model rather than by its name.
    /// </summary>
    [Fact]
    public void AModelNamedLikeAStreamIsNotOne() {
        Assert.Empty(
            Reported(
                Generate("""
                    [Get("/report")]
                    public Reading Report() => new("a");
                    """),
                ContentTypeDiagnostics.MissingDeclarationId));
    }

    // ── a model declared as something nothing writes ───────────────────

    [Fact]
    public void AModelDeclaredAsSomethingNothingWritesIsHRDR012() {
        var diagnostic = Assert.Single(
            Reported(
                Generate("""
                    [Get("/report")]
                    [Produces("text/csv")]
                    public Reading Report() => new("a");
                    """),
                ContentTypeDiagnostics.NothingProducesId));

        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("text/csv", diagnostic.GetMessage());
    }

    /// <summary>
    /// A warning rather than an error is the whole point: a library declaring <c>text/csv</c> has
    /// no way to know its host will register a CSV serializer, and a library that cannot compile
    /// without its host is not a library.
    /// </summary>
    [Fact]
    public void ItIsNotAnErrorSoALibraryStillCompiles() {
        var result = Generate("""
            [Get("/report")]
            [Produces("text/csv")]
            public Reading Report() => new("a");
            """);

        Assert.Empty(
            result.GeneratorDiagnostics.Where(
                diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
    }

    /// <summary>
    /// A handler that writes its own bytes produces whatever it declares, so nothing is warned
    /// about however unusual the media type.
    /// </summary>
    [Theory]
    [InlineData("public string Report() => \"a,b\";")]
    [InlineData("public byte[] Report() => new byte[] { 1 };")]
    public void AHandlerThatWritesItsOwnBytesProducesAnythingItDeclares(string handler) {
        Assert.Empty(
            Reported(
                Generate($"""
                    [Get("/report")]
                    [Produces("text/csv")]
                    {handler}
                    """),
                ContentTypeDiagnostics.NothingProducesId));
    }

    /// <summary>JSON is always producible: the framework registers a serializer for it.</summary>
    [Fact]
    public void JsonIsAlwaysProducible() {
        Assert.Empty(
            Reported(
                Generate("""
                    [Get("/report")]
                    [Produces("application/json")]
                    public Reading Report() => new("a");
                    """),
                ContentTypeDiagnostics.NothingProducesId));
    }

    /// <summary>
    /// A streamed handler's media types are its framing's, and the streaming writer produces both.
    /// </summary>
    [Fact]
    public void AStreamedHandlerIsNotWarnedAbout() {
        Assert.Empty(
            Reported(
                Generate("""
                    [Get("/report")]
                    [ServerSentEvents]
                    public async IAsyncEnumerable<Reading> Report() {
                        yield return new("a");

                        await Task.CompletedTask;
                    }
                    """),
                ContentTypeDiagnostics.NothingProducesId));
    }
}
