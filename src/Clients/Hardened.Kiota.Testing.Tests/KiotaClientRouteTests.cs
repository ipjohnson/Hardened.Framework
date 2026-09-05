using Hardened.Web.Testing;
using Microsoft.Kiota.Abstractions;
using Xunit;

namespace Hardened.Kiota.Testing.Tests;

/// <summary>
/// Which types the route answers for: the shape Kiota generates, and nothing else.
/// </summary>
/// <remarks>
/// Building one needs the harness's context, which only the harness makes, so construction over
/// the pipeline is asserted in the Web integration application's suite.
/// </remarks>
public class KiotaClientRouteTests {

    /// <summary>The shape every Kiota client has: a request builder over one adapter.</summary>
    private sealed class GeneratedClient(IRequestAdapter adapter)
        : BaseRequestBuilder(adapter, "{+baseurl}", new Dictionary<string, object>());

    private abstract class AbstractBuilder(IRequestAdapter adapter)
        : BaseRequestBuilder(adapter, "{+baseurl}", new Dictionary<string, object>());

    /// <summary>A request builder whose constructor is not the client's: a nested builder.</summary>
    private sealed class NestedBuilder(IRequestAdapter adapter, string id)
        : BaseRequestBuilder(adapter, "{+baseurl}/" + id, new Dictionary<string, object>());

    private sealed class HandWritten(HttpClient http) {
        public HttpClient Http { get; } = http;
    }

    private static readonly KiotaClientRoute Route = new();

    [Fact]
    public void AKiotaClientIsRecognisedByItsShape() {
        Assert.True(Route.CanBuild(typeof(GeneratedClient)));
    }

    [Fact]
    public void AClientOverAnHttpClientIsNotThisRoutes() {
        Assert.False(Route.CanBuild(typeof(HandWritten)));
    }

    [Fact]
    public void AnAbstractBuilderIsNotBuilt() {
        Assert.False(Route.CanBuild(typeof(AbstractBuilder)));
    }

    [Fact]
    public void ABuilderNeedingMoreThanAnAdapterIsNotAClient() {
        Assert.False(Route.CanBuild(typeof(NestedBuilder)));
    }

    [Fact]
    public void AnInterfaceIsNotAClient() {
        Assert.False(Route.CanBuild(typeof(IRequestAdapter)));
    }

    [Fact]
    public void TheAttributeNamesTheRoute() {
        TestClientRouteAttribute attribute = new KiotaTestingAttribute();

        Assert.Equal(typeof(KiotaClientRoute), attribute.RouteType);
    }
}
