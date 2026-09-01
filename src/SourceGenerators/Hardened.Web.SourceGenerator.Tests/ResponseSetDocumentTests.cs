using System.Text.Json;
using Hardened.Requests.Abstract.Responses;
using Hardened.SourceGeneration.Testing;
using Hardened.Web.Runtime.Attributes;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;
using Xunit;

namespace Hardened.Web.SourceGenerator.Tests;

/// <summary>
/// The <c>responses</c> object a code-first operation publishes.
///
/// <para>
/// This wrote a single hardcoded <c>"200"</c> for every operation until response sets existed, and
/// both halves of that were wrong. Any other status a handler could answer with was absent, so a
/// client generated from the document had no branch for a 404 the handler returns on every miss.
/// And the success status was not read from the model at all, so <c>[Post(SuccessStatus = 201)]</c>
/// published a contract promising 200.
/// </para>
///
/// <para>
/// Nothing covered the second one, which is why it survived: no code-first document test used
/// <c>SuccessStatus</c>. The first test below is that assertion.
/// </para>
/// </summary>
public class ResponseSetDocumentTests {

    private static readonly Type[] Anchors = [
        typeof(GetAttribute),
        typeof(Response<,>)
    ];

    private static JsonElement Document(string handlers) {
        var result = GeneratorTestHarness.Run(
            $$"""
            using System.Threading.Tasks;
            using Hardened.Requests.Abstract.Responses;
            using Hardened.Shared.Runtime.Attributes;
            using Hardened.Web.Runtime.Attributes;

            namespace TestApp;

            [HardenedModule]
            {{GeneratedOpenApiDocument.EnableAttribute}}
            public partial class TestApplication { }

            public record Todo(int Id, string Title);
            public record Archived(int Id);

            public class TodoController {
            {{handlers}}
            }
            """,
            new WebLibrarySourceGenerator(),
            Anchors);

        var source = result.GeneratedSources
            .First(pair => pair.Key.Contains("OpenApiDocument")).Value;

        return JsonDocument.Parse(GeneratedOpenApiDocument.Extract(source)).RootElement;
    }

    private static JsonElement Responses(JsonElement document, string path, string method) =>
        document.GetProperty("paths").GetProperty(path).GetProperty(method).GetProperty("responses");

    private static IEnumerable<string> Statuses(JsonElement responses) =>
        responses.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal);

    #region the success status

    /// <summary>
    /// The bug nothing covered. A handler declaring 201 published 200, and a create-then-poll
    /// client acts on the difference.
    /// </summary>
    [Fact]
    public void ADeclaredSuccessStatusReachesTheDocument() {
        var responses = Responses(Document("""
                [Post("/todos", SuccessStatus = 201)]
                public Todo Create() => new Todo(1, "t");
            """), "/todos", "post");

        Assert.Equal(new[] { "201" }, Statuses(responses));
        Assert.Equal("Created", responses.GetProperty("201").GetProperty("description").GetString());
    }

    /// <summary>
    /// A handler that declares nothing still publishes 200, which is every application that existed
    /// before this changed.
    /// </summary>
    [Fact]
    public void AnUndeclaredSuccessStatusIsStillTwoHundred() {
        var responses = Responses(Document("""
                [Get("/todos/{id}")]
                public Todo ById(string id) => new Todo(1, "t");
            """), "/todos/{id}", "get");

        Assert.Equal(new[] { "200" }, Statuses(responses));
        Assert.Equal("OK", responses.GetProperty("200").GetProperty("description").GetString());
        Assert.True(responses.GetProperty("200").TryGetProperty("content", out _));
    }

    #endregion

    #region the whole response set

    /// <summary>
    /// The point of C.5: every status the handler can answer with, not only the one it succeeds
    /// with.
    /// </summary>
    [Fact]
    public void EveryDeclaredCaseBecomesAResponse() {
        var responses = Responses(Document("""
                [Get("/todos/{id}")]
                public Response<Todo, NotFound, Conflict> ById(string id) => new Todo(1, "t");
            """), "/todos/{id}", "get");

        Assert.Equal(new[] { "200", "404", "409" }, Statuses(responses));
    }

    /// <summary>
    /// Each carries the status's registered name, because a description is read by whoever reads
    /// the document and that is the one thing they already know the status by.
    /// </summary>
    [Fact]
    public void EachResponseCarriesItsReasonPhrase() {
        var responses = Responses(Document("""
                [Get("/todos/{id}")]
                public Response<Todo, NotFound, Gone> ById(string id) => new Todo(1, "t");
            """), "/todos/{id}", "get");

        Assert.Equal("OK", responses.GetProperty("200").GetProperty("description").GetString());
        Assert.Equal("Not Found", responses.GetProperty("404").GetProperty("description").GetString());
        Assert.Equal("Gone", responses.GetProperty("410").GetProperty("description").GetString());
    }

    /// <summary>
    /// The success case takes the endpoint's status, so the whole set moves with it rather than the
    /// annotated cases moving and the success case staying at 200.
    /// </summary>
    [Fact]
    public void TheSuccessCaseOfASetTakesTheEndpointStatus() {
        var responses = Responses(Document("""
                [Post("/todos", SuccessStatus = 201)]
                public Response<Todo, Conflict> Create() => new Todo(1, "t");
            """), "/todos", "post");

        Assert.Equal(new[] { "201", "409" }, Statuses(responses));
    }

    /// <summary>
    /// A 204 declares no content. A document that says otherwise makes a generated client wait for
    /// a body that is not coming.
    /// </summary>
    [Fact]
    public void ABodylessCaseDeclaresNoContent() {
        var responses = Responses(Document("""
                [Delete("/todos/{id}")]
                public Response<NoContent, NotFound> Remove(string id) => new NoContent();
            """), "/todos/{id}", "delete");

        Assert.Equal(new[] { "204", "404" }, Statuses(responses));
        Assert.False(responses.GetProperty("204").TryGetProperty("content", out _));
        Assert.True(responses.GetProperty("404").TryGetProperty("content", out _));
    }

    /// <summary>
    /// Two shapes under one status is a real declaration rather than a mistake, so the last one must
    /// not silently win.
    /// </summary>
    [Fact]
    public void TwoCasesSharingAStatusBecomeAOneOf() {
        var responses = Responses(Document("""
                [Get("/todos/{id}")]
                public Response<Todo, Archived, NotFound> ById(string id) => new Todo(1, "t");
            """), "/todos/{id}", "get");

        Assert.Equal(new[] { "200", "404" }, Statuses(responses));

        var schema = responses.GetProperty("200").GetProperty("content")
            .GetProperty("application/json").GetProperty("schema");

        Assert.True(schema.TryGetProperty("oneOf", out var oneOf));
        Assert.Equal(2, oneOf.GetArrayLength());
    }

    /// <summary>
    /// The wrapper must never appear. Writing its schema would offer a component whose only member
    /// is an untyped <c>Value</c> as the contract for the 200.
    /// </summary>
    [Fact]
    public void TheResponseWrapperIsNeverASchemaComponent() {
        var document = Document("""
                [Get("/todos/{id}")]
                public Response<Todo, NotFound> ById(string id) => new Todo(1, "t");
            """);

        if (document.TryGetProperty("components", out var components)) {
            foreach (var schema in components.GetProperty("schemas").EnumerateObject()) {
                Assert.DoesNotContain("Response", schema.Name, StringComparison.Ordinal);
            }
        }
    }

    #endregion

    #region the statuses the framework itself answers

    /// <summary>
    /// A bound value that fails to parse never reaches the handler:
    /// <c>StringConverterService.Parse</c> answers 400 with the validation envelope. The gate used
    /// to require a generated validator, so a documented 400 depended on the operation happening
    /// to declare a constraint - binding an <c>int</c> was enough to answer one and the document
    /// said nothing.
    /// </summary>
    [Fact]
    public void ABindingRefusalPublishesTheFourHundred() {
        var responses = Responses(Document("""
                [Get("/todos/{id}")]
                public Todo ById(int id) => new Todo(id, "t");
            """), "/todos/{id}", "get");

        Assert.Equal(new[] { "200", "400" }, Statuses(responses));

        Assert.Equal(
            "#/components/schemas/RequestValidationError",
            responses.GetProperty("400").GetProperty("content").GetProperty("application/json")
                .GetProperty("schema").GetProperty("$ref").GetString());
    }

    /// <summary>A string binds as itself and cannot fail conversion, so nothing is added.</summary>
    [Fact]
    public void AStringBoundOperationPublishesNoFourHundred() {
        var responses = Responses(Document("""
                [Get("/todos/{title}")]
                public Todo ByTitle(string title) => new Todo(1, title);
            """), "/todos/{title}", "get");

        Assert.Equal(new[] { "200" }, Statuses(responses));
    }

    /// <summary>
    /// A value that violates a route constraint means the route did not match, and the answer is
    /// the router's bodyless 404. The constraint is stripped from the path template, so this entry
    /// is the one trace of it the document carries.
    /// </summary>
    [Fact]
    public void AConstrainedPathTokenPublishesTheFourOhFour() {
        var responses = Responses(Document("""
                [Get("/todos/{id:int}")]
                public Todo ById(int id) => new Todo(id, "t");
            """), "/todos/{id}", "get");

        Assert.Equal(new[] { "200", "400", "404" }, Statuses(responses));
        Assert.False(responses.GetProperty("404").TryGetProperty("content", out _));
    }

    /// <summary>An operation that declares its own 404 keeps its own description.</summary>
    [Fact]
    public void ADeclaredFourOhFourIsNotOverwritten() {
        var responses = Responses(Document("""
                [Get("/todos/{id:int}")]
                public Response<Todo, NotFound> ById(int id) => new Todo(id, "t");
            """), "/todos/{id}", "get");

        Assert.Equal(new[] { "200", "400", "404" }, Statuses(responses));
        Assert.True(responses.GetProperty("404").TryGetProperty("content", out _));
    }

    #endregion

    #region it is still a document

    /// <summary>
    /// Parsed by <c>Microsoft.OpenApi</c> rather than only by assertions on characters this
    /// generator wrote, for the reason <c>OpenApiRoundTripTests</c> gives: a misunderstanding shared
    /// between a writer and its own reader agrees with itself and passes.
    /// </summary>
    [Fact]
    public void AResponseSetDocumentParsesAsOpenApi() {
        var result = GeneratorTestHarness.Run(
            $$"""
            using Hardened.Requests.Abstract.Responses;
            using Hardened.Shared.Runtime.Attributes;
            using Hardened.Web.Runtime.Attributes;

            namespace TestApp;

            [HardenedModule]
            {{GeneratedOpenApiDocument.EnableAttribute}}
            public partial class TestApplication { }

            public record Todo(int Id, string Title);

            public class TodoController {
                [Get("/todos/{id}")]
                public Response<Todo, NotFound, Conflict, NoContent> ById(string id) =>
                    new Todo(1, "t");
            }
            """,
            new WebLibrarySourceGenerator(),
            Anchors);

        var json = GeneratedOpenApiDocument.Extract(
            result.GeneratedSources.First(pair => pair.Key.Contains("OpenApiDocument")).Value);

        var read = OpenApiDocument.Parse(json, "json");

        Assert.Empty(read.Diagnostic?.Errors ?? []);

        var operation = read.Document!.Paths["/todos/{id}"].Operations![HttpMethod.Get];

        Assert.Equal(
            new[] { "200", "204", "404", "409" },
            operation.Responses!.Keys.OrderBy(k => k, StringComparer.Ordinal));
    }

    #endregion
}
