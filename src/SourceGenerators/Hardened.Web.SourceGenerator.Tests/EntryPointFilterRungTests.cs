using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using Hardened.Requests.Abstract.Attributes;
using Hardened.Requests.Runtime.Authorization;
using Hardened.Shared.Runtime.Attributes;
using Hardened.SourceGeneration.Testing;
using Hardened.Web.Runtime.Attributes;
using Hardened.Web.Runtime.Conditional;
using Hardened.Web.Runtime.OpenApi;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Hardened.Web.SourceGenerator.Tests;

/// <summary>
/// A filter declared on the entry point, in the generated source and in the served document.
/// </summary>
/// <remarks>
/// <para>
/// The 0.32 trial's A-04: <c>[Enable&lt;ConditionalGet&gt;]</c> installs the filter on every GET
/// and publishes it on none, because what decides which handlers are covered is a predicate
/// evaluated at startup and the document is written at build time. A declaration on the module
/// class stays inside the compilation, so both halves read it.
/// </para>
/// <para>
/// Both halves are asserted here rather than only the document: the array the pipeline reads and
/// the statuses the document publishes come from one declaration, and a test that watched only one
/// of them would pass on exactly the disagreement this closes.
/// </para>
/// </remarks>
public class EntryPointFilterRungTests {

    private static readonly Type[] Anchors = [
        typeof(GetAttribute),                 // Hardened.Web.Runtime
        typeof(FromBodyAttribute),            // Hardened.Requests.Abstract
        typeof(AuthorizeGrantsAttribute),     // Hardened.Requests.Runtime
        typeof(EnableAttribute<>),            // Hardened.Shared.Runtime
        typeof(ConditionalGetAttribute),      // the declaration
        typeof(OpenApiDocumentPublishing)     // the marker
    ];

    private const string Usings = """
        using System.Collections.Generic;
        using System.Threading.Tasks;
        using Hardened.Shared.Runtime.Attributes;
        using Hardened.Web.Runtime.Attributes;
        using Hardened.Web.Runtime.Conditional;
        using Hardened.Web.Runtime.OpenApi;

        """;

    private const string Controllers = """
        public class LibraryController {
            [Get("/books")]
            public string List() => "";

            [Get("/books/{id}")]
            public string Read(string id) => "";

            [Post("/books")]
            public string Add(string title) => "";
        }

        """;

    private static GeneratorResult Generate(string entryPointAttributes, string controllers) {
        var source = Usings + """
            namespace TestApp;

            [HardenedModule]
            [Enable<OpenApiDocumentPublishing>]
            """ + entryPointAttributes + """

            public partial class Application { }

            """ + controllers;

        return GeneratorTestHarness.Run(
            new Dictionary<string, string> { ["Test.cs"] = source },
            new IIncrementalGenerator[] { new WebLibrarySourceGenerator() },
            Anchors).AssertNoErrors();
    }

    private static JsonElement Document(GeneratorResult result) {
        var match = Regex.Match(
            result.SourceContaining("OpenApiDocument"),
            @"new byte\[\]\s*\{(.*?)\}\s*;", RegexOptions.Singleline);

        Assert.True(match.Success, "No document byte array in the generated source.");

        var bytes = match.Groups[1].Value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(byte.Parse)
            .ToArray();

        using var source = new MemoryStream(bytes, writable: false);
        using var gzip = new GZipStream(source, CompressionMode.Decompress);
        using var inflated = new MemoryStream();

        gzip.CopyTo(inflated);

        return JsonDocument.Parse(inflated.ToArray()).RootElement.Clone();
    }

    private static JsonElement Operation(JsonElement document, string path, string method) =>
        document.GetProperty("paths").GetProperty(path).GetProperty(method);

    private static string[] Statuses(JsonElement document, string path, string method = "get") =>
        Operation(document, path, method).GetProperty("responses").EnumerateObject()
            .Select(response => response.Name)
            .OrderBy(status => status, StringComparer.Ordinal)
            .ToArray();

    private static string[] Headers(JsonElement document, string path, string status) =>
        Operation(document, path, "get").GetProperty("responses").GetProperty(status)
            .TryGetProperty("headers", out var headers)
            ? headers.EnumerateObject().Select(header => header.Name).ToArray()
            : [];

    private static string[] Parameters(JsonElement document, string path, string method = "get") =>
        Operation(document, path, method).TryGetProperty("parameters", out var parameters)
            ? parameters.EnumerateArray().Select(p => p.GetProperty("name").GetString()!).ToArray()
            : [];

    /// <summary>
    /// The declaration reaches the pipeline as an array beside the routing table, registered for
    /// <c>ExecutionHelper</c> to merge into each handler as its chain is built.
    /// </summary>
    [Fact]
    public void TheDeclarationIsEmittedOnceAndRegistered() {
        var routing = Generate("[ConditionalGet]", Controllers).SourceContaining("Routing");

        Assert.Contains("class ApplicationFilters", routing);
        Assert.Contains("new ConditionalGetAttribute()", routing);
        Assert.Contains("IApplicationFilterDeclarations", routing);

        // Once for the application, rather than once per handler - which is the reason it is here
        // and not folded into each handler's own metadata array.
        Assert.Single(Regex.Matches(routing, @"new ConditionalGetAttribute\(\)"));
    }

    /// <summary>
    /// An entry point declaring no filter generates what it generated before this existed.
    /// </summary>
    [Fact]
    public void AnEntryPointDeclaringNoFilterEmitsNothing() {
        var routing = Generate("", Controllers).SourceContaining("Routing");

        Assert.DoesNotContain("ApplicationFilters", routing);
        Assert.DoesNotContain("IApplicationFilterDeclarations", routing);
    }

    /// <summary>
    /// A-04 itself: every read publishes the 304 the filter answers, and the write publishes
    /// nothing, because the declaration says it reaches GET and HEAD.
    /// </summary>
    [Fact]
    public void EveryReadPublishesTheStatusTheDeclarationAnswers() {
        var document = Document(Generate("[ConditionalGet]", Controllers));

        Assert.Equal(["200", "304"], Statuses(document, "/books"));
        Assert.Equal(["200", "304"], Statuses(document, "/books/{id}"));
        Assert.Equal(["200"], Statuses(document, "/books", "post"));
    }

    /// <summary>
    /// And the rest of what a caller needs to make the request: the tag on both statuses, and the
    /// header to send it back in.
    /// </summary>
    [Fact]
    public void EveryReadPublishesTheHeadersTheDeclarationReadsAndWrites() {
        var document = Document(Generate("[ConditionalGet]", Controllers));

        Assert.Equal(["ETag"], Headers(document, "/books", "200"));
        Assert.Equal(["ETag"], Headers(document, "/books", "304"));
        Assert.Contains("If-None-Match", Parameters(document, "/books"));
        Assert.Contains("If-Modified-Since", Parameters(document, "/books"));

        Assert.Empty(Parameters(document, "/books", "post"));
    }

    /// <summary>
    /// A handler that streams is left alone by both halves. The filter stands down because tagging
    /// a response means holding it back to hash it, and the document says so because the
    /// declaration states <c>NotWhenStreaming</c>.
    /// </summary>
    [Fact]
    public void AStreamedReadPublishesNothingFromTheRung() {
        var document = Document(Generate("[ConditionalGet]", """
            public class FeedController {
                [Get("/feed")]
                public async IAsyncEnumerable<string> Live() { yield return ""; await Task.CompletedTask; }
            }
            """));

        Assert.Equal(["200"], Statuses(document, "/feed"));
    }

    /// <summary>
    /// A handler declaring it itself is published once rather than twice, which is the document's
    /// half of nearest-rung-wins.
    /// </summary>
    [Fact]
    public void AHandlerDeclaringItItselfPublishesOneOfEach() {
        var document = Document(Generate("[ConditionalGet]", """
            public class RateController {
                [Get("/rates")]
                [ConditionalGet]
                public string Read() => "";
            }
            """));

        Assert.Equal(["200", "304"], Statuses(document, "/rates"));
        Assert.Equal(["ETag"], Headers(document, "/rates", "200"));
        Assert.Single(Parameters(document, "/rates"), name => name == "If-None-Match");
    }
}
