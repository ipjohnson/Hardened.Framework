using System.Collections.Immutable;
using CSharpAuthor;
using Hardened.Idl.Models;
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
            ImmutableArray<string>.Empty, CancellationToken.None);

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
            ImmutableArray<string>.Empty, CancellationToken.None);

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
            ImmutableArray<string>.Empty, CancellationToken.None);

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
            appModel, handlers, ImmutableArray<HandlerInfo?>.Empty, ImmutableArray<string>.Empty, CancellationToken.None);

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
            appModel, handlers, ImmutableArray<HandlerInfo?>.Empty, ImmutableArray<string>.Empty, CancellationToken.None);

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
            appModel, handlers, handlerInfos, ImmutableArray<string>.Empty, CancellationToken.None);

        Assert.Matches(@"AddTransient<[^>]*IPetService[^>]*,[^>]*PetServiceImpl[^>]*>", result);
    }

    [Fact]
    public void GenerateCSharpRouteFile_ContainsRoutingForEndpointPaths() {
        var appModel = CreateAppModel();
        var handlers = CreatePetstoreHandlers();

        var result = SpecRoutingTableGenerator.GenerateCSharpRouteFile(
            appModel, handlers, ImmutableArray<HandlerInfo?>.Empty, ImmutableArray<string>.Empty, CancellationToken.None);

        // Routing should reference handler fields for each endpoint
        Assert.Contains("PetController_ListPets", result);
        Assert.Contains("PetController_CreatePet", result);
        Assert.Contains("PetController_GetPet", result);
        Assert.Contains("PetController_DeletePet", result);
        Assert.Contains("StoreController_ListStores", result);
    }

    [Fact]
    public void GenerateCSharpRouteFile_ContainsDependencyRegistrationStaticField() {
        var appModel = CreateAppModel();
        var handlers = CreatePetstoreHandlers();

        var result = SpecRoutingTableGenerator.GenerateCSharpRouteFile(
            appModel, handlers, ImmutableArray<HandlerInfo?>.Empty, ImmutableArray<string>.Empty, CancellationToken.None);

        Assert.Contains("DependencyRegistry<TestApp>", result);
        Assert.Contains("SpecRoutingTableDI", result);
    }
}
