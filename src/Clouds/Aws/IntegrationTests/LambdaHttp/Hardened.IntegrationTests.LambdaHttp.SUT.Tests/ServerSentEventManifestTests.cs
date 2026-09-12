using Hardened.Requests.Abstract.Serializer;
using Hardened.Shared.Testing.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Hardened.IntegrationTests.LambdaHttp.SUT.Tests;

/// <summary>
/// The whole chain the buffered-mode warning rests on, through a real application rather than a
/// fixture: the routing generator finds the framing, emits a manifest, and registers it where the
/// host can resolve it.
/// </summary>
/// <remarks>
/// Each half is covered on its own - the emitted source by the web pipeline fixtures, the warning by
/// <c>ServerSentEventsResponseModeStartupServiceTests</c> - and neither says the two meet. A
/// manifest that is generated and never registered would pass both and warn about nothing.
/// </remarks>
public class ServerSentEventManifestTests {

    [HardenedTest]
    public void TheEventStreamHandlerIsInTheManifest(IServiceProvider provider) {
        var handlers = provider.GetServices<IServerSentEventManifest>()
            .SelectMany(manifest => manifest.Handlers)
            .ToArray();

        Assert.Contains("GET /orders/live", handlers);
    }

    /// <summary>
    /// Token names only. The route is <c>/orders/{id:int}/live</c>, and the constraint is how the
    /// router decides what matches: an operator reading this line wants the route they wrote, and a
    /// constraint a contract declared is named after a hash of its pattern -
    /// <c>{deviceId:spec_p_588343bc}</c> - which appears in nobody's source.
    /// </summary>
    [HardenedTest]
    public void AConstrainedRouteIsListedWithoutItsConstraint(IServiceProvider provider) {
        var handlers = provider.GetServices<IServerSentEventManifest>()
            .SelectMany(manifest => manifest.Handlers)
            .ToArray();

        Assert.Contains("GET /orders/{id}/live", handlers);
        Assert.DoesNotContain(handlers, handler => handler.Contains(":int"));
    }

    /// <summary>
    /// The three ordinary routes are not listed. Only a handler framed as events belongs here, and
    /// a manifest naming every route would have the host warn about all of them.
    /// </summary>
    [HardenedTest]
    public void OrdinaryHandlersAreNotListed(IServiceProvider provider) {
        var handlers = provider.GetServices<IServerSentEventManifest>()
            .SelectMany(manifest => manifest.Handlers)
            .ToArray();

        Assert.DoesNotContain("GET /orders/{id}", handlers);
        Assert.DoesNotContain("DELETE /orders/{id}", handlers);
        Assert.DoesNotContain("POST /orders", handlers);
    }
}
