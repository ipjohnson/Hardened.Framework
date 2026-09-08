using Hardened.Requests.Abstract.Middleware;
using Hardened.Shared.Runtime.Application;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Gcp.CloudRun.Runtime.Envelopes;

/// <summary>
/// Puts the front door in the middleware chain at startup, when any adapter registered an
/// envelope.
/// </summary>
/// <remarks>
/// <para>
/// A startup service rather than a registration because the chain is composed at startup: the
/// host runs the startup services and then appends dispatch, so anything added here sits ahead
/// of routing however the host was reached - Kestrel, the pipeline test host, or a test delivery
/// running the executor directly. The same arrangement the authentication middleware uses.
/// </para>
/// <para>
/// A service with no adapter package registered no envelope and gets no front door, so a web
/// application on Cloud Run pays nothing per request for a feature it did not ask for.
/// </para>
/// </remarks>
internal sealed class TriggerFrontDoorStartupService : IStartupService {
    public Task<bool> Startup(IServiceProvider rootProvider) {
        var envelopes = rootProvider.GetServices<ITriggerEnvelope>().ToArray();

        if (envelopes.Length > 0) {
            var frontDoor = new TriggerFrontDoor(envelopes);

            rootProvider.GetRequiredService<IMiddlewareService>().Use(_ => frontDoor);
        }

        return Task.FromResult(true);
    }
}
