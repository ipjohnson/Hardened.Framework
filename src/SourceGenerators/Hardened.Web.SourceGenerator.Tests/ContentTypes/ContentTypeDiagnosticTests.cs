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
public class ContentTypeDiagnosticTests
{
    private static readonly Type[] Anchors =
    [
        typeof(GetAttribute), // Hardened.Web.Runtime
        typeof(ProducesAttribute), // Hardened.Requests.Abstract
    ];

    private static GeneratorResult Generate(string handler, string assemblyAttributes = "") =>
        GeneratorTestHarness.Run(
            $$"""
            using System.Collections.Generic;
            using System.IO;
            using System.Threading.Tasks;
            using Hardened.Requests.Abstract.Attributes;
            using Hardened.Requests.Abstract.Responses;
            using Hardened.Shared.Runtime.Attributes;
            using Hardened.Web.Runtime.Attributes;
            using Hardened.Web.Runtime.Responses;

            {{assemblyAttributes}}

            namespace TestApp;

            [HardenedModule]
            public partial class TestApplication { }

            public record Reading(string Name);

            public class ReportController {
            {{handler}}
            }
            """,
            new WebLibrarySourceGenerator(),
            Anchors
        );

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
    public void BytesWithNoDeclarationIsHRDR011(string handler)
    {
        var diagnostic = Assert.Single(
            Reported(
                Generate(
                    $"""
                    [Get("/report")]
                    {handler}
                    """
                ),
                ContentTypeDiagnostics.MissingDeclarationId
            )
        );

        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("ReportController.Report", diagnostic.GetMessage());
    }

    /// <summary>
    /// A response set answering with bytes is answering with bytes. Read off the return type this
    /// saw <c>Response&lt;byte[], NotFound&gt;</c> - a model - and said nothing, so the shape that
    /// needed the declaration most was the one that never got it: its bytes went out as base64
    /// under <c>application/json</c> with no diagnostic anywhere.
    /// </summary>
    [Theory]
    [InlineData("public Response<byte[], NotFound> Report() => new byte[] { 1 };")]
    [InlineData("public Response<Stream, NotFound> Report() => Stream.Null;")]
    [InlineData(
        "public Task<Response<byte[], NotFound>> Report() => Task.FromResult<Response<byte[], NotFound>>(new byte[] { 1 });"
    )]
    public void ASetAnsweringWithBytesIsHRDR011(string handler)
    {
        var diagnostic = Assert.Single(
            Reported(
                Generate(
                    $"""
                    [Get("/report")]
                    {handler}
                    """
                ),
                ContentTypeDiagnostics.MissingDeclarationId
            )
        );

        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("ReportController.Report", diagnostic.GetMessage());
    }

    /// <summary>
    /// And once it declares one, nothing complains that a model cannot be written as it. The
    /// declaration is about the success case, which is bytes. HRDR012 read the return type and saw
    /// the set, so the only correct spelling of this handler carried a warning saying it was wrong.
    /// </summary>
    [Fact]
    public void ASetAnsweringWithBytesIsNotHRDR012()
    {
        var result = Generate(
            """
            [Get("/report")]
            [Produces("application/octet-stream")]
            public Response<byte[], NotFound> Report() => new byte[] { 1 };
            """
        );

        Assert.Empty(Reported(result, ContentTypeDiagnostics.MissingDeclarationId));
        Assert.Empty(Reported(result, ContentTypeDiagnostics.NothingProducesId));
    }

    /// <summary>
    /// A set whose success case is a model is unchanged. The refusal case is a model either way,
    /// and it is never what the operation declares a media type for.
    /// </summary>
    [Fact]
    public void ASetAnsweringWithAModelIsUnchanged()
    {
        Assert.Empty(
            Reported(
                Generate(
                    """
                    [Get("/report")]
                    public Response<Reading, NotFound> Report() => new Reading("r");
                    """
                ),
                ContentTypeDiagnostics.MissingDeclarationId
            )
        );
    }

    [Fact]
    public void BytesThatDeclareAMediaTypeAreFine()
    {
        Assert.Empty(
            Reported(
                Generate(
                    """
                    [Get("/report")]
                    [Produces("application/pdf")]
                    public byte[] Report() => new byte[] { 1 };
                    """
                ),
                ContentTypeDiagnostics.MissingDeclarationId
            )
        );
    }

    /// <summary>
    /// A <c>string</c> is not one of them. It has a JSON reading, which is what a handler declaring
    /// nothing answers with, so there is nothing to refuse.
    /// </summary>
    [Fact]
    public void AStringWithNoDeclarationIsFine()
    {
        Assert.Empty(
            Reported(
                Generate(
                    """
                    [Get("/report")]
                    public string Report() => "a,b";
                    """
                ),
                ContentTypeDiagnostics.MissingDeclarationId
            )
        );
    }

    /// <summary>
    /// A model called something ending in Stream is not a stream. The return type is read through
    /// the semantic model rather than by its name.
    /// </summary>
    [Fact]
    public void AModelNamedLikeAStreamIsNotOne()
    {
        Assert.Empty(
            Reported(
                Generate(
                    """
                    [Get("/report")]
                    public Reading Report() => new("a");
                    """
                ),
                ContentTypeDiagnostics.MissingDeclarationId
            )
        );
    }

    // ── a model declared as something nothing writes ───────────────────

    [Fact]
    public void AModelDeclaredAsSomethingNothingWritesIsHRDR012()
    {
        var diagnostic = Assert.Single(
            Reported(
                Generate(
                    """
                    [Get("/report")]
                    [Produces("text/csv")]
                    public Reading Report() => new("a");
                    """
                ),
                ContentTypeDiagnostics.NothingProducesId
            )
        );

        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("text/csv", diagnostic.GetMessage());
    }

    /// <summary>
    /// A warning rather than an error is the whole point: a library declaring <c>text/csv</c> has
    /// no way to know its host will register a CSV serializer, and a library that cannot compile
    /// without its host is not a library.
    /// </summary>
    [Fact]
    public void ItIsNotAnErrorSoALibraryStillCompiles()
    {
        var result = Generate(
            """
            [Get("/report")]
            [Produces("text/csv")]
            public Reading Report() => new("a");
            """
        );

        Assert.Empty(
            result.GeneratorDiagnostics.Where(diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error
            )
        );
    }

    /// <summary>
    /// A handler that writes its own bytes produces whatever it declares, so nothing is warned
    /// about however unusual the media type.
    /// </summary>
    [Theory]
    [InlineData("public string Report() => \"a,b\";")]
    [InlineData("public byte[] Report() => new byte[] { 1 };")]
    public void AHandlerThatWritesItsOwnBytesProducesAnythingItDeclares(string handler)
    {
        Assert.Empty(
            Reported(
                Generate(
                    $"""
                    [Get("/report")]
                    [Produces("text/csv")]
                    {handler}
                    """
                ),
                ContentTypeDiagnostics.NothingProducesId
            )
        );
    }

    /// <summary>JSON is always producible: the framework registers a serializer for it.</summary>
    [Fact]
    public void JsonIsAlwaysProducible()
    {
        Assert.Empty(
            Reported(
                Generate(
                    """
                    [Get("/report")]
                    [Produces("application/json")]
                    public Reading Report() => new("a");
                    """
                ),
                ContentTypeDiagnostics.NothingProducesId
            )
        );
    }

    // ── a serializer in reach declares the media type ──────────────────

    /// <summary>
    /// The finding this was written to catch, and the one it got wrong: a compilation that has a
    /// serializer for the media type is not making a mistake.
    /// </summary>
    /// <remarks>
    /// Before <c>[assembly: WritesContentType]</c> every media type but <c>application/json</c> was
    /// treated as unproducible, so installing a serializer package did not silence this and an
    /// application adopting MessagePack could not build warning-free.
    /// </remarks>
    [Fact]
    public void ADeclaredMediaTypeASerializerWritesIsNotWarnedAbout()
    {
        Assert.Empty(
            Reported(
                Generate(
                    """
                    [Get("/report")]
                    [Produces("application/x-msgpack")]
                    public Reading Report() => new("a");
                    """,
                    """[assembly: WritesContentType("application/x-msgpack")]"""
                ),
                ContentTypeDiagnostics.NothingProducesId
            )
        );
    }

    /// <summary>
    /// One type of a declared set having a writer says nothing about the others, so the set is
    /// filtered rather than passed or failed whole.
    /// </summary>
    [Fact]
    public void OnlyTheTypesWithNoWriterAreReported()
    {
        var diagnostic = Assert.Single(
            Reported(
                Generate(
                    """
                    [Get("/report")]
                    [Produces("application/x-msgpack", "text/csv")]
                    public Reading Report() => new("a");
                    """,
                    """[assembly: WritesContentType("application/x-msgpack")]"""
                ),
                ContentTypeDiagnostics.NothingProducesId
            )
        );

        Assert.Contains("text/csv", diagnostic.GetMessage());
    }

    /// <summary>
    /// One attribute naming several, which is how a package that writes two spellings of one format
    /// declares them.
    /// </summary>
    [Fact]
    public void OneAttributeCanNameSeveralMediaTypes()
    {
        Assert.Empty(
            Reported(
                Generate(
                    """
                    [Get("/report")]
                    [Produces("application/msgpack")]
                    public Reading Report() => new("a");
                    """,
                    """[assembly: WritesContentType("application/x-msgpack", "application/msgpack")]"""
                ),
                ContentTypeDiagnostics.NothingProducesId
            )
        );
    }

    /// <summary>
    /// Exactly, because the runtime's lookup is exact: <c>SerializationLocatorService.ProducerOf</c>
    /// is a dictionary hit on the serializer's own tag, and it is what the compile-time binding
    /// uses. Matching a wildcard here would go quiet for a spelling the locator will not find.
    /// </summary>
    [Fact]
    public void AWildcardDeclarationDoesNotCoverAConcreteType()
    {
        Assert.Single(
            Reported(
                Generate(
                    """
                    [Get("/report")]
                    [Produces("application/x-msgpack")]
                    public Reading Report() => new("a");
                    """,
                    """[assembly: WritesContentType("application/*")]"""
                ),
                ContentTypeDiagnostics.NothingProducesId
            )
        );
    }

    /// <summary>
    /// A streamed handler's media types are its framing's, and the streaming writer produces both.
    /// </summary>
    [Fact]
    public void AStreamedHandlerIsNotWarnedAbout()
    {
        Assert.Empty(
            Reported(
                Generate(
                    """
                    [Get("/report")]
                    [ServerSentEvents]
                    public async IAsyncEnumerable<Reading> Report() {
                        yield return new("a");

                        await Task.CompletedTask;
                    }
                    """
                ),
                ContentTypeDiagnostics.NothingProducesId
            )
        );
    }
}
