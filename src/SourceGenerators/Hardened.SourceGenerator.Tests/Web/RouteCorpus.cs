using CSharpAuthor;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.Shared;

namespace Hardened.SourceGenerator.Tests.Web;

/// <summary>
/// One set of route shapes, shared by the golden guard and the conformance suite.
///
/// <para>
/// Every scenario names a routing behaviour rather than an application: the point is coverage of
/// the tree walk — literals, tokens, typed constraints, nested wildcards, catch-all, overlap,
/// verb sets, and the two entry-point attributes that change matching.
/// </para>
/// </summary>
public static class RouteCorpus {
    public static readonly string[] Scenarios = [
        "literal-paths",
        "single-token",
        "typed-constraints",
        "nested-wildcards",
        "catch-all",
        "overlapping-literal-and-token",
        "verb-set-on-one-path",
        "case-insensitive",
        "base-path",
        "header-dispatch"
    ];

    public static (EntryPointSelector.Model, IReadOnlyList<RequestHandlerModel>) Build(string scenario) =>
        scenario switch {
            "literal-paths" => (App(), [
                Handler("/pets", "GET", "ListPets"),
                Handler("/pets/featured", "GET", "Featured"),
                Handler("/store", "GET", "Store")
            ]),

            "single-token" => (App(), [
                Handler("/pets/{petId}", "GET", "GetPet")
            ]),

            "typed-constraints" => (App(), [
                Handler("/items/{id:int}", "GET", "GetItem"),
                Handler("/price/{value:decimal}", "GET", "GetPrice"),
                Handler("/key/{key:guid}", "GET", "GetByKey"),
                Handler("/flag/{on:bool}", "GET", "GetFlag")
            ]),

            "nested-wildcards" => (App(), [
                Handler("/a/{x}/b/{y}", "GET", "TwoTokens"),
                Handler("/a/{x}/b/{y}/c/{z}", "GET", "ThreeTokens")
            ]),

            // Catch-all is attribute-routed only today. It is in the corpus because the spec path
            // gains it in this work, and the fixture is what proves the web path did not change
            // while that happened.
            "catch-all" => (App(), [
                Handler("/files/{*path}", "GET", "GetFile")
            ]),

            "overlapping-literal-and-token" => (App(), [
                Handler("/pets/{petId}", "GET", "GetPet"),
                Handler("/pets/special", "GET", "Special")
            ]),

            "verb-set-on-one-path" => (App(), [
                Handler("/pets/{petId}", "GET", "GetPet"),
                Handler("/pets/{petId}", "PUT", "UpdatePet"),
                Handler("/pets/{petId}", "DELETE", "DeletePet")
            ]),

            "case-insensitive" => (App(Attribute("CaseInsensitiveRoutesAttribute")), [
                Handler("/Pets/{PetId}", "GET", "GetPet")
            ]),

            "base-path" => (App(Attribute("BasePathAttribute", "\"/api\"")), [
                Handler("/pets", "GET", "ListPets"),
                Handler("/pets/{petId}", "GET", "GetPet")
            ]),

            // The AWS JSON protocol shape: the operation is selected by header value, not by path.
            // Emitted only by the spec table today; the port brings it here.
            "header-dispatch" => (App(), [
                Dispatched("X-Amz-Target", "Bank.GetBalance", "GetBalance"),
                Dispatched("X-Amz-Target", "Bank.Transfer", "Transfer"),
                Handler("/health", "GET", "Health")
            ]),

            _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "Unknown scenario.")
        };

    private static EntryPointSelector.Model App(params AttributeModel[] attributes) =>
        new() {
            EntryPointType = TypeDefinition.Get("Test.Api", "TestApp"),
            AttributeModels = attributes,
            RootEntryPoint = true,
            MethodDefinitions = Array.Empty<HardenedMethodDefinition>()
        };

    private static AttributeModel Attribute(string name, string arguments = "") =>
        new(TypeDefinition.Get("Hardened.Web.Runtime.Attributes", name), arguments, "");

    private static RequestHandlerModel Handler(string path, string method, string handlerName) =>
        Build(new RequestHandlerNameModel(path, method), handlerName);

    private static RequestHandlerModel Dispatched(string header, string key, string handlerName) =>
        Build(new RequestHandlerNameModel("/", "POST", header, key), handlerName);

    private static RequestHandlerModel Build(RequestHandlerNameModel name, string handlerName) =>
        new(name,
            TypeDefinition.Get("Test.Api.Services", "IPetService"),
            handlerName,
            TypeDefinition.Get("Test.Api.Generated", "PetController_" + handlerName),
            Array.Empty<RequestParameterInformation>(),
            new ResponseInformationModel { IsAsync = true },
            Array.Empty<AttributeModel>());
}
