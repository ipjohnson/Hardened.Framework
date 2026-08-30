using System.Text.Json;
using CSharpAuthor;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.OpenApiDocument;
using Hardened.SourceGenerator.Requests;
using Hardened.SourceGenerator.Shared;
using Xunit;

namespace Hardened.OpenApi.SourceGenerator.Tests;

/// <summary>
/// The code-first document writer, through <c>Hardened.Idl.SourceGenerator</c>'s copy of it.
/// </summary>
/// <remarks>
/// <para>
/// <c>OpenApiDocumentGenerator.Write</c> is a pure function over models - no symbols, no
/// compilation - so it can be called directly, which is what reaches the copy this generator ships.
/// The web generator's copy is covered by tests that run a whole generator and read the document
/// back out of the emitted constant; those say nothing about this compilation.
/// </para>
/// <para>
/// Asserted by parsing the JSON rather than by matching text, so a test fails on the document being
/// wrong rather than on the writer having put a comma somewhere new.
/// </para>
/// </remarks>
public class OpenApiDocumentSourceTests {

    private static ITypeDefinition Type(string name) => TypeDefinition.Get("TestApp", name);

    private static EntryPointSelector.Model EntryPoint() =>
        new() {
            EntryPointType = Type("Application"),
            AttributeModels = System.Array.Empty<AttributeModel>()
        };

    private static HandlerSchema Schema(string name) =>
        new($"{{\"$ref\":\"#/components/schemas/{name}\"}}",
            new[] { new SchemaComponent(name, "{\"type\":\"object\"}") });

    private static RequestHandlerModel Handler(
        string path = "/todos/{id}",
        string method = "GET",
        int? successStatus = null,
        HandlerSchema? response = null,
        IReadOnlyList<ResponseSchemaModel>? responses = null) =>
        new(
            new RequestHandlerNameModel(path, method),
            Type("TodoController"),
            "GetTodo",
            TypeDefinition.Get("TestApp.Generated", "TodoController_GetTodo"),
            [],
            new ResponseInformationModel {
                ReturnType = Type("Todo"),
                DefaultStatusCode = successStatus
            },
            []) {
            ResponseSchema = response,
            ResponseSchemas = responses ?? System.Array.Empty<ResponseSchemaModel>()
        };

    private static JsonElement Document(params RequestHandlerModel[] handlers) =>
        JsonDocument.Parse(
            OpenApiDocumentGenerator.Write(EntryPoint(), handlers, "")).RootElement;

    private static JsonElement Responses(JsonElement document, string path, string verb) =>
        document.GetProperty("paths").GetProperty(path).GetProperty(verb).GetProperty("responses");

    #region the success status

    /// <summary>
    /// The defect nothing covered: DefaultStatusCode existed throughout and was never read, so a
    /// handler declaring 201 published a contract promising 200.
    /// </summary>
    [Fact]
    public void ADeclaredSuccessStatusReachesTheDocument() {
        var responses = Responses(
            Document(Handler(method: "POST", successStatus: 201, response: Schema("Todo"))),
            "/todos/{id}", "post");

        Assert.True(responses.TryGetProperty("201", out var created));
        Assert.Equal("Created", created.GetProperty("description").GetString());
        Assert.False(responses.TryGetProperty("200", out _));
    }

    [Fact]
    public void AnUndeclaredSuccessStatusIsStillTwoHundred() {
        var responses = Responses(
            Document(Handler(response: Schema("Todo"))), "/todos/{id}", "get");

        Assert.True(responses.TryGetProperty("200", out var ok));
        Assert.Equal("OK", ok.GetProperty("description").GetString());
        Assert.True(ok.TryGetProperty("content", out _));
    }

    /// <summary>
    /// A handler with no body still declares its status, so the operation is not left with an empty
    /// responses object.
    /// </summary>
    [Fact]
    public void AHandlerWithNoBodyStillDeclaresItsStatus() {
        var responses = Responses(Document(Handler(successStatus: 204)), "/todos/{id}", "get");

        Assert.True(responses.TryGetProperty("204", out var noContent));
        Assert.False(noContent.TryGetProperty("content", out _));
    }

    #endregion

    #region a declared response set

    private static ResponseSchemaModel Response(int status, string? schema = null) =>
        new(status, HttpResponseDescription.For(status), schema == null ? null : Schema(schema));

    [Fact]
    public void EveryDeclaredResponseBecomesAStatus() {
        var responses = Responses(
            Document(Handler(responses: [
                Response(200, "Todo"), Response(404, "NotFound"), Response(409, "Conflict")
            ])),
            "/todos/{id}", "get");

        Assert.Equal(
            ["200", "404", "409"],
            responses.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public void EachResponseCarriesItsDescription() {
        var responses = Responses(
            Document(Handler(responses: [Response(200, "Todo"), Response(410, "Gone")])),
            "/todos/{id}", "get");

        Assert.Equal("OK", responses.GetProperty("200").GetProperty("description").GetString());
        Assert.Equal("Gone", responses.GetProperty("410").GetProperty("description").GetString());
    }

    /// <summary>
    /// A 204 declares no content. A document saying otherwise makes a generated client wait for a
    /// body that is not coming.
    /// </summary>
    [Fact]
    public void ABodylessResponseDeclaresNoContent() {
        var responses = Responses(
            Document(Handler(responses: [Response(204), Response(404, "NotFound")])),
            "/todos/{id}", "get");

        Assert.False(responses.GetProperty("204").TryGetProperty("content", out _));
        Assert.True(responses.GetProperty("404").TryGetProperty("content", out _));
    }

    /// <summary>
    /// Two shapes under one status is a real declaration rather than a mistake, so the last one must
    /// not silently win.
    /// </summary>
    [Fact]
    public void TwoResponsesSharingAStatusBecomeAOneOf() {
        var responses = Responses(
            Document(Handler(responses: [
                Response(200, "Todo"), Response(200, "Archived"), Response(404, "NotFound")
            ])),
            "/todos/{id}", "get");

        var schema = responses.GetProperty("200").GetProperty("content")
            .GetProperty("application/json").GetProperty("schema");

        Assert.True(schema.TryGetProperty("oneOf", out var oneOf));
        Assert.Equal(2, oneOf.GetArrayLength());
    }

    /// <summary>
    /// Written in status order, ordinally, because a document is diffed against the last one as
    /// often as it is read - and responses that moved for no reason are a diff a reviewer has to
    /// work out is empty.
    /// </summary>
    [Fact]
    public void ResponsesAreWrittenInStatusOrder() {
        var json = OpenApiDocumentGenerator.Write(
            EntryPoint(),
            [Handler(responses: [Response(503), Response(200, "Todo"), Response(404, "NotFound")])],
            "");

        Assert.True(
            json.IndexOf("\"200\"", StringComparison.Ordinal) <
            json.IndexOf("\"404\"", StringComparison.Ordinal));

        Assert.True(
            json.IndexOf("\"404\"", StringComparison.Ordinal) <
            json.IndexOf("\"503\"", StringComparison.Ordinal));
    }

    /// <summary>
    /// Every schema a response reaches lands in components, so a $ref in the document resolves.
    /// </summary>
    [Fact]
    public void EveryResponseSchemaReachesComponents() {
        var document = Document(Handler(responses: [
            Response(200, "Todo"), Response(404, "NotFound")
        ]));

        var schemas = document.GetProperty("components").GetProperty("schemas");

        Assert.True(schemas.TryGetProperty("Todo", out _));
        Assert.True(schemas.TryGetProperty("NotFound", out _));
    }

    #endregion

    #region the document itself

    [Fact]
    public void TheDocumentNamesItsVersionAndTitle() {
        var document = Document(Handler(response: Schema("Todo")));

        Assert.Equal("3.2.0", document.GetProperty("openapi").GetString());
        Assert.Equal("Application", document.GetProperty("info").GetProperty("title").GetString());
    }

    [Fact]
    public void OperationsAreGroupedUnderTheirPath() {
        var document = Document(
            Handler(path: "/todos", method: "GET", response: Schema("Todo")),
            Handler(path: "/todos", method: "POST", successStatus: 201, response: Schema("Todo")));

        var todos = document.GetProperty("paths").GetProperty("/todos");

        Assert.True(todos.TryGetProperty("get", out _));
        Assert.True(todos.TryGetProperty("post", out _));
    }

    /// <summary>
    /// A base path is composed onto every route, so the document describes where the application
    /// answers rather than where its handlers were declared.
    /// </summary>
    [Fact]
    public void ABasePathIsComposedOntoEveryRoute() {
        var json = OpenApiDocumentGenerator.Write(
            EntryPoint(), [Handler(path: "/todos", response: Schema("Todo"))], "/api");

        Assert.Contains("\"/api/todos\"", json);
    }

    #endregion

    #region control characters in prose

    /// <summary>
    /// A description with a second line still yields a document a strict parser accepts.
    /// </summary>
    /// <remarks>
    /// The defect this guards: multi-line contract prose reached <c>description</c> with its
    /// newlines raw, RFC 8259 forbids that, and <c>System.Text.Json</c>, <c>jq</c> and the served
    /// reference page all refused the published document. <see cref="Document"/> parses strictly,
    /// so reaching the assertions at all is most of the test.
    /// </remarks>
    [Fact]
    public void MultiLineDescriptionsSurviveStrictParsing() {
        var handler = Handler(
            responses: [
                new ResponseSchemaModel(
                    404,
                    "Which one is in the code member,\nbecause one status carries one body.",
                    Schema("Problem"))
            ]);

        handler.Description = "Line one.\nLine two.\r\n\tIndented, with a tab.";

        var document = Document(handler);
        var operation = document.GetProperty("paths").GetProperty("/todos/{id}").GetProperty("get");

        Assert.Equal(
            "Line one.\nLine two.\r\n\tIndented, with a tab.",
            operation.GetProperty("description").GetString());
        Assert.Equal(
            "Which one is in the code member,\nbecause one status carries one body.",
            operation.GetProperty("responses").GetProperty("404")
                .GetProperty("description").GetString());
    }

    /// <summary>Anything below U+0020 without a short escape goes out as <c>\u</c>.</summary>
    [Fact]
    public void BareControlCharactersAreUnicodeEscaped() {
        var handler = Handler(response: Schema("Todo"));

        handler.Description = "before\u0001after";

        var operation = Document(handler)
            .GetProperty("paths").GetProperty("/todos/{id}").GetProperty("get");

        Assert.Equal("before\u0001after", operation.GetProperty("description").GetString());
    }

    #endregion
}
