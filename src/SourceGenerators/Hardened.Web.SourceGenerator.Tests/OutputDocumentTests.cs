using System.Text.Json;
using Hardened.Requests.Abstract.Attributes;
using Hardened.Requests.Abstract.Templates;
using Hardened.Requests.Runtime.Authorization;
using Hardened.SourceGeneration.Testing;
using Hardened.Web.Runtime.Attributes;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Hardened.Web.SourceGenerator.Tests;

/// <summary>
/// What an operation carrying <c>[Output&lt;T&gt;]</c> publishes.
///
/// <para>
/// It published <c>application/json</c> and a schema of the model, which is the one representation
/// such a route does not answer. Refitter reads the operation and pins
/// <c>[Headers("Accept: application/json")]</c>, so a generated client's every call to a view route
/// was refused - and the model under that key was wrong twice over, because a view renders a subset
/// of what its model holds and the markup is what goes on the wire.
/// </para>
/// </summary>
public class OutputDocumentTests
{
    private static readonly Type[] Anchors =
    [
        typeof(GetAttribute), // Hardened.Web.Runtime
        typeof(FromBodyAttribute), // Hardened.Requests.Abstract
        typeof(TemplateBaseAttribute), // Hardened.Requests.Abstract
        typeof(AuthorizeGrantsAttribute), // Hardened.Requests.Runtime
    ];

    /// <summary>
    /// Views and an engine, hand-written so the test owns both ends. The same shape
    /// <c>OutputFactoryGeneratorTests</c> uses: what a real view adds is RazorBlade's rendering,
    /// which is not what a document is written from.
    /// </summary>
    private const string Views = """
        using System.Threading.Tasks;
        using Hardened.Requests.Abstract.Execution;
        using Hardened.Requests.Abstract.Outputs;
        using Hardened.Requests.Abstract.Templates;

        namespace TestApp.Views;

        public record FleetSummary(string Region, int Devices, string OperatorEmail);

        public abstract class ViewBase<TModel> : IHardenedResponseOutput<TModel> {
            public virtual string ContentType => "text/html";

            protected IExecutionContext Context { get; private set; } = default!;

            public Task WriteOutput(IExecutionContext context) {
                Context = context;

                return Task.CompletedTask;
            }
        }

        [TemplateBase(typeof(ViewBase<>))]
        [TemplateContentType("text/html; charset=utf-8")]
        public sealed class HtmlEngine { }

        [TemplateBase(typeof(ViewBase<>))]
        [TemplateContentType("text/calendar")]
        public sealed class CalendarEngine { }

        public class Dashboard : ViewBase<FleetSummary> { }
        """;

    private static JsonElement Document(string handlers, string enables = "")
    {
        var result = GeneratorTestHarness.Run(
            new Dictionary<string, string>
            {
                ["Views.cs"] = Views,
                ["Controller.cs"] = $$"""
                using System.Threading.Tasks;
                using Hardened.Requests.Abstract.Attributes;
                using Hardened.Requests.Runtime.Authorization;
                using Hardened.Shared.Runtime.Attributes;
                using Hardened.Web.Runtime.Attributes;
                using Hardened.Web.Runtime.Responses;
                using TestApp.Views;

                namespace TestApp;

                [HardenedModule]
                {{GeneratedOpenApiDocument.EnableAttribute}}
                {{enables}}
                public partial class TestApplication { }

                public class OpsController {
                {{handlers}}
                }
                """,
            },
            new IIncrementalGenerator[] { new WebLibrarySourceGenerator() },
            Anchors
        );

        var source = result
            .AssertNoErrors()
            .GeneratedSources.First(pair => pair.Key.Contains("OpenApiDocument"))
            .Value;

        return JsonDocument.Parse(GeneratedOpenApiDocument.Extract(source)).RootElement;
    }

    private static JsonElement Response(
        JsonElement document,
        string status,
        string path = "/ops"
    ) =>
        document
            .GetProperty("paths")
            .GetProperty(path)
            .GetProperty("get")
            .GetProperty("responses")
            .GetProperty(status);

    private static JsonElement Content(JsonElement response) => response.GetProperty("content");

    private static IEnumerable<string> MediaTypes(JsonElement response) =>
        Content(response).EnumerateObject().Select(property => property.Name);

    private const string Dashboard = """
            [Get("/ops")]
            [Output<TestApp.Views.Dashboard>]
            public Task<FleetSummary> Dashboard() => Task.FromResult(new FleetSummary("us", 1, "a@b.c"));
        """;

    /// <summary>
    /// The media type the output writes, rather than <c>application/json</c>.
    /// </summary>
    /// <remarks>
    /// Read from the engine's <c>[TemplateContentType]</c>, which is the same value the generated
    /// base writes onto the response - so the document and the wire come from one declaration
    /// rather than from two that can drift.
    /// </remarks>
    [Fact]
    public void AnOutputPublishesWhatItWrites()
    {
        var response = Response(Document(Dashboard, "[Enable<TestApp.Views.HtmlEngine>]"), "200");

        Assert.Equal(["text/html; charset=utf-8"], MediaTypes(response));
    }

    /// <summary>
    /// <c>text/html</c> where the application enabled no template engine at all, which is every
    /// hand-written output - the reference page is one.
    /// </summary>
    [Fact]
    public void AnOutputWithNoEngineEnabledPublishesHtml()
    {
        Assert.Equal(["text/html"], MediaTypes(Response(Document(Dashboard), "200")));
    }

    /// <summary>
    /// And <c>text/html</c> where two engines are enabled, because which one a given view was built
    /// on is the thing that cannot be seen from here. A handler in that position says so itself.
    /// </summary>
    [Fact]
    public void TwoEnginesFallBackToHtml()
    {
        var document = Document(
            Dashboard,
            "[Enable<TestApp.Views.HtmlEngine>][Enable<TestApp.Views.CalendarEngine>]"
        );

        Assert.Equal(["text/html"], MediaTypes(Response(document, "200")));
    }

    /// <summary>
    /// The body is a string. The model is not on the wire - a view renders a subset of it - so a
    /// <c>$ref</c> to it under <c>text/html</c> trades one wrong schema for another, and a client
    /// generated from that parses markup as the model.
    /// </summary>
    [Fact]
    public void TheBodyIsTheRenderedPageRatherThanTheModel()
    {
        var document = Document(Dashboard, "[Enable<TestApp.Views.HtmlEngine>]");
        var schema = Content(Response(document, "200"))
            .GetProperty("text/html; charset=utf-8")
            .GetProperty("schema");

        Assert.Equal("string", schema.GetProperty("type").GetString());
        Assert.False(schema.TryGetProperty("$ref", out _));
        Assert.DoesNotContain(
            "OperatorEmail",
            document.ToString(),
            StringComparison.OrdinalIgnoreCase
        );
    }

    /// <summary>
    /// An operation that stated its own media types is left alone, response body and all.
    /// </summary>
    /// <remarks>
    /// The rewrite fills in what nothing declared rather than correcting what something did. That
    /// is what keeps a described operation's contract authoritative - a contract stating what its
    /// <c>text/html</c> carries is what the service publishes - and it is how a handler in a
    /// two-engine application says which of them its view came from.
    /// </remarks>
    [Fact]
    public void ADeclaredMediaTypeIsLeftAlone()
    {
        var document = Document(
            """
                [Get("/ops")]
                [Produces("text/calendar")]
                [Output<TestApp.Views.Dashboard>]
                public Task<FleetSummary> Dashboard() => Task.FromResult(new FleetSummary("us", 1, "a@b.c"));
            """,
            "[Enable<TestApp.Views.HtmlEngine>]"
        );

        Assert.Equal(["text/calendar"], MediaTypes(Response(document, "200")));
        Assert.Contains(
            "FleetSummary",
            Content(Response(document, "200")).GetProperty("text/calendar").ToString()
        );
    }

    /// <summary>
    /// A refusal keeps JSON and keeps its own body, because a refusal never reaches the output: a
    /// handler that threw has no model to render, so <c>ContextSerializationService</c> sends it to
    /// the exception serializer first and its body is whatever the error-body policy writes.
    /// </summary>
    [Fact]
    public void ARefusalIsStillJsonAndStillTyped()
    {
        var document = Document(
            """
                [Get("/ops")]
                [AuthorizeGrants("ops:read")]
                [Output<TestApp.Views.Dashboard>]
                public Task<FleetSummary> Dashboard() => Task.FromResult(new FleetSummary("us", 1, "a@b.c"));
            """,
            "[Enable<TestApp.Views.HtmlEngine>]"
        );

        Assert.Equal(["text/html; charset=utf-8"], MediaTypes(Response(document, "200")));
        Assert.Equal(["application/json"], MediaTypes(Response(document, "403")));

        Assert.Equal(
            "string",
            Content(Response(document, "200"))
                .GetProperty("text/html; charset=utf-8")
                .GetProperty("schema")
                .GetProperty("type")
                .GetString()
        );
    }

    /// <summary>
    /// A handler with no output is untouched: JSON, and a schema of the model. The rewrite is keyed
    /// on the attribute rather than applied to the document as a whole.
    /// </summary>
    [Fact]
    public void AHandlerWithNoOutputIsUnchanged()
    {
        var document = Document(
            """
                [Get("/ops")]
                public Task<FleetSummary> Summary() => Task.FromResult(new FleetSummary("us", 1, "a@b.c"));
            """,
            "[Enable<TestApp.Views.HtmlEngine>]"
        );

        Assert.Equal(["application/json"], MediaTypes(Response(document, "200")));
        Assert.Contains(
            "FleetSummary",
            Content(Response(document, "200")).GetProperty("application/json").ToString()
        );
    }
}
