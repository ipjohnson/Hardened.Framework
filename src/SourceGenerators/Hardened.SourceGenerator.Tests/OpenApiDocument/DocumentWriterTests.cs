using System.Text.Json;
using CSharpAuthor;
using Hardened.Generation.Models;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.OpenApiDocument;
using Hardened.SourceGenerator.Requests;
using Hardened.SourceGenerator.Shared;
using Xunit;
using Hardened.Web.Runtime.Responses;

namespace Hardened.SourceGenerator.Tests.OpenApiDocument;

/// <summary>
/// The document writer over the model shapes only a described front end supplies, driven
/// directly.
/// </summary>
/// <remarks>
/// <para>
/// This file is compiled into both this suite and <c>Hardened.Web.SourceGenerator.Tests</c>, the
/// way the SpecBridge tests are, because each generator wrapper compiles its own copy of the
/// writer and a test in one assembly covers that assembly's copy and nothing else. The emission
/// tests beside this file drive the writer through real source, which reaches only what
/// code-first can declare; the declared-facts branches - SpecParameter facets, security, headers,
/// the identity - exist in every copy and are otherwise exercised only by the IDL suite's.
/// </para>
/// </remarks>
public class DocumentWriterTests {

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
        RequestParameterInformation? parameter = null,
        IReadOnlyList<ResponseSchemaModel>? responses = null,
        IReadOnlyList<string>? security = null,
        string path = "/todos/{id}") =>
        new(
            new RequestHandlerNameModel(path, "GET"),
            Type("TodoController"),
            "GetTodo",
            TypeDefinition.Get("TestApp.Generated", "TodoController_GetTodo"),
            parameter == null ? [] : [parameter],
            new ResponseInformationModel { ReturnType = Type("Todo") },
            []) {
            ResponseSchema = Schema("Todo"),
            ResponseSchemas = responses ?? System.Array.Empty<ResponseSchemaModel>(),
            SecurityRequirements = security ?? System.Array.Empty<string>()
        };

    private static JsonElement Write(
        RequestHandlerModel handler,
        OpenApiVersion version = OpenApiVersionFacts.Default,
        DocumentIdentity? identity = null) =>
        JsonDocument.Parse(
            OpenApiDocumentGenerator.Write(EntryPoint(), [handler], "", version, identity))
            .RootElement;

    private static JsonElement LimitSchema(JsonElement document) {
        foreach (var parameter in document
                     .GetProperty("paths").GetProperty("/todos/{id}").GetProperty("get")
                     .GetProperty("parameters").EnumerateArray()) {
            if (parameter.GetProperty("name").GetString() == "limit") {
                return parameter.GetProperty("schema");
            }
        }

        throw new Xunit.Sdk.XunitException("No limit parameter in the document.");
    }

    private static RequestParameterInformation Bound(ParameterModel spec) =>
        new(
            TypeDefinition.Get("System", "String"), "limit", false, null,
            ParameterBindType.QueryString, "limit", 0) {
            SpecParameter = spec
        };

    #region declared parameter facts

    [Fact]
    public void TheDeclaredFacetsWinOverTheCSharpType() {
        var schema = LimitSchema(Write(Handler(Bound(new ParameterModel {
            Name = "limit", In = "query", Type = "integer", Format = "int32",
            Minimum = 1, Maximum = 100, Default = "20",
            MinLength = 1, MaxLength = 10, Pattern = "^[0-9]+$"
        }))));

        Assert.Equal("integer", schema.GetProperty("type").GetString());
        Assert.Equal(1, schema.GetProperty("minimum").GetDecimal());
        Assert.Equal(100, schema.GetProperty("maximum").GetDecimal());
        Assert.Equal(20, schema.GetProperty("default").GetDecimal());
        Assert.Equal("^[0-9]+$", schema.GetProperty("pattern").GetString());
    }

    [Fact]
    public void ExclusiveBoundsSpellPerVersion() {
        var spec = new ParameterModel {
            Name = "limit", In = "query", Type = "number",
            Minimum = 0, ExclusiveMinimum = true, Maximum = 1, ExclusiveMaximum = true
        };

        var modern = LimitSchema(Write(Handler(Bound(spec))));

        Assert.Equal(0, modern.GetProperty("exclusiveMinimum").GetDecimal());
        Assert.Equal(1, modern.GetProperty("exclusiveMaximum").GetDecimal());

        var legacy = LimitSchema(Write(Handler(Bound(spec)), OpenApiVersion.V3_0));

        Assert.Equal(0, legacy.GetProperty("minimum").GetDecimal());
        Assert.True(legacy.GetProperty("exclusiveMinimum").GetBoolean());
        Assert.True(legacy.GetProperty("exclusiveMaximum").GetBoolean());
    }

    [Fact]
    public void ADeclaredArrayKeepsItsItemAndBounds() {
        var schema = LimitSchema(Write(Handler(Bound(new ParameterModel {
            Name = "limit", In = "query",
            IsArray = true, ArrayItemsType = "string", MinItems = 1, MaxItems = 5
        }))));

        Assert.Equal("array", schema.GetProperty("type").GetString());
        Assert.Equal("string", schema.GetProperty("items").GetProperty("type").GetString());
        Assert.Equal(1, schema.GetProperty("minItems").GetInt32());
        Assert.Equal(5, schema.GetProperty("maxItems").GetInt32());
    }

    /// <summary>The non-numeric default spellings: booleans stay bare, prose stays quoted.</summary>
    [Fact]
    public void DefaultsAreTypedByTheirSchema() {
        var flag = LimitSchema(Write(Handler(Bound(new ParameterModel {
            Name = "limit", In = "query", Type = "boolean", Default = "true"
        }))));

        Assert.True(flag.GetProperty("default").GetBoolean());

        var text = LimitSchema(Write(Handler(Bound(new ParameterModel {
            Name = "limit", In = "query", Type = "string", Default = "compact"
        }))));

        Assert.Equal("compact", text.GetProperty("default").GetString());

        // A default that does not parse as its declared type is a string rather than an invalid
        // document.
        var odd = LimitSchema(Write(Handler(Bound(new ParameterModel {
            Name = "limit", In = "query", Type = "integer", Default = "lots"
        }))));

        Assert.Equal("lots", odd.GetProperty("default").GetString());
    }

    #endregion

    #region identity, security, headers, validation

    [Fact]
    public void TheIdentityIsWrittenAndTheSchemesDeclared() {
        var document = Write(
            Handler(security: ["{\"BearerAuth\":[]}"]),
            identity: new DocumentIdentity(
                "Todos API", "2.0.0", "The todos.",
                [("BearerAuth", "{\"type\":\"http\",\"scheme\":\"bearer\"}")]));

        var info = document.GetProperty("info");

        Assert.Equal("Todos API", info.GetProperty("title").GetString());
        Assert.Equal("2.0.0", info.GetProperty("version").GetString());
        Assert.Equal("The todos.", info.GetProperty("description").GetString());

        Assert.Equal(
            "bearer",
            document.GetProperty("components").GetProperty("securitySchemes")
                .GetProperty("BearerAuth").GetProperty("scheme").GetString());

        var operation = document
            .GetProperty("paths").GetProperty("/todos/{id}").GetProperty("get");
        var requirement = Assert.Single(operation.GetProperty("security").EnumerateArray());

        Assert.Equal(0, requirement.GetProperty("BearerAuth").GetArrayLength());
    }

    [Fact]
    public void DeclaredHeadersAreMergedByWireName() {
        var headers = new[] {
            new ResponseHeaderModel { Name = "Location", ParameterName = "Location", Description = "Where." },
            new ResponseHeaderModel { Name = "location", ParameterName = "Location2" }
        };

        var handler = Handler(responses: [
            new ResponseSchemaModel(201, "Created.", Schema("Todo")) { Headers = headers }
        ]);

        var created = Write(handler)
            .GetProperty("paths").GetProperty("/todos/{id}").GetProperty("get")
            .GetProperty("responses").GetProperty("201");

        var location = Assert.Single(created.GetProperty("headers").EnumerateObject());

        Assert.Equal("Location", location.Name);
        Assert.Equal("Where.", location.Value.GetProperty("description").GetString());
    }

    [Fact]
    public void AValidatedHandlerDeclaresTheFourHundred() {
        var handler = Handler();

        handler.HasGeneratedValidation = true;

        var responses = Write(handler)
            .GetProperty("paths").GetProperty("/todos/{id}").GetProperty("get")
            .GetProperty("responses");

        Assert.Equal(
            "#/components/schemas/RequestValidationError",
            responses.GetProperty("400").GetProperty("content")
                .GetProperty("application/json").GetProperty("schema")
                .GetProperty("$ref").GetString());
    }

    /// <summary>A handler that declared its own 400 keeps it; the generated one yields.</summary>
    [Fact]
    public void ADeclaredFourHundredWins() {
        var handler = Handler(responses: [
            new ResponseSchemaModel(400, "My own.", Schema("Problem"))
        ]);

        handler.HasGeneratedValidation = true;

        var responses = Write(handler)
            .GetProperty("paths").GetProperty("/todos/{id}").GetProperty("get")
            .GetProperty("responses");

        Assert.Equal("My own.", responses.GetProperty("400").GetProperty("description").GetString());
    }

    #endregion

    #region what the router already refused

    private static RequestParameterInformation PathToken(string type = "Int32") =>
        new(TypeDefinition.Get("System", type), "id", true, null, ParameterBindType.Path, "id", 0);

    private static JsonElement Responses(RequestHandlerModel handler) =>
        Write(handler)
            .GetProperty("paths").GetProperty("/todos/{id}").GetProperty("get")
            .GetProperty("responses");

    /// <summary>
    /// The router decides before the binder, so a value the converter would have refused is a 404
    /// and never reaches binding. Both writers used to run: the 404 explained the refusal and the
    /// 400 claimed one the same operation could not produce.
    /// </summary>
    [Fact]
    public void AConstrainedTokenGuaranteeingTheConversionDeclaresNoFourHundred() {
        var responses = Responses(Handler(PathToken(), path: "/todos/{id:int}"));

        Assert.False(responses.TryGetProperty("400", out _));
        Assert.True(responses.TryGetProperty("404", out _));
    }

    /// <summary>The same handler with nothing guarding the token: the 400 is real.</summary>
    [Fact]
    public void AnUnconstrainedTokenStillDeclaresTheFourHundred() {
        Assert.True(Responses(Handler(PathToken())).TryGetProperty("400", out _));
    }

    /// <summary>
    /// A constraint wider than the type still lets a refusal through - <c>{id:long}</c> admits
    /// 99999999999, which an <c>int</c> parameter refuses - so the 400 stays.
    /// </summary>
    [Fact]
    public void AConstraintWiderThanTheTypeStillDeclaresTheFourHundred() {
        Assert.True(
            Responses(Handler(PathToken(), path: "/todos/{id:long}")).TryGetProperty("400", out _));
    }

    /// <summary>
    /// Two 404s reach the wire and a document can write one, so the one it writes says the other
    /// exists. A client author reading the declared body schema is otherwise told nothing about the
    /// empty body they will also be sent.
    /// </summary>
    [Fact]
    public void ADeclaredNotFoundSaysARouteConstraintAnswersItToo() {
        var handler = Handler(
            responses: [new ResponseSchemaModel(404, "No such todo", Schema("Problem"))],
            path: "/todos/{id:int}");

        Assert.Equal(
            "No such todo. A token that fails its route constraint answers this status too, " +
            "before the handler and with no body.",
            Responses(handler).GetProperty("404").GetProperty("description").GetString());
    }

    /// <summary>The note is for the operations a route constraint can refuse, and no others.</summary>
    [Fact]
    public void ADeclaredNotFoundOnAnUnconstrainedRouteIsLeftAlone() {
        var handler = Handler(responses: [new ResponseSchemaModel(404, "No such todo", Schema("Problem"))]);

        Assert.Equal(
            "No such todo", Responses(handler).GetProperty("404").GetProperty("description").GetString());
    }

    #endregion
}
