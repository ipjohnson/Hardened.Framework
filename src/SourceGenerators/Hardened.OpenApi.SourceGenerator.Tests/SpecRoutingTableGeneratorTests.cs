using System.Collections.Immutable;
using CSharpAuthor;
using Hardened.Generation.Models;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.Shared;
using Xunit;

namespace Hardened.OpenApi.SourceGenerator.Tests;

public class SpecRoutingTableGeneratorTests {
    private static EntryPointSelector.Model CreateAppModel() {
        return new EntryPointSelector.Model {
            EntryPointType = TypeDefinition.Get("Test.Api", "TestApp"),
            AttributeModels = Array.Empty<AttributeModel>(),
            RootEntryPoint = true,
            MethodDefinitions = Array.Empty<HardenedMethodDefinition>()
        };
    }

    private static List<RequestHandlerModel> CreatePetstoreHandlers() {
        var petServiceType = TypeDefinition.Get("Test.Api.Services", "IPetService");
        var storeServiceType = TypeDefinition.Get("Test.Api.Services", "IStoreService");

        return new List<RequestHandlerModel> {
            new(new RequestHandlerNameModel("/pets", "GET"),
                petServiceType, "ListPets",
                TypeDefinition.Get("Test.Api.Generated", "PetController_ListPets"),
                Array.Empty<RequestParameterInformation>(),
                new ResponseInformationModel { IsAsync = true },
                Array.Empty<AttributeModel>()),
            new(new RequestHandlerNameModel("/pets", "POST"),
                petServiceType, "CreatePet",
                TypeDefinition.Get("Test.Api.Generated", "PetController_CreatePet"),
                Array.Empty<RequestParameterInformation>(),
                new ResponseInformationModel { IsAsync = true },
                Array.Empty<AttributeModel>()),
            new(new RequestHandlerNameModel("/pets/{petId}", "GET"),
                petServiceType, "GetPet",
                TypeDefinition.Get("Test.Api.Generated", "PetController_GetPet"),
                Array.Empty<RequestParameterInformation>(),
                new ResponseInformationModel { IsAsync = true },
                Array.Empty<AttributeModel>()),
            new(new RequestHandlerNameModel("/pets/{petId}", "DELETE"),
                petServiceType, "DeletePet",
                TypeDefinition.Get("Test.Api.Generated", "PetController_DeletePet"),
                Array.Empty<RequestParameterInformation>(),
                new ResponseInformationModel { IsAsync = true },
                Array.Empty<AttributeModel>()),
            new(new RequestHandlerNameModel("/stores", "GET"),
                storeServiceType, "ListStores",
                TypeDefinition.Get("Test.Api.Generated", "StoreController_ListStores"),
                Array.Empty<RequestParameterInformation>(),
                new ResponseInformationModel { IsAsync = true },
                Array.Empty<AttributeModel>())
        };
    }

    /// <summary>
    /// An RPC service: every operation at POST /, told apart by a header.
    /// </summary>
    private static List<RequestHandlerModel> CreateDispatchedHandlers() {
        var bankServiceType = TypeDefinition.Get("Test.Api.Services", "IBankService");

        return new List<RequestHandlerModel> {
            new(new RequestHandlerNameModel("/", "POST", "X-Amz-Target", "Bank.GetBalance"),
                bankServiceType, "GetBalance",
                TypeDefinition.Get("Test.Api.Generated", "BankController_GetBalance"),
                Array.Empty<RequestParameterInformation>(),
                new ResponseInformationModel { IsAsync = true },
                Array.Empty<AttributeModel>()),
            new(new RequestHandlerNameModel("/", "POST", "X-Amz-Target", "Bank.Transfer"),
                bankServiceType, "Transfer",
                TypeDefinition.Get("Test.Api.Generated", "BankController_Transfer"),
                Array.Empty<RequestParameterInformation>(),
                new ResponseInformationModel { IsAsync = true },
                Array.Empty<AttributeModel>())
        };
    }

    /// <summary>
    /// A dispatched handler is selected by an exact token, so the table is a switch over string
    /// literals rather than a route tree - no span slicing, no wildcard node, no verb fallback.
    /// </summary>
    [Fact]
    public void GenerateCSharpRouteFile_DispatchesOnTheDeclaredHeader() {
        var result = SpecRoutingTableGenerator.GenerateCSharpRouteFile(
            CreateAppModel(), CreateDispatchedHandlers(), ImmutableArray<HandlerInfo?>.Empty,
            ImmutableArray<SpecRegistration>.Empty, CancellationToken.None);

        Assert.Contains("Headers.TryGetValue(\"X-Amz-Target\"", result);
        Assert.Contains("case \"Bank.GetBalance\":", result);
        Assert.Contains("case \"Bank.Transfer\":", result);
    }

    /// <summary>
    /// With nothing left to route, the table does not build a tree over an empty list - it says so
    /// and returns.
    /// </summary>
    [Fact]
    public void GenerateCSharpRouteFile_EmitsNoRouteTreeWhenEveryHandlerIsDispatched() {
        var result = SpecRoutingTableGenerator.GenerateCSharpRouteFile(
            CreateAppModel(), CreateDispatchedHandlers(), ImmutableArray<HandlerInfo?>.Empty,
            ImmutableArray<SpecRegistration>.Empty, CancellationToken.None);

        Assert.DoesNotContain("pathSpan", result);
    }

    /// <summary>
    /// Both kinds in one application, with header dispatch consulted first.
    /// </summary>
    /// <remarks>
    /// The order is the point. An awsJson service sends every operation to POST /, so a path tree
    /// consulted first would match that route for whichever handler happened to own it and answer
    /// the wrong one for all the others.
    /// </remarks>
    [Fact]
    public void GenerateCSharpRouteFile_ChecksDispatchBeforeRouting() {
        var handlers = new List<RequestHandlerModel>(CreateDispatchedHandlers());

        handlers.AddRange(CreatePetstoreHandlers());

        var result = SpecRoutingTableGenerator.GenerateCSharpRouteFile(
            CreateAppModel(), handlers, ImmutableArray<HandlerInfo?>.Empty,
            ImmutableArray<SpecRegistration>.Empty, CancellationToken.None);

        var dispatch = result.IndexOf("X-Amz-Target", StringComparison.Ordinal);
        var routing = result.IndexOf("pathSpan", StringComparison.Ordinal);

        Assert.True(dispatch >= 0, "the dispatch table was not emitted");
        Assert.True(routing >= 0, "the route tree was not emitted");
        Assert.True(dispatch < routing, "the route tree is consulted before the dispatch table");
    }

    [Fact]
    public void GenerateCSharpRouteFile_ProducesCompilableCSharp() {
        var appModel = CreateAppModel();
        var handlers = CreatePetstoreHandlers();

        var result = SpecRoutingTableGenerator.GenerateCSharpRouteFile(
            appModel, handlers, ImmutableArray<HandlerInfo?>.Empty, ImmutableArray<SpecRegistration>.Empty, CancellationToken.None);

        Assert.Contains("partial class TestApp", result);
        Assert.Contains("SpecRoutingTable", result);
        Assert.Contains("IWebExecutionRequestHandlerProvider", result);
        Assert.Contains("GetExecutionRequestHandler", result);
    }

    [Fact]
    public void GenerateCSharpRouteFile_DoesNotContainStandaloneControllerRegistration() {
        var appModel = CreateAppModel();
        var handlers = CreatePetstoreHandlers();

        var result = SpecRoutingTableGenerator.GenerateCSharpRouteFile(
            appModel, handlers, ImmutableArray<HandlerInfo?>.Empty, ImmutableArray<SpecRegistration>.Empty, CancellationToken.None);

        // Without [Handler] implementations, there should be no standalone AddTransient for interfaces
        Assert.DoesNotMatch(@"AddTransient<\s*IPetService\s*>\s*\(\s*\)", result);
        Assert.DoesNotMatch(@"AddTransient<\s*IStoreService\s*>\s*\(\s*\)", result);
    }

    [Fact]
    public void GenerateCSharpRouteFile_WithHandlerInfos_ContainsServiceRegistration() {
        var appModel = CreateAppModel();
        var handlers = CreatePetstoreHandlers();

        var handlerInfo = new HandlerInfo(
            TypeDefinition.Get("Test.Api", "PetServiceImpl"),
            TypeDefinition.Get("Test.Api.Services", "IPetService"),
            Array.Empty<AttributeModel>(),
            Array.Empty<HandlerMethodFilterInfo>());

        var handlerInfos = ImmutableArray.Create<HandlerInfo?>(handlerInfo);

        var result = SpecRoutingTableGenerator.GenerateCSharpRouteFile(
            appModel, handlers, handlerInfos, ImmutableArray<SpecRegistration>.Empty, CancellationToken.None);

        Assert.Matches(@"AddTransient<[^>]*IPetService[^>]*,[^>]*PetServiceImpl[^>]*>", result);
    }

    /// <summary>
    /// A handler declaring a base class registers against its service interface, not the base.
    /// </summary>
    /// <remarks>
    /// C# requires a base class to be written first, and the selector took the first base-list entry
    /// and called it the interface. So <c>class PetServiceImpl : HandlerBase, IPetService</c>
    /// registered <c>HandlerBase</c>, nothing implemented <c>IPetService</c>, the build stayed clean
    /// and every route on that service failed at request time.
    /// </remarks>
    [Fact]
    public void GenerateCSharpRouteFile_RegistersTheServiceInterfaceRatherThanAPrecedingBaseClass() {
        var appModel = CreateAppModel();
        var handlers = CreatePetstoreHandlers();

        var handlerInfo = new HandlerInfo(
            TypeDefinition.Get("Test.Api", "PetServiceImpl"),
            new ITypeDefinition[] {
                TypeDefinition.Get("Test.Api", "HandlerBase"),
                TypeDefinition.Get("Test.Api.Services", "IPetService")
            },
            Array.Empty<AttributeModel>(),
            Array.Empty<HandlerMethodFilterInfo>());

        var result = SpecRoutingTableGenerator.GenerateCSharpRouteFile(
            appModel, handlers, ImmutableArray.Create<HandlerInfo?>(handlerInfo),
            ImmutableArray<SpecRegistration>.Empty, CancellationToken.None);

        Assert.Matches(@"AddTransient<[^>]*IPetService[^>]*,[^>]*PetServiceImpl[^>]*>", result);
        Assert.DoesNotMatch(@"AddTransient<[^>]*HandlerBase[^>]*,", result);
    }

    /// <summary>
    /// A handler naming no described service still registers against its first base-list entry,
    /// which is what it did before any of this. HOAG031 reports it; the registration is unchanged.
    /// </summary>
    [Fact]
    public void GenerateCSharpRouteFile_FallsBackToTheFirstBaseTypeWhenNoneIsADescribedService() {
        var appModel = CreateAppModel();
        var handlers = CreatePetstoreHandlers();

        var handlerInfo = new HandlerInfo(
            TypeDefinition.Get("Test.Api", "OrphanImpl"),
            new ITypeDefinition[] {
                TypeDefinition.Get("Test.Api", "ISomethingElse"),
                TypeDefinition.Get("Test.Api", "IAlsoNotDescribed")
            },
            Array.Empty<AttributeModel>(),
            Array.Empty<HandlerMethodFilterInfo>());

        var result = SpecRoutingTableGenerator.GenerateCSharpRouteFile(
            appModel, handlers, ImmutableArray.Create<HandlerInfo?>(handlerInfo),
            ImmutableArray<SpecRegistration>.Empty, CancellationToken.None);

        Assert.Matches(@"AddTransient<[^>]*ISomethingElse[^>]*,[^>]*OrphanImpl[^>]*>", result);
    }

    [Fact]
    public void GenerateCSharpRouteFile_ContainsRoutingForEndpointPaths() {
        var appModel = CreateAppModel();
        var handlers = CreatePetstoreHandlers();

        var result = SpecRoutingTableGenerator.GenerateCSharpRouteFile(
            appModel, handlers, ImmutableArray<HandlerInfo?>.Empty, ImmutableArray<SpecRegistration>.Empty, CancellationToken.None);

        // Routing should reference handler fields for each endpoint
        Assert.Contains("PetController_ListPets", result);
        Assert.Contains("PetController_CreatePet", result);
        Assert.Contains("PetController_GetPet", result);
        Assert.Contains("PetController_DeletePet", result);
        Assert.Contains("StoreController_ListStores", result);
    }

    /// <summary>
    /// A handler whose path ends in a constrained token, for the two positions a constraint can
    /// guard: the end of a route and the middle of one.
    /// </summary>
    private static List<RequestHandlerModel> CreateConstrainedHandlers() {
        var serviceType = TypeDefinition.Get("Test.Api.Services", "IPetService");

        return new List<RequestHandlerModel> {
            new(new RequestHandlerNameModel("/pets/{petId:int}", "GET"),
                serviceType, "GetPet",
                TypeDefinition.Get("Test.Api.Generated", "PetController_GetPet"),
                Array.Empty<RequestParameterInformation>(),
                new ResponseInformationModel { IsAsync = true },
                Array.Empty<AttributeModel>()),
            new(new RequestHandlerNameModel("/pets/{petId:guid}/tags", "GET"),
                serviceType, "GetTags",
                TypeDefinition.Get("Test.Api.Generated", "PetController_GetTags"),
                Array.Empty<RequestParameterInformation>(),
                new ResponseInformationModel { IsAsync = true },
                Array.Empty<AttributeModel>())
        };
    }

    /// <summary>
    /// The route tree is shared source, so a node built from a document carries
    /// <c>WildCardConstraint</c> exactly as one built from an attribute does. Until the spec table
    /// emitted the test, it read that property nowhere: a constrained token compiled, routed, and
    /// constrained nothing - the state <c>{id:int}</c> was in before any of this.
    /// </summary>
    [Fact]
    public void GenerateCSharpRouteFile_TestsAConstraintAtTheEndOfARoute() {
        var result = SpecRoutingTableGenerator.GenerateCSharpRouteFile(
            CreateAppModel(), CreateConstrainedHandlers(), ImmutableArray<HandlerInfo?>.Empty,
            ImmutableArray<SpecRegistration>.Empty, CancellationToken.None);

        Assert.Contains("RouteConstraints.IsInt(", result);

        // Negated at a leaf: failing the constraint is no match at all, so the method returns null
        // rather than handing the value on to a binder that would answer 400.
        Assert.Contains("!global::Hardened.Web.Runtime.Routing.RouteConstraints.IsInt(", result);
    }

    /// <summary>
    /// A constraint in the middle of a route gates the segment scan rather than being checked after
    /// it, so a value that fails leaves the scan free to try the next boundary.
    /// </summary>
    [Fact]
    public void GenerateCSharpRouteFile_TestsAConstraintInTheMiddleOfARoute() {
        var result = SpecRoutingTableGenerator.GenerateCSharpRouteFile(
            CreateAppModel(), CreateConstrainedHandlers(), ImmutableArray<HandlerInfo?>.Empty,
            ImmutableArray<SpecRegistration>.Empty, CancellationToken.None);

        Assert.Contains("RouteConstraints.IsGuid(", result);

        // Not negated, and not a separate statement: it joins the boundary conjunction.
        Assert.DoesNotContain("!global::Hardened.Web.Runtime.Routing.RouteConstraints.IsGuid(", result);
    }

    /// <summary>
    /// A token names at least one character, so <c>/pets/</c> is not a match for
    /// <c>/pets/{petId}</c>. The attribute-routed table has guarded this since 2026-08-16; this one
    /// did not, so an empty segment bound <c>""</c> and came back 400 from the binder — telling a
    /// client it addressed a real endpoint incorrectly about a URL that addresses no endpoint.
    /// </summary>
    [Fact]
    public void GenerateCSharpRouteFile_RejectsAnEmptyTokenAtTheEndOfARoute() {
        var result = SpecRoutingTableGenerator.GenerateCSharpRouteFile(
            CreateAppModel(), CreatePetstoreHandlers(), ImmutableArray<HandlerInfo?>.Empty,
            ImmutableArray<SpecRegistration>.Empty, CancellationToken.None);

        Assert.Contains("charSpan.Length <= index", result);
    }

    /// <summary>
    /// And mid-path the scan has to have consumed something, or it accepts a boundary at the
    /// position it started from — which let <c>//tags</c> match <c>/{petId}/tags</c> with the token
    /// bound to <c>""</c>. The same defect as above, at the other position.
    /// </summary>
    [Fact]
    public void GenerateCSharpRouteFile_RejectsAnEmptyTokenInTheMiddleOfARoute() {
        var result = SpecRoutingTableGenerator.GenerateCSharpRouteFile(
            CreateAppModel(), CreateConstrainedHandlers(), ImmutableArray<HandlerInfo?>.Empty,
            ImmutableArray<SpecRegistration>.Empty, CancellationToken.None);

        Assert.Contains("currentIndex > index", result);
    }

    /// <summary>A handler whose path carries a parameterised constraint and a chained one.</summary>
    private static List<RequestHandlerModel> CreateChainedHandlers(string constraint) {
        var serviceType = TypeDefinition.Get("Test.Api.Services", "IPetService");

        return new List<RequestHandlerModel> {
            new(new RequestHandlerNameModel("/pets/{petId:" + constraint + "}", "GET"),
                serviceType, "GetPet",
                TypeDefinition.Get("Test.Api.Generated", "PetController_GetPet"),
                Array.Empty<RequestParameterInformation>(),
                new ResponseInformationModel { IsAsync = true },
                Array.Empty<AttributeModel>())
        };
    }

    private static string Generate(string constraint) =>
        SpecRoutingTableGenerator.GenerateCSharpRouteFile(
            CreateAppModel(), CreateChainedHandlers(constraint), ImmutableArray<HandlerInfo?>.Empty,
            ImmutableArray<SpecRegistration>.Empty, CancellationToken.None);

    /// <summary>Arguments reach the emitted call as literals.</summary>
    [Theory]
    [InlineData("length(6)", "IsLength(charSpan.Slice(index), 6)")]
    [InlineData("length(3,9)", "IsLength(charSpan.Slice(index), 3, 9)")]
    [InlineData("min(1)", "IsMin(charSpan.Slice(index), 1)")]
    [InlineData("range(1,500)", "IsRange(charSpan.Slice(index), 1, 500)")]
    public void GenerateCSharpRouteFile_PassesConstraintArguments(string constraint, string call) {
        Assert.Contains(call, Generate(constraint));
    }

    /// <summary>
    /// A chain is one conjunction, and it is parenthesised because it is negated at a leaf — without
    /// the brackets <c>!A &amp;&amp; B</c> would negate the first term alone and let the second decide the
    /// match on its own.
    /// </summary>
    [Fact]
    public void GenerateCSharpRouteFile_JoinsAChainIntoOneNegatedConjunction() {
        var result = Generate("int:min(1)");

        Assert.Contains("!(global::Hardened.Web.Runtime.Routing.RouteConstraints.IsInt(", result);
        Assert.Contains("&& global::Hardened.Web.Runtime.Routing.RouteConstraints.IsMin(", result);
    }

    /// <summary>
    /// A single term needs no brackets, so the generated code keeps the shape it had before chains
    /// existed rather than churning every route table that already worked.
    /// </summary>
    [Fact]
    public void GenerateCSharpRouteFile_DoesNotBracketASingleTerm() {
        Assert.Contains("!global::Hardened.Web.Runtime.Routing.RouteConstraints.IsInt(", Generate("int"));
    }

    /// <summary>
    /// A name at an arity it does not have emits nothing, because the diagnostic has already
    /// reported it — a call to a method that does not exist would bury that under a CS1501.
    /// </summary>
    [Theory]
    [InlineData("length(1,2,3)")]
    [InlineData("range(1)")]
    [InlineData("length(")]
    public void GenerateCSharpRouteFile_EmitsNothingForAConstraintItCannotCompile(string constraint) {
        Assert.DoesNotContain("RouteConstraints.", Generate(constraint));
    }

    /// <summary>An unconstrained token emits no test, so the common route pays nothing.</summary>
    [Fact]
    public void GenerateCSharpRouteFile_EmitsNoConstraintWhenTheTokenDeclaresNone() {
        var result = SpecRoutingTableGenerator.GenerateCSharpRouteFile(
            CreateAppModel(), CreatePetstoreHandlers(), ImmutableArray<HandlerInfo?>.Empty,
            ImmutableArray<SpecRegistration>.Empty, CancellationToken.None);

        Assert.DoesNotContain("RouteConstraints.", result);
    }

    [Fact]
    public void GenerateCSharpRouteFile_ContainsDependencyRegistrationStaticField() {
        var appModel = CreateAppModel();
        var handlers = CreatePetstoreHandlers();

        var result = SpecRoutingTableGenerator.GenerateCSharpRouteFile(
            appModel, handlers, ImmutableArray<HandlerInfo?>.Empty, ImmutableArray<SpecRegistration>.Empty, CancellationToken.None);

        Assert.Contains("DependencyRegistry<TestApp>", result);
        Assert.Contains("SpecRoutingTableDI", result);
    }
}
