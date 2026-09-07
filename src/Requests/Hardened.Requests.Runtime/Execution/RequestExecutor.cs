using DependencyModules.Runtime.Attributes;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Logging;
using Hardened.Requests.Abstract.Metrics;
using Hardened.Requests.Abstract.Middleware;

namespace Hardened.Requests.Runtime.Execution;

/// <inheritdoc />
/// <remarks>
/// In <c>Hardened.Requests.Runtime</c> rather than in a web or a cloud package, because no line of
/// it names a transport and putting it anywhere else is what produced five copies of it.
/// </remarks>
[SingletonService]
public class RequestExecutor : IRequestExecutor {
    private readonly IMiddlewareService _middlewareService;
    private readonly IRequestLogger _requestLogger;

    public RequestExecutor(IMiddlewareService middlewareService, IRequestLogger requestLogger) {
        _middlewareService = middlewareService;
        _requestLogger = requestLogger;
    }

    public void Begin(IExecutionContext context) {
        _requestLogger.RequestBegin(context);
    }

    public async Task RunChain(IExecutionContext context, HostFailurePolicy onFailure) {
        try {
            await _middlewareService.GetExecutionChain(context).Next();
        }
        catch (Exception exception) {
            if (onFailure == HostFailurePolicy.Answer500 && !context.Response.ResponseStarted) {
                // Once the response has started the status line is already on the wire and there is
                // nothing left to say; on ASP.NET Core setting it would throw in its own right.
                // Decided before the logger is told, because the logger reads the status to pick
                // its level.
                context.Response.Status = 500;
            }

            _requestLogger.RequestFailed(context, exception);

            if (onFailure == HostFailurePolicy.Rethrow) {
                throw;
            }
        }
    }

    public void End(IExecutionContext context) {
        context.RequestMetrics.Record(
            RequestMetrics.TotalRequestDuration, context.StartTime.GetElapsedMilliseconds());

        _requestLogger.RequestEnd(context);

        // The logger is created per request and nothing else owns it. Disposal is how a provider
        // learns the request finished - EmbeddedMetricLogger writes its EMF line here - so without
        // it any provider that emits on completion emitted nothing at all.
        //
        // This is the line that was written, missed and then fixed three times in three files. It
        // exists once now, and a host reaches it from a finally rather than from straight-line
        // code an exception can skip.
        context.RequestMetrics.Dispose();
    }

    public async Task Run(IExecutionContext context, HostFailurePolicy onFailure) {
        Begin(context);

        try {
            await RunChain(context, onFailure);
        }
        finally {
            End(context);
        }
    }
}
