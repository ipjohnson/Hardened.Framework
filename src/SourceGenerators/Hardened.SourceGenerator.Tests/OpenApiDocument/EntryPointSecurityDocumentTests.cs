using System.Text.Json;
using CSharpAuthor;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.OpenApiDocument;
using Hardened.SourceGenerator.Requests;
using Hardened.SourceGenerator.Shared;
using Xunit;

namespace Hardened.SourceGenerator.Tests.OpenApiDocument;

/// <summary>
/// What a requirement declared on the entry point publishes, per operation.
/// </summary>
/// <remarks>
/// <para>
/// The pipeline conjoins <c>[Authorize&lt;TScheme&gt;]</c> on a module class into every handler
/// compiled with it, and <c>[AllowAnonymous]</c> on a handler cancels it. These pin the document to
/// the same rule. Driven over hand-built facts, as <see cref="EntryPointRungDocumentTests"/> is, so
/// the combination with what an operation already declares is exercised on each shape it meets.
/// </para>
/// </remarks>
public class EntryPointSecurityDocumentTests
{
    private static readonly SecuritySchemeDeclaration Bearer = new(
        "BearerAuth",
        "{\"type\":\"http\",\"scheme\":\"bearer\"}",
        carriesScopes: false
    );

    private static readonly SecuritySchemeDeclaration ApiKey = new(
        "ApiKeyAuth",
        "{\"type\":\"apiKey\",\"name\":\"X-Api-Key\",\"in\":\"header\"}",
        carriesScopes: false
    );

    private static readonly SecuritySchemeDeclaration PetsOAuth = new(
        "PetsOAuth",
        "{\"type\":\"oauth2\",\"flows\":{\"clientCredentials\":{\"tokenUrl\":\"https://auth.example/token\",\"scopes\":{}}}}",
        carriesScopes: true
    );

    private static ITypeDefinition Type(string name) => TypeDefinition.Get("TestApp", name);

    /// <summary>The 403 <c>IAuthorizeAttribute</c> declares, on every verb.</summary>
    private static DeclaredOperationFacts Forbidden() =>
        new(
            [
                new ScopedRefusal(
                    new ResponseSchemaModel(
                        403,
                        "The caller does not hold what this operation requires.",
                        null
                    ),
                    new DeclaredScope(null, notWhenStreaming: false)
                ),
            ],
            [],
            []
        );

    private static EntryPointSecurity Requires(
        IReadOnlyList<SecuritySchemeDeclaration>? schemes = null,
        IReadOnlyList<string>? grants = null
    ) => new(schemes ?? [Bearer], grants ?? [], Forbidden());

    private static EntryPointSelector.Model App(EntryPointSecurity? security) =>
        new()
        {
            EntryPointType = Type("Application"),
            AttributeModels = Array.Empty<AttributeModel>(),
            DeclaresRequirement = security != null,
            SecurityFacts = security,
        };

    private static AttributeModel AllowAnonymous() =>
        new(
            TypeDefinition.Get(
                "Hardened.Requests.Runtime.Authorization",
                "AllowAnonymousAttribute"
            ),
            "",
            ""
        );

    private static RequestHandlerModel Handler(
        IReadOnlyList<AttributeModel>? filters = null,
        IReadOnlyList<SecuritySchemeDeclaration>? schemes = null,
        IReadOnlyList<string>? grants = null,
        IReadOnlyList<string>? requirements = null
    ) =>
        new(
            new RequestHandlerNameModel("/books", "GET"),
            Type("BookController"),
            "List",
            TypeDefinition.Get("TestApp.Generated", "BookController_List"),
            [],
            new ResponseInformationModel { ReturnType = Type("Book") },
            filters ?? []
        )
        {
            DeclaredSecuritySchemes = schemes ?? Array.Empty<SecuritySchemeDeclaration>(),
            DeclaredGrants = grants ?? Array.Empty<string>(),
            SecurityRequirements = requirements ?? Array.Empty<string>(),
        };

    private static JsonElement Document(
        EntryPointSecurity? security,
        RequestHandlerModel handler
    ) =>
        JsonDocument
            .Parse(OpenApiDocumentGenerator.Write(App(security), [handler], ""))
            .RootElement.Clone();

    private static JsonElement Operation(JsonElement document) =>
        document.GetProperty("paths").GetProperty("/books").GetProperty("get");

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

    [Fact]
    public void AnOperationPublishesTheSchemeThe401AndThe403()
    {
        var operation = Operation(Document(Requires(), Handler()));

        Assert.Equal(["{\"BearerAuth\":[]}"], Security(operation));
        Assert.Equal(["200", "401", "403"], Statuses(operation));
    }

    [Fact]
    public void TheSchemeJoinsTheComponents()
    {
        var document = Document(Requires(), Handler());

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
    /// <c>[AllowAnonymous]</c> on the handler's method or class makes it public at run time, so its
    /// operation publishes none of the module's requirement.
    /// </summary>
    [Fact]
    public void AnOperationThatAllowsAnonymousCallersPublishesNoneOfIt()
    {
        var operation = Operation(Document(Requires(), Handler(filters: [AllowAnonymous()])));

        Assert.Empty(Security(operation));
        Assert.Equal(["200"], Statuses(operation));
    }

    /// <summary>
    /// A scheme the handler names itself and the module's are alternatives, as a method's and its
    /// class's are. The pipeline accepts a caller any source authenticated.
    /// </summary>
    [Fact]
    public void TheHandlersOwnSchemeAndTheModulesAreAlternatives()
    {
        var operation = Operation(
            Document(Requires(), Handler(schemes: [ApiKey], requirements: ["{\"ApiKeyAuth\":[]}"]))
        );

        Assert.Equal(["{\"ApiKeyAuth\":[]}", "{\"BearerAuth\":[]}"], Security(operation));
    }

    /// <summary>
    /// The handler's literal grants become scopes on the module's OAuth2 scheme, which is what the
    /// same grants beside the same scheme on its class publish.
    /// </summary>
    [Fact]
    public void TheHandlersGrantsBecomeScopesOnTheModulesOAuthScheme()
    {
        var operation = Operation(
            Document(Requires(schemes: [PetsOAuth]), Handler(grants: ["pets:read"]))
        );

        Assert.Equal(["{\"PetsOAuth\":[\"pets:read\"]}"], Security(operation));
    }

    /// <summary>
    /// And the module's grants join the handler's on the handler's own OAuth2 scheme.
    /// </summary>
    [Fact]
    public void TheModulesGrantsJoinTheHandlersOnItsOAuthScheme()
    {
        var operation = Operation(
            Document(
                Requires(schemes: [], grants: ["pets:admin"]),
                Handler(
                    schemes: [PetsOAuth],
                    grants: ["pets:read"],
                    requirements: ["{\"PetsOAuth\":[\"pets:read\"]}"]
                )
            )
        );

        Assert.Equal(["{\"PetsOAuth\":[\"pets:read\",\"pets:admin\"]}"], Security(operation));
    }

    /// <summary>
    /// A described operation's requirements are its contract's, written as the contract wrote
    /// them, and the module's scheme is added beside them.
    /// </summary>
    [Fact]
    public void ADescribedOperationKeepsItsContractsRequirements()
    {
        var operation = Operation(
            Document(Requires(), Handler(requirements: ["{\"oauth\":[\"read\"]}"]))
        );

        Assert.Equal(["{\"oauth\":[\"read\"]}", "{\"BearerAuth\":[]}"], Security(operation));
    }

    /// <summary>
    /// Grants with no scheme in sight publish the 403 alone, as they do on a handler.
    /// </summary>
    [Fact]
    public void GrantsWithNoSchemePublishThe403Alone()
    {
        var operation = Operation(Document(Requires(schemes: [], grants: ["admin"]), Handler()));

        Assert.Empty(Security(operation));
        Assert.Equal(["200", "403"], Statuses(operation));
    }

    [Fact]
    public void AnEntryPointDeclaringNoRequirementChangesNothing()
    {
        var handler = Handler();

        Assert.Equal(
            OpenApiDocumentGenerator.Write(App(null), [handler], ""),
            OpenApiDocumentGenerator.Write(
                App(new EntryPointSecurity([], [], DeclaredOperationFacts.Empty)),
                [handler],
                ""
            )
        );
    }
}
