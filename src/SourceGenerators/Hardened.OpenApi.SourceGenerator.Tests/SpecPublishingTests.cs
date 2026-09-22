using System.Text.Json;
using Hardened.SourceGeneration.Testing;
using Xunit;

namespace Hardened.OpenApi.SourceGenerator.Tests;

/// <summary>
/// What a specification-first application registers to serve its contract, its generated
/// document and the reference page, and the service-wide policies a contract states at its root.
/// </summary>
/// <remarks>
/// <c>PublishUrl</c>, <c>SourceUrl</c> and <c>UiUrl</c> are metadata on the <c>OpenApiSpec</c> item
/// rather than anything the yaml says, so these set them on the model the way
/// <c>ExtractSpecTask</c> does.
/// </remarks>
public class SpecPublishingTests
{
    private const string Pets = """
        openapi: "3.0.0"
        info: { title: Pets, version: "1.0" }
        paths:
          /pets/{petId}:
            get:
              tags: [Pet]
              operationId: getPet
              parameters:
                - name: petId
                  in: path
                  required: true
                  schema: { type: string }
              responses:
                '200':
                  description: A pet
                  content:
                    application/json:
                      schema:
                        $ref: '#/components/schemas/Pet'
        components:
          schemas:
            Pet:
              type: object
              required: [id]
              properties:
                id: { type: string }
        """;

    private static string Routing(GeneratorResult result) =>
        result.AssertNoErrors().SourceContaining("SpecRouting");

    /// <summary>
    /// A <c>PublishUrl</c> serves the document generated from the model, from the member the
    /// document source emits on the entry point.
    /// </summary>
    [Fact]
    public void APublishUrlServesTheGeneratedDocument()
    {
        var routing = Routing(
            OpenApiGenerator.Run(Pets, configure: model => model.PublishUrl = "/openapi.json")
        );

        Assert.Contains(
            "new global::Hardened.Web.Runtime.OpenApi.OpenApiDocumentProvider(serviceProvider, "
                + "global::TestNamespace.TestApp.",
            routing
        );
        Assert.Contains(", \"/openapi.json\"))", routing);
        Assert.DoesNotContain("HardenedOpenApiUi", routing);
    }

    /// <summary>
    /// A <c>UiUrl</c> adds the reference page over the published document, limited to the
    /// environments the item names.
    /// </summary>
    [Fact]
    public void AUiUrlAddsTheReferencePageOverThePublishedDocument()
    {
        var routing = Routing(
            OpenApiGenerator.Run(
                Pets,
                configure: model =>
                {
                    model.PublishUrl = "/openapi.json";
                    model.UiUrl = "/docs";
                    model.UiEnvironments = "Development";
                }
            )
        );

        Assert.Contains(
            "new global::Hardened.Web.Runtime.OpenApi.HardenedOpenApiUi { Path = \"/docs\", "
                + "DocumentPath = \"/openapi.json\", Environments = \"Development\" }",
            routing
        );
    }

    /// <summary>
    /// And with no environments named, the page is registered without the restriction.
    /// </summary>
    [Fact]
    public void AReferencePageWithNoEnvironmentsIsNotRestricted()
    {
        var routing = Routing(
            OpenApiGenerator.Run(
                Pets,
                configure: model =>
                {
                    model.PublishUrl = "/openapi.json";
                    model.UiUrl = "/docs";
                }
            )
        );

        Assert.Contains(
            "HardenedOpenApiUi { Path = \"/docs\", DocumentPath = \"/openapi.json\" }",
            routing
        );
    }

    /// <summary>
    /// A <c>SourceUrl</c> serves the contract as written, under its own content type, from the
    /// type the task embeds it in - and serving the source publishes nothing generated.
    /// </summary>
    [Fact]
    public void ASourceUrlServesTheContractAsWritten()
    {
        var result = OpenApiGenerator.Run(
            Pets,
            configure: model => model.SourceUrl = "/petstore.yaml"
        );

        var routing = Routing(result);

        Assert.Contains(".DocumentGZip, \"/petstore.yaml\", global::TestNamespace.", routing);
        Assert.Contains(".ContentType))", routing);
        Assert.Single(routing.Split("OpenApiDocumentProvider(").Skip(1));
    }

    /// <summary>
    /// The negotiation and error-body policies a contract states at its root are registered for
    /// the whole service.
    /// </summary>
    [Theory]
    [InlineData("strict", "negotiated", "Strict", "Negotiated")]
    [InlineData("lenient", "json", "Lenient", "Json")]
    public void RootPoliciesAreRegisteredForTheService(
        string negotiation,
        string errorBodies,
        string mode,
        string format
    )
    {
        var spec = Pets.Replace(
            "info: { title: Pets, version: \"1.0\" }",
            "info: { title: Pets, version: \"1.0\" }\n"
                + $"x-hardened-content-negotiation: {negotiation}\n"
                + $"x-hardened-error-bodies: {errorBodies}"
        );

        var routing = Routing(OpenApiGenerator.Run(spec));

        Assert.Contains(
            "new global::Hardened.Requests.Abstract.Serializer.ContentNegotiationPolicy("
                + $"global::Hardened.Requests.Abstract.Serializer.ContentNegotiationMode.{mode})",
            routing
        );
        Assert.Contains(
            "new global::Hardened.Requests.Abstract.Serializer.ErrorBodyPolicy("
                + $"global::Hardened.Requests.Abstract.Serializer.ErrorBodyFormat.{format})",
            routing
        );
    }

    private const string PetsWithMessagePackRefusal = """
        openapi: "3.0.0"
        info: { title: Pets, version: "1.0" }
        paths:
          /pets/{petId}:
            get:
              tags: [Pet]
              operationId: getPet
              parameters:
                - name: petId
                  in: path
                  required: true
                  schema: { type: string }
              responses:
                '200':
                  description: A pet
                  content:
                    application/json:
                      schema:
                        $ref: '#/components/schemas/Pet'
                '404':
                  description: No such pet
                  content:
                    application/x-msgpack:
                      schema:
                        $ref: '#/components/schemas/Problem'
        components:
          schemas:
            Pet:
              type: object
              required: [id]
              properties:
                id: { type: string }
            Problem:
              type: object
              properties:
                detail: { type: string }
        """;

    /// <summary>
    /// <c>[JsonErrorBodies]</c> on the entry point narrows every refusal the document publishes to
    /// JSON, because that is what the service then answers them with.
    /// </summary>
    [Fact]
    public void JsonErrorBodiesNarrowThePublishedRefusals()
    {
        var result = OpenApiGenerator.Run(
            PetsWithMessagePackRefusal,
            """
            using Hardened.Requests.Abstract.Attributes;
            using Hardened.Shared.Runtime.Attributes;

            namespace TestNamespace;

            [HardenedModule]
            [JsonErrorBodies]
            public partial class TestApp {
            }
            """
        );

        result.AssertNoErrors();

        var notFound = SpecFirstDocumentTests
            .PublishedDocumentFrom(result)
            .GetProperty("paths")
            .GetProperty("/pets/{petId}")
            .GetProperty("get")
            .GetProperty("responses")
            .GetProperty("404")
            .GetProperty("content");

        var media = Assert.Single(notFound.EnumerateObject());

        Assert.Equal("application/json", media.Name);
    }
}
