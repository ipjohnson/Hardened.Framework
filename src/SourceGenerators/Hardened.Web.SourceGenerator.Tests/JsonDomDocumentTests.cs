using System.Text.Json;
using Hardened.Requests.Abstract.Attributes;
using Hardened.SourceGeneration.Testing;
using Hardened.Web.Runtime.Attributes;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;
using Microsoft.OpenApi.YamlReader;
using Xunit;
using Tree = Hardened.Generation.Document;

namespace Hardened.Web.SourceGenerator.Tests;

/// <summary>
/// What the <c>System.Text.Json</c> DOM types publish: free-form JSON, in the served document, in
/// its YAML export and at each <c>HardenedOpenApiOutputVersion</c>.
///
/// <para>
/// They were reflected like models. <c>JsonElement</c> published its indexer as a property named
/// <c>this[]</c>, <c>JsonObject</c> a required <c>count</c>, and <c>JsonNode</c> a required
/// <c>root</c>, while each of them holds whatever JSON arrived. Refitter refused the repeated
/// <c>this[]</c> key.
/// </para>
/// </summary>
public class JsonDomDocumentTests
{
    private static readonly Type[] Anchors = [typeof(GetAttribute), typeof(FromBodyAttribute)];

    private const string DomHandlers = """
            [Post("/dom/envelope")]
            public Task<Envelope> Envelope(Envelope body) => Task.FromResult(body);

            [Post("/dom/element")]
            public Task<JsonElement> Element([FromBody] JsonElement body) => Task.FromResult(body);

            [Post("/dom/node")]
            public Task<JsonNode> Node([FromBody] JsonNode body) => Task.FromResult(body);

            [Post("/dom/object")]
            public Task<JsonObject> Object([FromBody] JsonObject body) => Task.FromResult(body);

            [Post("/dom/array")]
            public Task<JsonArray> Array([FromBody] JsonArray body) => Task.FromResult(body);

            [Post("/dom/value")]
            public Task<JsonValue> Value([FromBody] JsonValue body) => Task.FromResult(body);
        """;

    private const string Models = """
        public record Envelope(
            JsonElement Element,
            JsonNode Node,
            JsonObject Object,
            JsonArray Array,
            JsonValue Value,
            JsonNode? MaybeNode,
            JsonObject? MaybeObject,
            JsonArray? MaybeArray
        );
        """;

    private const string Free = "{}";
    private const string FreeObject = "{\"type\":\"object\"}";
    private const string FreeArray = "{\"type\":\"array\",\"items\":{}}";

    private static readonly Dictionary<string, string> Expected = new()
    {
        ["element"] = Free,
        ["node"] = Free,
        ["object"] = FreeObject,
        ["array"] = FreeArray,
        ["value"] = Free,
    };

    private static readonly Lazy<string> DomDocument = new(() => Emit(DomHandlers, Models));

    private static string Emit(string handlers, string models, string shapes = "")
    {
        var sources = new Dictionary<string, string>
        {
            ["Application.cs"] = $$"""
                using System.Text.Json;
                using System.Text.Json.Nodes;
                using System.Threading.Tasks;
                using Hardened.Requests.Abstract.Attributes;
                using Hardened.Shared.Runtime.Attributes;
                using Hardened.Web.Runtime.Attributes;

                namespace TestApp;

                [HardenedModule]
                {{GeneratedOpenApiDocument.EnableAttribute}}
                public partial class TestApplication { }

                {{models}}

                public class DomController {
                {{handlers}}
                }
                """,
            ["Shapes.cs"] = "namespace TestApp.Shapes;\n\n" + shapes,
        };

        var result = GeneratorTestHarness.Run(sources, [new WebLibrarySourceGenerator()], Anchors);

        var source = result
            .AssertNoErrors()
            .GeneratedSources.First(pair => pair.Key.Contains("OpenApiDocument"))
            .Value;

        return GeneratedOpenApiDocument.Extract(source);
    }

    public static TheoryData<string, string> Outputs()
    {
        var data = new TheoryData<string, string>();

        foreach (var format in new[] { "json", "yaml" })
        {
            foreach (var version in new[] { "3.2.0", "3.1.0", "3.0.0" })
            {
                data.Add(format, version);
            }
        }

        return data;
    }

    /// <summary>
    /// The document the export writes, the way <c>WriteOpenApiDocument</c> writes it: lowered when
    /// the version is not the served one, then indented JSON or YAML.
    /// </summary>
    /// <remarks>
    /// A YAML export is read back by <c>Microsoft.OpenApi</c> and held to the same document as the
    /// JSON one, so the assertions on the JSON speak for both.
    /// </remarks>
    private static JsonElement Written(string compact, string format, string version)
    {
        var tree = (Tree.JsonObject)Tree.JsonTree.Parse(compact);

        if (version != "3.2.0")
        {
            Tree.OpenApiDocumentLowering.Lower(tree, version);
        }

        var json = Tree.JsonTreeWriter.WriteIndented(tree);

        if (format == "yaml")
        {
            Assert.Equal(
                Canonical(Read(json, "json")),
                Canonical(Read(Tree.YamlTreeWriter.Write(tree), "yaml"))
            );
        }

        var document = JsonDocument.Parse(json).RootElement;

        Assert.Equal(version, document.GetProperty("openapi").GetString());

        return document;
    }

    private static Microsoft.OpenApi.OpenApiDocument Read(string text, string format)
    {
        var settings = new OpenApiReaderSettings();
        settings.AddYamlReader();

        var read = Microsoft.OpenApi.OpenApiDocument.Parse(text, format, settings);

        Assert.NotNull(read.Document);
        Assert.Empty(read.Diagnostic!.Errors);

        return read.Document!;
    }

    private static string Canonical(Microsoft.OpenApi.OpenApiDocument document)
    {
        using var text = new StringWriter();

        document.SerializeAsV32(new OpenApiJsonWriter(text));

        return text.ToString();
    }

    private static string Compact(JsonElement schema) => JsonSerializer.Serialize(schema);

    private static JsonElement Operation(JsonElement document, string kind) =>
        document.GetProperty("paths").GetProperty("/dom/" + kind).GetProperty("post");

    private static JsonElement Schemas(JsonElement document) =>
        document.GetProperty("components").GetProperty("schemas");

    [Theory]
    [MemberData(nameof(Outputs))]
    public void EachTypeIsFreeFormAsAProperty(string format, string version)
    {
        var envelope = Schemas(Written(DomDocument.Value, format, version))
            .GetProperty("Envelope")
            .GetProperty("properties");

        foreach (var (name, schema) in Expected)
        {
            Assert.Equal(schema, Compact(envelope.GetProperty(name)));
        }

        // A nullable member adds null to a typed schema, and a schema that already admits null is
        // left alone.
        var lowered = version == "3.0.0";

        Assert.Equal(Free, Compact(envelope.GetProperty("maybeNode")));
        Assert.Equal(
            lowered
                ? "{\"type\":\"object\",\"nullable\":true}"
                : "{\"type\":[\"object\",\"null\"]}",
            Compact(envelope.GetProperty("maybeObject"))
        );
        Assert.Equal(
            lowered
                ? "{\"type\":\"array\",\"nullable\":true,\"items\":{}}"
                : "{\"type\":[\"array\",\"null\"],\"items\":{}}",
            Compact(envelope.GetProperty("maybeArray"))
        );
    }

    [Theory]
    [MemberData(nameof(Outputs))]
    public void EachTypeIsFreeFormAsARequestBody(string format, string version)
    {
        var document = Written(DomDocument.Value, format, version);

        foreach (var (kind, schema) in Expected)
        {
            var body = Operation(document, kind)
                .GetProperty("requestBody")
                .GetProperty("content")
                .GetProperty("application/json")
                .GetProperty("schema");

            Assert.Equal(schema, Compact(body));
        }
    }

    [Theory]
    [MemberData(nameof(Outputs))]
    public void EachTypeIsFreeFormAsAResponse(string format, string version)
    {
        var document = Written(DomDocument.Value, format, version);

        foreach (var (kind, schema) in Expected)
        {
            var response = Operation(document, kind)
                .GetProperty("responses")
                .GetProperty("200")
                .GetProperty("content")
                .GetProperty("application/json")
                .GetProperty("schema");

            Assert.Equal(schema, Compact(response));
        }
    }

    /// <summary>
    /// No component for a DOM type, or for the two types only their members reach.
    /// </summary>
    [Theory]
    [MemberData(nameof(Outputs))]
    public void NoDomTypeIsAComponent(string format, string version)
    {
        var document = Written(DomDocument.Value, format, version);
        var names = Schemas(document).EnumerateObject().Select(schema => schema.Name).ToList();

        Assert.Contains("Envelope", names);
        Assert.Empty(
            names.Intersect([
                "JsonElement",
                "JsonNode",
                "JsonObject",
                "JsonArray",
                "JsonValue",
                "JsonNodeOptions",
                "JsonValueKind",
            ])
        );
        Assert.DoesNotContain("this[]", document.GetRawText());
    }

    /// <summary>
    /// An application's own type named <c>JsonNode</c> is a model like any other, because the DOM
    /// types are recognised by their namespace.
    /// </summary>
    [Fact]
    public void AnApplicationTypeNamedJsonNodeIsStillReflected()
    {
        var document = JsonDocument
            .Parse(
                Emit(
                    """
                        [Get("/own")]
                        public Task<TestApp.Shapes.JsonNode> Own() =>
                            Task.FromResult(new TestApp.Shapes.JsonNode("a"));
                    """,
                    "",
                    "public record JsonNode(string Label);"
                )
            )
            .RootElement;

        var properties = Schemas(document).GetProperty("JsonNode").GetProperty("properties");

        Assert.Equal(["label"], properties.EnumerateObject().Select(property => property.Name));
    }

    /// <summary>An indexer is not a member on the wire, so a model's schema has no property for it.</summary>
    [Fact]
    public void AnIndexerIsNotAProperty()
    {
        var document = JsonDocument
            .Parse(
                Emit(
                    """
                        [Get("/indexed")]
                        public Task<Indexed> Indexed() => Task.FromResult(new Indexed());
                    """,
                    """
                    public class Indexed {
                        public string Name { get; set; } = "";

                        public string this[int index] => Name;
                    }
                    """
                )
            )
            .RootElement;

        var properties = Schemas(document).GetProperty("Indexed").GetProperty("properties");

        Assert.Equal(["name"], properties.EnumerateObject().Select(property => property.Name));
    }
}
