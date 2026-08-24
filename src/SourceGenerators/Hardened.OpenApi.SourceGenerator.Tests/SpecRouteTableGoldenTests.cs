using System.Collections.Immutable;
using CSharpAuthor;
using Hardened.Idl.Models;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.Shared;
using Xunit;

namespace Hardened.OpenApi.SourceGenerator.Tests;

/// <summary>
/// The spec table's emitted C# over the same route corpus the attribute-routed table records.
///
/// <para>
/// These fixtures exist to be compared against the attribute-routed ones, not to be admired on
/// their own: every difference between the two sets is either a configuration value this work
/// keeps, or a defect this work removes. Once the two generators are one, the comparison is
/// trivially true and these fixtures collapse into the attribute-routed set.
/// </para>
/// </summary>
public class SpecRouteTableGoldenTests {
    /// <summary>
    /// Set <c>HARDENED_RECORD_FIXTURES=1</c> to rewrite every fixture from current output.
    /// </summary>
    /// <remarks>
    /// An environment variable rather than a constant in this file. A constant makes the recording
    /// branch provably unreachable, which is CS0162 and therefore a build error under
    /// ContinuousIntegrationBuild - and, worse, it can be committed as true, which turns every
    /// assertion below into a no-op silently. CI never sets the variable, so recording cannot reach it.
    /// </remarks>
    private static readonly bool Recording =
        Environment.GetEnvironmentVariable("HARDENED_RECORD_FIXTURES") == "1";

    private static string FixtureDirectory() {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null &&
               !File.Exists(Path.Combine(directory.FullName, "Hardened.OpenApi.SourceGenerator.Tests.csproj"))) {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);

        return Path.Combine(directory!.FullName, "Fixtures", "SpecRouteTable");
    }

    [Theory]
    [MemberData(nameof(Corpus))]
    public void SpecRouteTable_OutputIsByteIdentical(string scenario) {
        var (appModel, handlers) = SpecRouteCorpus.Build(scenario);

        var generated = SpecRoutingTableGenerator.GenerateCSharpRouteFile(
            appModel, handlers, ImmutableArray<HandlerInfo?>.Empty,
            ImmutableArray<SpecRegistration>.Empty, CancellationToken.None);

        var path = Path.Combine(FixtureDirectory(), scenario + ".cs");

        if (Recording) {
            Directory.CreateDirectory(FixtureDirectory());
            File.WriteAllText(path, generated);
            return;
        }

        Assert.True(File.Exists(path), $"No fixture for '{scenario}' at {path}.");
        Assert.Equal(File.ReadAllText(path), generated);
    }

    public static TheoryData<string> Corpus() {
        var data = new TheoryData<string>();

        foreach (var scenario in SpecRouteCorpus.Scenarios) {
            data.Add(scenario);
        }

        return data;
    }
}

/// <summary>
/// The same shapes as the attribute-routed corpus. Duplicated for the length of the merge only —
/// the two test projects cannot reference both generators at once, because the shared generator
/// source is compiled into each of them and referencing both makes every shared type ambiguous
/// (CS0433). One generator means one corpus.
/// </summary>
public static class SpecRouteCorpus {
    public static readonly string[] Scenarios = [
        "literal-paths",
        "single-token",
        "typed-constraints",
        "nested-wildcards",
        "catch-all",
        "overlapping-literal-and-token",
        "verb-set-on-one-path",
        "case-insensitive",
        "header-dispatch"
    ];

    public static (EntryPointSelector.Model, IReadOnlyList<RequestHandlerModel>) Build(string scenario) =>
        scenario switch {
            "literal-paths" => (App(), [
                Handler("/pets", "GET", "ListPets"),
                Handler("/pets/featured", "GET", "Featured"),
                Handler("/store", "GET", "Store")
            ]),
            "single-token" => (App(), [Handler("/pets/{petId}", "GET", "GetPet")]),
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
            "catch-all" => (App(), [Handler("/files/{*path}", "GET", "GetFile")]),
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
        Model(new RequestHandlerNameModel(path, method), handlerName);

    private static RequestHandlerModel Dispatched(string header, string key, string handlerName) =>
        Model(new RequestHandlerNameModel("/", "POST", header, key), handlerName);

    private static RequestHandlerModel Model(RequestHandlerNameModel name, string handlerName) =>
        new(name,
            TypeDefinition.Get("Test.Api.Services", "IPetService"),
            handlerName,
            TypeDefinition.Get("Test.Api.Generated", "PetController_" + handlerName),
            Array.Empty<RequestParameterInformation>(),
            new ResponseInformationModel { IsAsync = true },
            Array.Empty<AttributeModel>());
}
