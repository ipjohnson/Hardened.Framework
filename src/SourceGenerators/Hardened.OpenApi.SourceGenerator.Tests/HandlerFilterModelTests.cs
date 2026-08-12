using CSharpAuthor;
using Hardened.OpenApi.SourceGenerator.Models;
using Hardened.SourceGenerator.Shared;
using Hardened.SourceGeneration.Testing;
using Xunit;

namespace Hardened.OpenApi.SourceGenerator.Tests;

/// <summary>
/// <see cref="HandlerInfo"/> is what a <c>[Handler]</c> class contributes to generation: the
/// implementation type, the generated interface it implements, and the filter attributes written on
/// the class and on its methods.
///
/// <para>
/// Unlike the spec models it is built from C# syntax rather than from a document, so it changes
/// whenever the consumer edits their handler. Roslyn compares it on every keystroke in that file,
/// and a comparison that missed a filter would leave the previously generated handler — without the
/// filter — in place.
/// </para>
/// </summary>
public class HandlerFilterModelTests {

    private static readonly ITypeDefinition Implementation = TypeDefinition.Get("TestNamespace", "PetServiceImpl");
    private static readonly ITypeDefinition Interface = TypeDefinition.Get("TestNamespace.Services", "IPetService");

    private static AttributeModel Filter(string name, string arguments = "", string properties = "") =>
        new(TypeDefinition.Get("TestNamespace", name), arguments, properties);

    private static HandlerInfo Handler(
        IReadOnlyList<AttributeModel>? classFilters = null,
        IReadOnlyList<HandlerMethodFilterInfo>? methodFilters = null,
        ITypeDefinition? implementation = null,
        ITypeDefinition? contract = null) =>
        new(implementation ?? Implementation,
            contract ?? Interface,
            classFilters ?? [],
            methodFilters ?? []);

    // ── the handler itself ────────────────────────────────────────────────────────────────────

    [Fact]
    public void TwoHandlersDescribingTheSameClassCompareEqual() {
        Assert.Equal(
            Handler([Filter("AuditAttribute")], [new HandlerMethodFilterInfo("CreatePet", [Filter("AuthorizeAttribute")])]),
            Handler([Filter("AuditAttribute")], [new HandlerMethodFilterInfo("CreatePet", [Filter("AuthorizeAttribute")])]));
    }

    [Fact]
    public void EqualHandlersHashEqual() {
        Assert.Equal(
            Handler([Filter("AuditAttribute")]).GetHashCode(),
            Handler([Filter("AuditAttribute")]).GetHashCode());
    }

    [Fact]
    public void AHandlerEqualsItself() {
        var handler = Handler();

        Assert.True(handler.Equals(handler));
    }

    [Fact]
    public void AHandlerIsNotEqualToNull() {
        Assert.False(Handler().Equals(null));
        Assert.False(Handler().Equals((object?)null));
    }

    [Fact]
    public void AHandlerIsNotEqualToSomethingElseEntirely() {
        Assert.False(Handler().Equals("PetServiceImpl"));
    }

    /// <summary>
    /// The implementation type is what the routing table registers the interface against, so two
    /// classes implementing the same interface are two different handlers.
    /// </summary>
    [Fact]
    public void TwoClassesImplementingTheSameInterfaceAreDifferentHandlers() {
        Assert.NotEqual(
            Handler(implementation: TypeDefinition.Get("TestNamespace", "PetServiceImpl")),
            Handler(implementation: TypeDefinition.Get("TestNamespace", "OtherPetService")));
    }

    /// <summary>
    /// The interface is what matches a handler to the operations generated from a tag. Changing it
    /// moves every filter the class carries onto a different set of routes.
    /// </summary>
    [Fact]
    public void ChangingTheImplementedInterfaceMakesADifferentHandler() {
        Assert.NotEqual(
            Handler(contract: TypeDefinition.Get("TestNamespace.Services", "IPetService")),
            Handler(contract: TypeDefinition.Get("TestNamespace.Services", "IStoreService")));
    }

    [Fact]
    public void AddingAClassFilterMakesADifferentHandler() {
        Assert.NotEqual(Handler(), Handler([Filter("AuditAttribute")]));
    }

    [Fact]
    public void ChangingAClassFiltersTypeMakesADifferentHandler() {
        Assert.NotEqual(Handler([Filter("AuditAttribute")]), Handler([Filter("AuthorizeAttribute")]));
    }

    /// <summary>
    /// An argument written on a filter attribute is emitted verbatim into the generated metadata, so
    /// two otherwise identical attributes with different arguments are different filters.
    /// </summary>
    [Fact]
    public void ChangingAFiltersArgumentsMakesADifferentHandler() {
        Assert.NotEqual(
            Handler([Filter("AuditAttribute", arguments: "\"read\"")]),
            Handler([Filter("AuditAttribute", arguments: "\"write\"")]));
    }

    [Fact]
    public void AddingAMethodFilterMakesADifferentHandler() {
        Assert.NotEqual(
            Handler(),
            Handler(methodFilters: [new HandlerMethodFilterInfo("CreatePet", [Filter("AuthorizeAttribute")])]));
    }

    /// <summary>
    /// Which method a filter is on decides which single route it applies to, so moving it is a real
    /// change even though the set of filters is unchanged.
    /// </summary>
    [Fact]
    public void MovingAFilterToAnotherMethodMakesADifferentHandler() {
        Assert.NotEqual(
            Handler(methodFilters: [new HandlerMethodFilterInfo("CreatePet", [Filter("AuthorizeAttribute")])]),
            Handler(methodFilters: [new HandlerMethodFilterInfo("DeletePet", [Filter("AuthorizeAttribute")])]));
    }

    [Fact]
    public void AddingASecondMethodFilterMakesADifferentHandler() {
        Assert.NotEqual(
            Handler(methodFilters: [new HandlerMethodFilterInfo("CreatePet", [Filter("AuthorizeAttribute")])]),
            Handler(methodFilters: [
                new HandlerMethodFilterInfo("CreatePet", [Filter("AuthorizeAttribute")]),
                new HandlerMethodFilterInfo("DeletePet", [Filter("AuthorizeAttribute")])
            ]));
    }

    // ── the per-method record ─────────────────────────────────────────────────────────────────

    [Fact]
    public void TwoMethodFilterSetsForTheSameMethodCompareEqual() {
        Assert.Equal(
            new HandlerMethodFilterInfo("CreatePet", [Filter("AuthorizeAttribute")]),
            new HandlerMethodFilterInfo("CreatePet", [Filter("AuthorizeAttribute")]));
    }

    [Fact]
    public void EqualMethodFilterSetsHashEqual() {
        Assert.Equal(
            new HandlerMethodFilterInfo("CreatePet", [Filter("AuthorizeAttribute")]).GetHashCode(),
            new HandlerMethodFilterInfo("CreatePet", [Filter("AuthorizeAttribute")]).GetHashCode());
    }

    [Fact]
    public void AMethodFilterSetEqualsItself() {
        var filters = new HandlerMethodFilterInfo("CreatePet", []);

        Assert.True(filters.Equals(filters));
    }

    [Fact]
    public void AMethodFilterSetIsNotEqualToNull() {
        Assert.False(new HandlerMethodFilterInfo("CreatePet", []).Equals(null));
        Assert.False(new HandlerMethodFilterInfo("CreatePet", []).Equals((object?)null));
    }

    [Fact]
    public void AMethodFilterSetIsNotEqualToSomethingElseEntirely() {
        Assert.False(new HandlerMethodFilterInfo("CreatePet", []).Equals("CreatePet"));
    }

    [Fact]
    public void ChangingTheFiltersOnAMethodMakesADifferentSet() {
        Assert.NotEqual(
            new HandlerMethodFilterInfo("CreatePet", [Filter("AuthorizeAttribute")]),
            new HandlerMethodFilterInfo("CreatePet", [Filter("AuditAttribute")]));
    }

    // ── the same thing, read out of real C# ───────────────────────────────────────────────────

    /// <summary>
    /// A filter written on a handler method reaches the generated handler's metadata. This is the
    /// only path that puts a <see cref="HandlerMethodFilterInfo"/> in the model at all — the
    /// class-level path is separate — and it has to survive the generated code compiling.
    /// </summary>
    [Fact]
    public void AFilterOnAHandlerMethodReachesTheGeneratedHandler() {
        var result = OpenApiGenerator.Run(Specs.Minimal, HandlerWithAMethodFilter)
            .AssertGenerated("PetController_ListPets")
            .AssertNoErrors();

        Assert.Contains("AuditAttribute", result.SourceContaining("PetController_ListPets"));
    }

    /// <summary>
    /// A filter written on the handler class reaches every operation the interface carries, not just
    /// one of them.
    /// </summary>
    [Fact]
    public void AFilterOnTheHandlerClassReachesEveryGeneratedHandlerForThatInterface() {
        var result = OpenApiGenerator.Run(Specs.EveryVerb, HandlerWithAClassFilter)
            .AssertGenerated(
                "ItemController_ListItems",
                "ItemController_CreateItem",
                "ItemController_GetItem",
                "ItemController_ReplaceItem",
                "ItemController_UpdateItem",
                "ItemController_DeleteItem")
            .AssertNoErrors();

        foreach (var hintName in result.HintNamesExceptDiagnostic()
                     .Where(name => name.StartsWith("ItemController_", StringComparison.Ordinal))) {
            Assert.Contains("AuditAttribute", result.GeneratedSources[hintName]);
        }
    }

    /// <summary>
    /// Editing a comment inside the handler cannot change what is generated. Roslyn reaches that
    /// answer by comparing <see cref="HandlerInfo"/> values, so this is the equality above running
    /// in the pipeline it exists for.
    /// </summary>
    [Fact]
    public void AnEditInsideTheHandlerThatChangesNoFilterRegeneratesIdenticalOutput() {
        var result = GeneratorTestHarness.RunIncremental(
            new Dictionary<string, string> { ["Test.cs"] = HandlerWithAMethodFilter },
            new Dictionary<string, string> {
                ["Test.cs"] = HandlerWithAMethodFilter + Environment.NewLine + "// unrelated"
            },
            [new OpenApiSourceGenerator()],
            null,
            new Dictionary<string, string> { ["petstore.yaml"] = Specs.Minimal });

        Assert.NotEmpty(result.FirstRun);
        Assert.Equal(
            result.FirstRun.OrderBy(pair => pair.Key, StringComparer.Ordinal),
            result.SecondRun.OrderBy(pair => pair.Key, StringComparer.Ordinal));
    }

    private const string HandlerWithAMethodFilter =
        """
        using System;
        using System.Collections.Generic;
        using System.Threading.Tasks;
        using Hardened.Requests.Abstract.Attributes;
        using Hardened.Shared.Runtime.Attributes;
        using TestNamespace.Models;
        using TestNamespace.Services;

        namespace TestNamespace;

        [HardenedModule]
        public partial class TestApp {
        }

        [AttributeUsage(AttributeTargets.All)]
        public class AuditAttribute : Attribute {
        }

        [Handler]
        public class PetServiceImpl : IPetService {
            [Audit]
            public Task<Pet> ListPets() => Task.FromResult(new Pet("1"));
        }
        """;

    private const string HandlerWithAClassFilter =
        """
        using System;
        using System.Collections.Generic;
        using System.Threading.Tasks;
        using Hardened.Requests.Abstract.Attributes;
        using Hardened.Shared.Runtime.Attributes;
        using TestNamespace.Models;
        using TestNamespace.Services;

        namespace TestNamespace;

        [HardenedModule]
        public partial class TestApp {
        }

        [AttributeUsage(AttributeTargets.All)]
        public class AuditAttribute : Attribute {
        }

        [Handler]
        [Audit]
        public class ItemServiceImpl : IItemService {
            public Task ListItems() => Task.CompletedTask;
            public Task CreateItem() => Task.CompletedTask;
            public Task GetItem(string itemId) => Task.CompletedTask;
            public Task ReplaceItem(string itemId) => Task.CompletedTask;
            public Task UpdateItem(string itemId) => Task.CompletedTask;
            public Task DeleteItem(string itemId) => Task.CompletedTask;
        }
        """;
}
