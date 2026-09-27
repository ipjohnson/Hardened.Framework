using System.Text.Json;
using Hardened.Requests.Abstract.Attributes;
using Hardened.Requests.Abstract.Responses;
using Hardened.Requests.Runtime.Authorization;
using Hardened.SourceGeneration.Testing;
using Hardened.Web.Runtime.Attributes;
using Hardened.Web.Runtime.Responses;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Hardened.Web.SourceGenerator.Tests;

/// <summary>
/// The bodies a code-first handler declares for its failures, as the framework's own refusals
/// write them.
/// </summary>
/// <remarks>
/// <para>
/// The 0.41 trial's A-05 and A-02. A form over its limit answered <c>ErrorModel</c> where the
/// operation declares 413 with <c>ContentTooLarge</c>, and <c>[AuthorizeGrants]</c> beside a
/// declared <c>Forbidden</c> published the 403 as a <c>oneOf</c> of both, which Kiota could not
/// generate an error type for.
/// </para>
/// <para>
/// A contract's declared bodies already reached the handler info. A code-first handler's did not,
/// so every refusal the framework raised at a declared status sent its own shape.
/// </para>
/// </remarks>
public class DeclaredRefusalBodyTests
{
    private static readonly Type[] Anchors =
    [
        typeof(GetAttribute),
        typeof(FromBodyAttribute),
        typeof(Response<,>),
        typeof(AuthorizeGrantsAttribute),
    ];

    private static GeneratorResult Generate(string handlers) =>
        GeneratorTestHarness
            .Run(
                new Dictionary<string, string>
                {
                    ["Test.cs"] = $$"""
                    using System.Threading.Tasks;
                    using Hardened.Requests.Abstract.Responses;
                    using Hardened.Requests.Runtime.Authorization;
                    using Hardened.Shared.Runtime.Attributes;
                    using Hardened.Web.Runtime.Attributes;
                    using Hardened.Web.Runtime.Responses;

                    namespace TestApp;

                    [HardenedModule]
                    {{GeneratedOpenApiDocument.EnableAttribute}}
                    public partial class TestApplication { }

                    public record Photo(string Id);
                    public record ApiError(string Code);

                    public class PhotoController {
                    {{handlers}}
                    }
                    """,
                },
                new IIncrementalGenerator[] { new WebLibrarySourceGenerator() },
                Anchors
            )
            .AssertNoErrors();

    [Fact]
    public void AProblemRecordAHandlerDeclaresIsTheBodyItsRefusalsWrite()
    {
        var source = Generate(
                """
                    [Post("/photos")]
                    public Response<Photo, ContentTooLarge, NotFound> Upload(string name) => new Photo(name);
                """
            )
            .SourceContaining("PhotoController_Upload");

        Assert.Contains(
            "declaredErrorBodies: new global::System.Collections.Generic.Dictionary<int, object> { "
                + "{ 404, global::Hardened.Web.Runtime.Responses.NotFound.Default }, "
                + "{ 413, global::Hardened.Web.Runtime.Responses.ContentTooLarge.Default } }",
            source
        );
    }

    [Fact]
    public void AThrownProblemRecordIsTheBodyItsRefusalsWrite()
    {
        var source = Generate(
                """
                    [Post("/photos")]
                    [Throws<Conflict>]
                    public Photo Upload(string name) => new Photo(name);
                """
            )
            .SourceContaining("PhotoController_Upload");

        Assert.Contains("{ 409, global::Hardened.Web.Runtime.Responses.Conflict.Default }", source);
    }

    /// <summary>
    /// A generic case sends a body of the application's own type, and there is no instance of it
    /// to share, so the framework's shape is what its refusals send.
    /// </summary>
    [Fact]
    public void ABodyOfTheHandlersOwnDeclaresNone()
    {
        var source = Generate(
                """
                    [Post("/photos")]
                    public Response<Photo, ContentTooLarge<ApiError>> Upload(string name) => new Photo(name);
                """
            )
            .SourceContaining("PhotoController_Upload");

        Assert.DoesNotContain("declaredErrorBodies", source);
    }

    /// <summary>
    /// A-02: the 403 the handler declares is the body the authorization filter's refusal writes, so
    /// the document publishes that body alone rather than a <c>oneOf</c> with <c>ErrorModel</c>.
    /// </summary>
    [Fact]
    public void AStatusTheHandlerDeclaresIsPublishedWithItsBodyAlone()
    {
        var result = Generate(
            """
                [Get("/photos/{id}")]
                [AuthorizeGrants("staff")]
                public Response<Photo, Forbidden> Get(string id) => new Photo(id);
            """
        );

        var forbidden = JsonDocument
            .Parse(GeneratedOpenApiDocument.Extract(result.SourceContaining("OpenApiDocument")))
            .RootElement.GetProperty("paths")
            .GetProperty("/photos/{id}")
            .GetProperty("get")
            .GetProperty("responses")
            .GetProperty("403")
            .GetProperty("content")
            .GetProperty("application/problem+json")
            .GetProperty("schema");

        Assert.False(forbidden.TryGetProperty("oneOf", out _));
        Assert.Equal("#/components/schemas/Forbidden", forbidden.GetProperty("$ref").GetString());
    }

    /// <summary>
    /// Where the handler's body has no instance to share, the refusal still sends the framework's
    /// shape, so the document keeps both.
    /// </summary>
    [Fact]
    public void AStatusWhoseBodyHasNoInstanceKeepsTheFrameworksShapeBeside()
    {
        var result = Generate(
            """
                [Get("/photos/{id}")]
                [AuthorizeGrants("staff")]
                public Response<Photo, Forbidden<ApiError>> Get(string id) => new Photo(id);
            """
        );

        var forbidden = JsonDocument
            .Parse(GeneratedOpenApiDocument.Extract(result.SourceContaining("OpenApiDocument")))
            .RootElement.GetProperty("paths")
            .GetProperty("/photos/{id}")
            .GetProperty("get")
            .GetProperty("responses")
            .GetProperty("403")
            .GetProperty("content")
            .GetProperty("application/json")
            .GetProperty("schema");

        Assert.Equal(2, forbidden.GetProperty("oneOf").GetArrayLength());
    }

    private static JsonElement BadRequest(string declared)
    {
        var result = Generate(
            $$"""
                [Get("/photos/{id}")]
                public Response<Photo, {{declared}}> Get(int id) => new Photo(id.ToString());
            """
        );

        return JsonDocument
            .Parse(GeneratedOpenApiDocument.Extract(result.SourceContaining("OpenApiDocument")))
            .RootElement.GetProperty("paths")
            .GetProperty("/photos/{id}")
            .GetProperty("get")
            .GetProperty("responses")
            .GetProperty("400")
            .GetProperty("content");
    }

    /// <summary>
    /// A validated operation declaring a <c>BadRequest</c> answers its validation failures with it,
    /// so the 400 lists that problem alone.
    /// </summary>
    [Fact]
    public void AValidationFailureAtADeclaredProblemIsPublishedAsThatProblem()
    {
        var content = BadRequest("BadRequest");

        Assert.Equal(
            "#/components/schemas/BadRequest",
            content
                .GetProperty("application/problem+json")
                .GetProperty("schema")
                .GetProperty("$ref")
                .GetString()
        );
    }

    /// <summary>
    /// A generic case has no instance, so a validation failure there still sends the framework's
    /// envelope, and the 400 lists both bodies.
    /// </summary>
    [Fact]
    public void AValidationFailureAtAGenericCaseListsTheEnvelopeBeside()
    {
        var oneOf = BadRequest("BadRequest<ApiError>")
            .GetProperty("application/json")
            .GetProperty("schema")
            .GetProperty("oneOf")
            .EnumerateArray()
            .Select(schema => schema.GetProperty("$ref").GetString())
            .ToArray();

        Assert.Equal(
            ["#/components/schemas/ApiError", "#/components/schemas/RequestValidationError"],
            oneOf
        );
    }
}
