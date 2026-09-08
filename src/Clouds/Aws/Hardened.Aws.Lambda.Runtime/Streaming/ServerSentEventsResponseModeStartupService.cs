using Hardened.Requests.Abstract.Serializer;
using Hardened.Shared.Runtime.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hardened.Aws.Lambda.Runtime.Streaming;

/// <summary>
/// Says, at startup, when an application carries server-sent event handlers but was deployed
/// buffered.
/// </summary>
/// <remarks>
/// <para>
/// <b>The build cannot refuse this combination, which is why it is checked here.</b> The mode is an
/// environment variable and the same assembly serves both, so the compilation that knows a handler
/// carries <c>[ServerSentEvents]</c> does not know how the function will be deployed, and the
/// deployment that knows the mode has no view of the handlers. Only the running application holds
/// both, and the routing generator hands it the list through
/// <see cref="IServerSentEventManifest"/>.
/// </para>
/// <para>
/// In buffered mode every event is delivered when the invocation ends, or never when the function
/// times out first. That is not a degraded event stream but a broken one: a browser
/// <c>EventSource</c> holds a connection open expecting events as they happen, and gets a single
/// response minutes later or a timeout.
/// </para>
/// <para>
/// A warning rather than a throw. The application still serves its other routes correctly, and a
/// function refusing to start over one route's framing would take an entire deployment down for
/// something the operator may already know about.
/// </para>
/// <para>
/// Newline-delimited streams are not listed and get no warning. They arrive late in buffered mode
/// but intact, because nothing about NDJSON depends on when a line reaches the reader.
/// </para>
/// </remarks>
internal class ServerSentEventsResponseModeStartupService : IStartupService {
    public Task<bool> Startup(IServiceProvider rootProvider) {
        // Empty for every application without an event stream, which is the ordinary case: the
        // routing generator emits no manifest at all when no handler is framed as events.
        var handlers = rootProvider.GetServices<IServerSentEventManifest>()
            .SelectMany(manifest => manifest.Handlers)
            .ToArray();

        if (handlers.Length == 0) {
            return Task.FromResult(true);
        }

        if (rootProvider.GetRequiredService<IOptions<ILambdaResponseModeConfiguration>>().Value.Mode ==
            LambdaResponseMode.Stream) {
            return Task.FromResult(true);
        }

        var logger = rootProvider.GetService<ILoggerFactory>()
            ?.CreateLogger(typeof(ServerSentEventsResponseModeStartupService).FullName!);

        logger?.LogWarning(
            "{Variable} is buffered and {Count} handler(s) answer text/event-stream: {Handlers}. " +
            "Their events are delivered when the invocation ends, or never if it times out first. " +
            "Deploy behind a function URL in RESPONSE_STREAM invoke mode with {Variable}=stream, or " +
            "remove [ServerSentEvents].",
            LambdaResponseModeConfiguration.EnvironmentVariable,
            handlers.Length,
            string.Join(", ", handlers),
            LambdaResponseModeConfiguration.EnvironmentVariable);

        return Task.FromResult(true);
    }
}
