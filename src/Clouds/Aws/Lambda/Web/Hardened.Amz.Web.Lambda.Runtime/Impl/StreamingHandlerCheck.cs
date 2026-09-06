using Hardened.Amz.Shared.Lambda.Runtime.Streaming;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hardened.Amz.Web.Lambda.Runtime.Impl;

/// <summary>
/// Says, at startup, when an application carries server-sent event handlers but is deployed in
/// buffered mode.
/// </summary>
/// <remarks>
/// <para>
/// The build cannot refuse the combination, because the build no longer knows the deployment: the
/// same assembly serves both modes and the mode is an environment variable. What the build does
/// know is which handlers carry <c>[ServerSentEvents]</c>, and the generated application hands
/// that list here. In buffered mode every event is delivered when the invocation ends, or never
/// when the function times out first, which is not a degraded event stream but a broken one. A
/// warning rather than a throw: the application still serves its other routes.
/// </para>
/// <para>
/// NDJSON handlers are not listed. They arrive late in buffered mode but intact.
/// </para>
/// </remarks>
public static class StreamingHandlerCheck {
    public static void Warn(IServiceProvider services, IReadOnlyList<string> streamingHandlers) {
        if (streamingHandlers.Count == 0) {
            return;
        }

        var mode = services.GetRequiredService<IOptions<ILambdaResponseModeConfiguration>>().Value.Mode;

        if (mode == LambdaResponseMode.Stream) {
            return;
        }

        var logger = services.GetService<ILoggerFactory>()?.CreateLogger(typeof(StreamingHandlerCheck).FullName!);

        logger?.LogWarning(
            "{Variable} is buffered and {Count} handler(s) use [ServerSentEvents]: {Handlers}. " +
            "Their events are delivered when the invocation ends. Deploy behind a function URL in " +
            "RESPONSE_STREAM invoke mode with {Variable}=stream, or remove the attribute.",
            LambdaResponseModeConfiguration.EnvironmentVariable,
            streamingHandlers.Count,
            string.Join(", ", streamingHandlers),
            LambdaResponseModeConfiguration.EnvironmentVariable);
    }
}
