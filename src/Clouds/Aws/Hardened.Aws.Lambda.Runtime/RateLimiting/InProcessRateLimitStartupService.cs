using Hardened.Requests.Abstract.RateLimiting;
using Hardened.Requests.Runtime.RateLimiting;
using Hardened.Shared.Runtime.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Hardened.Aws.Lambda.Runtime.RateLimiting;

/// <summary>
/// Says, at startup, when a function declares rate limits and counts them in process.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="InProcessRateLimitStore"/> counts per execution environment, and Lambda runs as many
/// environments as traffic needs, so a caller gets the limit from each of them. The guide says so,
/// and nothing at startup did. The routing generator hands the list of limited handlers over
/// through <see cref="IRateLimitManifest"/>, and an application that registers a store of its own
/// hears nothing.
/// </para>
/// <para>
/// A warning rather than a throw, for the reason the response-mode warning gives: the function
/// still serves every route, and the operator may already count in API Gateway or WAF.
/// </para>
/// </remarks>
internal class InProcessRateLimitStartupService : IStartupService
{
    public Task<bool> Startup(IServiceProvider rootProvider)
    {
        // Empty for every application without a rate limit, which is the ordinary case: the
        // routing generator emits no manifest at all when no handler is limited.
        var handlers = rootProvider
            .GetServices<IRateLimitManifest>()
            .SelectMany(manifest => manifest.Handlers)
            .ToArray();

        if (
            handlers.Length == 0
            || rootProvider.GetService<IRateLimitStore>() is not InProcessRateLimitStore
        )
        {
            return Task.FromResult(true);
        }

        var logger = rootProvider
            .GetService<ILoggerFactory>()
            ?.CreateLogger(typeof(InProcessRateLimitStartupService).FullName!);

        logger?.LogWarning(
            "{Count} handler(s) declare a rate limit and InProcessRateLimitStore counts it: {Handlers}. "
                + "It counts per execution environment, and Lambda runs as many as traffic needs, so a "
                + "caller gets the limit from each of them. Count in an API Gateway usage plan or AWS WAF, "
                + "or register an IRateLimitStore that counts somewhere shared.",
            handlers.Length,
            string.Join(", ", handlers)
        );

        return Task.FromResult(true);
    }
}
