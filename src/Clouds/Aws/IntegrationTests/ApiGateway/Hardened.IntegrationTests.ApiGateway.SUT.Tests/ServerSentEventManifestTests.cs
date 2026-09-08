using Hardened.Requests.Abstract.Serializer;
using Hardened.Shared.Testing.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Hardened.IntegrationTests.ApiGateway.SUT.Tests;

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

        Assert.Equal(["GET /orders/live"], handlers);
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

        Assert.DoesNotContain(handlers, handler => handler.Contains("/orders/{id}"));
        Assert.DoesNotContain(handlers, handler => handler == "POST /orders");
    }
}
