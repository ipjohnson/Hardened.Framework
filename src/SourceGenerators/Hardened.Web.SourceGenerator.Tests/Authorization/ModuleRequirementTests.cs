using System.Text.Json;
using System.Text.RegularExpressions;
using Hardened.Requests.Abstract.Attributes;
using Hardened.Requests.Runtime.Authorization;
using Hardened.SourceGeneration.Testing;
using Hardened.Web.Runtime.Attributes;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Hardened.Web.SourceGenerator.Tests.Authorization;

/// <summary>
/// <c>[Authorize&lt;TScheme&gt;]</c> on a <c>[HardenedModule]</c> class, in the generated source and
/// in the served document.
/// </summary>
/// <remarks>
/// <para>
/// The 0.41 trial's A-19. The attribute compiled on a module class and guarded nothing, so an
/// anonymous request reached every handler that carried no requirement of its own. The module class
/// is where <c>[BasePath]</c> and <c>[RequireAuthorization]</c> go, so it is where a developer writes
/// a rule meant for every route.
/// </para>
/// <para>
/// Both halves are asserted from one generation: the array the pipeline merges into each handler,
/// and what the document publishes for each operation.
/// </para>
/// </remarks>
public class ModuleRequirementTests
{
    private static readonly Type[] Anchors =
    [
        typeof(GetAttribute),
        typeof(FromBodyAttribute),
        typeof(AllowAnonymousAttribute),
    ];

    private const string Controllers = """
        public class RequestsController {
            [Get("/requests")]
            public string List() => "";

            [Post("/requests")]
            public string Open(string title) => "";

            [Get("/health")]
            [AllowAnonymous]
            public string Health() => "";
        }

        [BasePath("/public")]
        [AllowAnonymous]
        public class PublicController {
            [Get("/version")]
            public string Version() => "";
        }

        """;

    private static GeneratorResult Generate(string moduleAttributes) =>
        GeneratorTestHarness
            .Run(
                new Dictionary<string, string>
                {
                    ["Test.cs"] = $$"""
                    using Hardened.Requests.Abstract.Authorization;
                    using Hardened.Requests.Runtime.Authorization;
                    using Hardened.Shared.Runtime.Attributes;
                    using Hardened.Web.Runtime.Attributes;

                    namespace TestApp;

                    [HttpAuthenticationScheme("bearer")]
                    public sealed class BearerAuth : IAuthenticationScheme;

                    [HardenedModule]
                    {{GeneratedOpenApiDocument.EnableAttribute}}
                    {{moduleAttributes}}
                    public partial class TestApplication { }

                    {{Controllers}}
                    """,
                },
                new IIncrementalGenerator[] { new WebLibrarySourceGenerator() },
                Anchors
            )
            .AssertNoErrors();

    private static JsonElement Document(GeneratorResult result) =>
        JsonDocument
            .Parse(GeneratedOpenApiDocument.Extract(result.SourceContaining("OpenApiDocument")))
            .RootElement.Clone();

    private static JsonElement Operation(JsonElement document, string path, string method) =>
        document.GetProperty("paths").GetProperty(path).GetProperty(method);

    private static string[] Statuses(JsonElement operation) =>
        operation
            .GetProperty("responses")
            .EnumerateObject()
            .Select(response => response.Name)
            .OrderBy(status => status, StringComparer.Ordinal)
            .ToArray();

    private static string[] Security(JsonElement operation) =>
        operation.TryGetProperty("security", out var security)
            ? security.EnumerateArray().Select(requirement => requirement.GetRawText()).ToArray()
            : [];

    /// <summary>
    /// The requirement reaches the array beside the routing table that the pipeline merges into
    /// every handler in the compilation, which is what makes it guard them.
    /// </summary>
    [Fact]
    public void TheRequirementIsEmittedForThePipeline()
    {
        var routing = Generate("[Authorize<BearerAuth>]").SourceContaining("Routing");

        Assert.Contains("class ApplicationFilters", routing);
        Assert.Single(
            Regex.Matches(routing, @"new [\w.:]*AuthorizeAttribute<[\w.:]*BearerAuth>\(\)")
        );
    }

    [Theory]
    [InlineData("/requests", "get")]
    [InlineData("/requests", "post")]
    public void EveryOperationPublishesTheSchemeAndThe401(string path, string method)
    {
        var operation = Operation(Document(Generate("[Authorize<BearerAuth>]")), path, method);

        Assert.Equal(["{\"BearerAuth\":[]}"], Security(operation));
        Assert.Contains("401", Statuses(operation));
        Assert.DoesNotContain("403", Statuses(operation));
    }

    /// <summary>
    /// A grant beside the scheme is what lets an authenticated caller be refused, so it brings
    /// the 403.
    /// </summary>
    [Theory]
    [InlineData("/requests", "get")]
    [InlineData("/requests", "post")]
    public void AGrantBesideTheSchemePublishesThe403(string path, string method)
    {
        var operation = Operation(
            Document(Generate("[Authorize<BearerAuth>]\n[AuthorizeGrants(\"admin\")]")),
            path,
            method
        );

        Assert.Contains("401", Statuses(operation));
        Assert.Contains("403", Statuses(operation));
    }

    /// <summary>
    /// <c>[AllowAnonymous]</c> on the method or on the class makes the handler public at run time,
    /// so its operation publishes none of the requirement.
    /// </summary>
    [Theory]
    [InlineData("/health")]
    [InlineData("/public/version")]
    public void AnOperationThatAllowsAnonymousCallersPublishesNoneOfIt(string path)
    {
        var operation = Operation(Document(Generate("[Authorize<BearerAuth>]")), path, "get");

        Assert.Empty(Security(operation));
        Assert.Equal(["200"], Statuses(operation));
    }

    [Fact]
    public void TheSchemeIsDeclaredInTheComponents()
    {
        var document = Document(Generate("[Authorize<BearerAuth>]"));

        Assert.Equal(
            "bearer",
            document
                .GetProperty("components")
                .GetProperty("securitySchemes")
                .GetProperty("BearerAuth")
                .GetProperty("scheme")
                .GetString()
        );
    }

    /// <summary>
    /// A module declaring no requirement generates what it generated before.
    /// </summary>
    [Fact]
    public void AModuleDeclaringNoRequirementPublishesNone()
    {
        var result = Generate("");

        Assert.DoesNotContain("ApplicationFilters", result.SourceContaining("Routing"));
        Assert.Empty(Security(Operation(Document(result), "/requests", "get")));
    }
}
