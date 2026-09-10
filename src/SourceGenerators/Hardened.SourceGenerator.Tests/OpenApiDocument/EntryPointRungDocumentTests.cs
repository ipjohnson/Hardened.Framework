using System.Text.Json;
using CSharpAuthor;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.OpenApiDocument;
using Hardened.SourceGenerator.Requests;
using Hardened.SourceGenerator.Shared;
using Xunit;

namespace Hardened.SourceGenerator.Tests.OpenApiDocument;

/// <summary>
/// What a filter declared on the entry point publishes, per operation.
/// </summary>
/// <remarks>
/// <para>
/// The facts are read once for the application and narrowed here, because whether a declaration
/// reaches an operation depends on that operation's verb and on whether it streams - the same
/// narrowing a declaration on a controller gets. Driven over hand-built facts rather than through a
/// generator, so the merge is exercised on shapes a real declaration reaches only in combination:
/// a status the handler already declares, a header it already binds, an operation whose responses
/// were complete.
/// </para>
/// <para>
/// The pipeline half of the same declaration is <c>ApplicationFilterEmitterTests</c>. A document
/// that publishes what nothing installs is the defect this rung closed, so neither half is a test
/// of the other.
/// </para>
/// </remarks>
public class EntryPointRungDocumentTests {

    private static ITypeDefinition Type(string name) => TypeDefinition.Get("TestApp", name);

    private static DeclaredScope Reads => new("GET,HEAD", notWhenStreaming: true);

    private static DeclaredOperationFacts NotModified(DeclaredScope? scope = null) {
        var reach = scope ?? Reads;

        return new DeclaredOperationFacts(
            [new ScopedRefusal(new ResponseSchemaModel(304, "Nothing is sent.", null), reach)],
            [
                new ScopedResponseHeader(304, "ETag", "The tag, repeated.", reach),
                new ScopedResponseHeader(200, "ETag", "A tag for this response.", reach)
            ],
            [new ScopedRequestHeader("If-None-Match", "A tag a previous response carried.", reach)]);
    }

    private static EntryPointSelector.Model App(DeclaredOperationFacts? facts) =>
        new() {
            EntryPointType = Type("Application"),
            AttributeModels = Array.Empty<AttributeModel>(),
            FilterFacts = facts
        };

    private static RequestHandlerModel Handler(
        string httpMethod = "GET",
        bool streams = false,
        IReadOnlyList<ResponseSchemaModel>? responses = null,
        RequestParameterInformation? parameter = null) =>
        new(new RequestHandlerNameModel("/books", httpMethod),
            Type("BookController"),
            "List",
            TypeDefinition.Get("TestApp.Generated", "BookController_List"),
            parameter == null ? [] : [parameter],
            new ResponseInformationModel { ReturnType = Type("Book"), IsAsyncEnumerable = streams },
            []) {
            ResponseSchemas = responses ?? Array.Empty<ResponseSchemaModel>()
        };

    private static JsonElement Operation(DeclaredOperationFacts? facts, RequestHandlerModel handler) =>
        JsonDocument
            .Parse(OpenApiDocumentGenerator.Write(App(facts), [handler], ""))
            .RootElement
            .GetProperty("paths").GetProperty("/books")
            .GetProperty(handler.Name.Method.ToLowerInvariant());

    private static string[] Statuses(JsonElement operation) =>
        operation.GetProperty("responses").EnumerateObject()
            .Select(response => response.Name)
            .OrderBy(status => status, StringComparer.Ordinal)
            .ToArray();

    private static string[] Parameters(JsonElement operation) =>
        operation.TryGetProperty("parameters", out var parameters)
            ? parameters.EnumerateArray().Select(p => p.GetProperty("name").GetString()!).ToArray()
            : [];

    /// <summary>
    /// The success survives. A refusal names what can be answered instead of the handler and says
    /// nothing about what the handler answers when it runs, so it cannot be the whole set.
    /// </summary>
    [Fact]
    public void ADeclaredRefusalJoinsTheOperationsOwnSuccess() {
        var operation = Operation(NotModified(), Handler());

        Assert.Equal(["200", "304"], Statuses(operation));
    }

    [Fact]
    public void TheHeadersTheDeclarationWritesLandOnTheStatusesItNames() {
        var operation = Operation(NotModified(), Handler());

        Assert.Equal(
            ["ETag"],
            operation.GetProperty("responses").GetProperty("304")
                .GetProperty("headers").EnumerateObject().Select(header => header.Name));

        Assert.Contains("If-None-Match", Parameters(operation));
    }

    /// <summary>
    /// And nothing reaches an operation the declaration stood down on, in either direction - a
    /// status published where the filter installs nothing is the lie the scoping exists to stop.
    /// </summary>
    [Theory]
    [InlineData("POST", false)]
    [InlineData("GET", true)]
    public void AnOperationTheDeclarationDoesNotReachPublishesNothingFromIt(
        string httpMethod, bool streams) {
        var operation = Operation(NotModified(), Handler(httpMethod, streams));

        Assert.DoesNotContain("304", Statuses(operation));
        Assert.Empty(Parameters(operation));
    }

    /// <summary>
    /// A status the operation declares itself keeps the shape the operation gave it. The rung's
    /// entries go last for that reason.
    /// </summary>
    [Fact]
    public void AStatusTheOperationAlreadyDeclaresKeepsItsOwnDescription() {
        var operation = Operation(
            NotModified(),
            Handler(responses: [new ResponseSchemaModel(304, "The handler's own wording.", null)]));

        Assert.Equal(
            "The handler's own wording.",
            operation.GetProperty("responses").GetProperty("304")
                .GetProperty("description").GetString());
    }

    /// <summary>
    /// A header the operation binds as a parameter wins: it carries a type and a description of its
    /// own, and two entries under one name is a document no generator can read.
    /// </summary>
    [Fact]
    public void AHeaderTheOperationBindsIsNotPublishedTwice() {
        var operation = Operation(
            NotModified(),
            Handler(parameter: new RequestParameterInformation(
                Type("String"), "ifNoneMatch", false, null,
                ParameterBindType.Header, "If-None-Match", 0)));

        Assert.Single(Parameters(operation), name => name == "If-None-Match");
    }

    /// <summary>
    /// A declaration that states no reach covers every operation, which is what any filter without
    /// a verb restriction means.
    /// </summary>
    [Fact]
    public void ADeclarationStatingNoReachCoversEveryOperation() {
        var facts = NotModified(new DeclaredScope(null, notWhenStreaming: false));

        Assert.Contains("304", Statuses(Operation(facts, Handler("POST"))));
        Assert.Contains("304", Statuses(Operation(facts, Handler(streams: true))));
    }

    /// <summary>
    /// An entry point declaring no filter writes the document it wrote before this existed.
    /// </summary>
    [Fact]
    public void AnEntryPointDeclaringNoFilterChangesNothing() {
        var handler = Handler();

        Assert.Equal(
            OpenApiDocumentGenerator.Write(App(null), [handler], ""),
            OpenApiDocumentGenerator.Write(
                App(DeclaredOperationFacts.Empty), [handler], ""));

        Assert.Equal(["200"], Statuses(Operation(null, handler)));
    }
}
