using System.Text.Json;
using Hardened.Requests.Abstract.Attributes;
using Hardened.SourceGeneration.Testing;
using Hardened.Web.Runtime.Attributes;
using Xunit;

namespace Hardened.Web.SourceGenerator.Tests;

/// <summary>
/// What a handler binding from a form, or a model from the query string, publishes.
/// </summary>
/// <remarks>
/// A <c>[FromForm]</c> handler published no request body and no parameters, so a client generated
/// from the document had no way to send the fields.
/// </remarks>
public class FormDocumentTests
{
    private static readonly Type[] Anchors = [typeof(GetAttribute), typeof(FromBodyAttribute)];

    private static JsonElement Document(string handlers)
    {
        var result = GeneratorTestHarness.Run(
            $$"""
            using System.Collections.Generic;
            using Hardened.Requests.Abstract.Attributes;
            using Hardened.Shared.Runtime.Attributes;
            using Hardened.Web.Runtime.Attributes;

            namespace TestApp;

            [HardenedModule]
            {{GeneratedOpenApiDocument.EnableAttribute}}
            public partial class TestApplication { }

            public enum Sort { Newest, Cheapest }

            public record Search(int Page, string Q, int Size = 20);

            public class Paging {
                public int Page { get; set; }
                public Sort Order { get; set; } = Sort.Newest;
                public List<string>? Tags { get; set; }
            }

            public class SearchController {
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

    private static JsonElement Operation(JsonElement document, string path, string verb) =>
        document.GetProperty("paths").GetProperty(path).GetProperty(verb);

    private static JsonElement FormSchema(JsonElement operation)
    {
        var content = operation.GetProperty("requestBody").GetProperty("content");
        var media = Assert.Single(content.EnumerateObject());

        Assert.Equal("application/x-www-form-urlencoded", media.Name);

        return media.Value.GetProperty("schema");
    }

    /// <summary>
    /// Fields bound one at a time are one property each, and the ones without a default are
    /// required.
    /// </summary>
    [Fact]
    public void FieldsArePublishedAsAUrlEncodedBody()
    {
        var operation = Operation(
            Document(
                """
                    [Post("/sign-in")]
                    public string SignIn(
                        [FromForm] string username,
                        [FromForm("remember_me")] bool? rememberMe,
                        [FromForm] int attempts = 1) => username;
                """
            ),
            "/sign-in",
            "post"
        );

        var schema = FormSchema(operation);

        Assert.Equal("object", schema.GetProperty("type").GetString());
        Assert.Equal(
            ["username"],
            schema.GetProperty("required").EnumerateArray().Select(e => e.GetString())
        );

        var properties = schema.GetProperty("properties");

        Assert.Equal("string", properties.GetProperty("username").GetProperty("type").GetString());
        Assert.True(properties.TryGetProperty("remember_me", out _));
        Assert.Equal("integer", properties.GetProperty("attempts").GetProperty("type").GetString());
        Assert.True(operation.GetProperty("requestBody").GetProperty("required").GetBoolean());
        Assert.False(operation.TryGetProperty("parameters", out _));
    }

    /// <summary>
    /// A form model is its own schema by reference, because the binder names each field the way
    /// that schema names the member.
    /// </summary>
    [Fact]
    public void AFormModelIsPublishedByReference()
    {
        var document = Document(
            """
                [Post("/search")]
                public string Find([FromForm] Search search) => search.Q;
            """
        );

        var schema = FormSchema(Operation(document, "/search", "post"));

        Assert.Equal("#/components/schemas/Search", schema.GetProperty("$ref").GetString());

        var component = document
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty("Search");

        Assert.Equal(
            ["page", "q"],
            component.GetProperty("required").EnumerateArray().Select(e => e.GetString())
        );
    }

    /// <summary>A field beside a model joins the model's schema with <c>allOf</c>.</summary>
    [Fact]
    public void AFieldBesideAModelIsJoinedWithAllOf()
    {
        var schema = FormSchema(
            Operation(
                Document(
                    """
                        [Post("/search")]
                        public string Find([FromForm] Search search, [FromForm] string token) =>
                            search.Q;
                    """
                ),
                "/search",
                "post"
            )
        );

        var parts = schema.GetProperty("allOf").EnumerateArray().ToList();

        Assert.Equal(2, parts.Count);
        Assert.Equal("#/components/schemas/Search", parts[0].GetProperty("$ref").GetString());
        Assert.True(parts[1].GetProperty("properties").TryGetProperty("token", out _));
    }

    /// <summary>
    /// A query string model is one parameter per member, because the members are what a caller
    /// sends.
    /// </summary>
    [Fact]
    public void AQueryStringModelIsOneParameterPerMember()
    {
        var document = Document(
            """
                [Get("/list")]
                public int List([FromQueryString] Paging paging) => paging.Page;
            """
        );

        var parameters = Operation(document, "/list", "get")
            .GetProperty("parameters")
            .EnumerateArray()
            .ToDictionary(p => p.GetProperty("name").GetString()!);

        Assert.Equal(["page", "order", "tags"], parameters.Keys);
        Assert.All(parameters.Values, p => Assert.Equal("query", p.GetProperty("in").GetString()));
        Assert.True(parameters["page"].GetProperty("required").GetBoolean());
        Assert.False(parameters["order"].GetProperty("required").GetBoolean());
        Assert.False(parameters["tags"].GetProperty("required").GetBoolean());
        Assert.Equal(
            "array",
            parameters["tags"].GetProperty("schema").GetProperty("type").GetString()
        );

        // The enum member is published with its vocabulary, which only the member refers to.
        var order = parameters["order"].GetProperty("schema").GetProperty("$ref").GetString()!;
        var component = document
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty(order.Substring(order.LastIndexOf('/') + 1));

        Assert.Equal(
            ["newest", "cheapest"],
            component.GetProperty("enum").EnumerateArray().Select(e => e.GetString())
        );
    }

    /// <summary>A form of optional fields is not a required body.</summary>
    [Fact]
    public void AFormOfOptionalFieldsIsNotARequiredBody()
    {
        var operation = Operation(
            Document(
                """
                    [Post("/preferences")]
                    public string Save([FromForm] string? theme, [FromForm] int size = 12) =>
                        theme ?? "";
                """
            ),
            "/preferences",
            "post"
        );

        Assert.False(operation.GetProperty("requestBody").GetProperty("required").GetBoolean());
        Assert.False(FormSchema(operation).TryGetProperty("required", out _));
    }
}
