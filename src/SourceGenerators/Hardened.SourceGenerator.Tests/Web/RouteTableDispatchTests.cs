using CSharpAuthor;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.Shared;
using Hardened.SourceGenerator.Web;
using Xunit;

namespace Hardened.SourceGenerator.Tests.Web;

/// <summary>
/// Header dispatch — the AWS JSON protocol, where the operation is named by a request header rather
/// than by the path.
///
/// <para>
/// <see cref="RequestHandlerNameModel"/> has carried <c>DispatchHeader</c> and <c>DispatchKey</c>
/// all along, and it lives in the shared model folder this generator already compiles. Only the
/// emit was missing here, which is why a dispatched handler reaching this generator produced two
/// <c>case "POST"</c> labels in one switch — CS0152, uncompilable — rather than anything that could
/// be diagnosed. See <see cref="Dispatch_DoesNotCollapseTwoOperationsOntoOneVerb"/>.
/// </para>
/// </summary>
public class RouteTableDispatchTests {
    private static string Generate(params RequestHandlerModel[] handlers) =>
        RoutingTableGenerator.GenerateCSharpRouteFile(App(), handlers, CancellationToken.None);

    [Fact]
    public void Dispatch_SwitchesOnTheDeclaredHeader() {
        var result = Generate(
            Dispatched("X-Amz-Target", "Bank.GetBalance", "GetBalance"),
            Dispatched("X-Amz-Target", "Bank.Transfer", "Transfer"));

        Assert.Contains("Headers.TryGetValue(\"X-Amz-Target\"", result);
        Assert.Contains("case \"Bank.GetBalance\":", result);
        Assert.Contains("case \"Bank.Transfer\":", result);
    }

    /// <summary>
    /// The defect the golden capture surfaced. Two dispatched operations both declare POST on the
    /// same path, so a generator that ignores the dispatch fields writes the same case label twice.
    /// </summary>
    [Fact]
    public void Dispatch_DoesNotCollapseTwoOperationsOntoOneVerb() {
        var result = Generate(
            Dispatched("X-Amz-Target", "Bank.GetBalance", "GetBalance"),
            Dispatched("X-Amz-Target", "Bank.Transfer", "Transfer"));

        var postLabels = result.Split("case \"POST\":").Length - 1;

        Assert.True(postLabels <= 1,
            $"Emitted {postLabels} 'case \"POST\":' labels in the same switch, which is CS0152.");
    }

    /// <summary>
    /// An application may serve two protocols, so the header is a property of the model rather than
    /// an assumed X-Amz-Target. Each header gets its own lookup and its own out variable.
    /// </summary>
    [Fact]
    public void Dispatch_WithTwoHeaders_LooksEachOneUpSeparately() {
        var result = Generate(
            Dispatched("X-Amz-Target", "Bank.GetBalance", "GetBalance"),
            Dispatched("X-Custom-Op", "Ledger.Post", "PostLedger"));

        Assert.Contains("Headers.TryGetValue(\"X-Amz-Target\", out var dispatchValues)", result);
        Assert.Contains("Headers.TryGetValue(\"X-Custom-Op\", out var dispatchValues1)", result);
    }

    /// <summary>
    /// With nothing left to route, no tree is built over an empty list.
    /// </summary>
    [Fact]
    public void Dispatch_WithNothingRouted_EmitsNoRouteTree() {
        var result = Generate(
            Dispatched("X-Amz-Target", "Bank.GetBalance", "GetBalance"));

        Assert.DoesNotContain("pathSpan", result);
    }

    /// <summary>
    /// Both kinds in one application, dispatch consulted first. The order is the point: an awsJson
    /// service sends every operation to POST /, so a path tree consulted first would match that
    /// route for whichever handler owned it and answer the wrong one for the rest.
    /// </summary>
    [Fact]
    public void Dispatch_IsCheckedBeforeTheRouteTree() {
        var result = Generate(
            Dispatched("X-Amz-Target", "Bank.GetBalance", "GetBalance"),
            Routed("/health", "GET", "Health"));

        var dispatch = result.IndexOf("Headers.TryGetValue", StringComparison.Ordinal);
        var routing = result.IndexOf("pathSpan", StringComparison.Ordinal);

        Assert.True(dispatch >= 0, "No dispatch lookup was emitted.");
        Assert.True(routing >= 0, "No route tree was emitted.");
        Assert.True(dispatch < routing, "The route tree is consulted before header dispatch.");
    }

    /// <summary>
    /// A dispatched route carries no template, so there is nothing to bind.
    /// </summary>
    [Fact]
    public void Dispatch_BindsNoPathTokens() {
        var result = Generate(
            Dispatched("X-Amz-Target", "Bank.GetBalance", "GetBalance"));

        Assert.Contains("PathTokenCollection.Empty", result);
    }

    private static EntryPointSelector.Model App() =>
        new() {
            EntryPointType = TypeDefinition.Get("Test.Api", "TestApp"),
            AttributeModels = Array.Empty<AttributeModel>(),
            RootEntryPoint = true,
            MethodDefinitions = Array.Empty<HardenedMethodDefinition>()
        };

    private static RequestHandlerModel Dispatched(string header, string key, string name) =>
        Model(new RequestHandlerNameModel("/", "POST", header, key), name);

    private static RequestHandlerModel Routed(string path, string method, string name) =>
        Model(new RequestHandlerNameModel(path, method), name);

    private static RequestHandlerModel Model(RequestHandlerNameModel name, string handlerName) =>
        new(name,
            TypeDefinition.Get("Test.Api.Services", "IBankService"),
            handlerName,
            TypeDefinition.Get("Test.Api.Generated", "BankController_" + handlerName),
            Array.Empty<RequestParameterInformation>(),
            new ResponseInformationModel { IsAsync = true },
            Array.Empty<AttributeModel>());
}
