using Hardened.Requests.Abstract.Execution;
using DependencyModules.Runtime.Attributes;
using Hardened.Shared.Runtime.Metrics;
using Microsoft.AspNetCore.Http;

namespace Hardened.Web.AspNetCore.Runtime.Impl;

public interface IAspNetCoreRequestHandler {
    Task HandleRequest(HttpContext context, RequestDelegate requestDelegate);
}

[TransientService]
public class AspNetCoreRequestHandler : IAspNetCoreRequestHandler {
    private readonly IMetricLoggerProvider _metricLoggerProvider;
    private readonly IRequestExecutor _executor;

    public AspNetCoreRequestHandler(
        IMetricLoggerProvider metricLoggerProvider,
        IRequestExecutor executor) {
        _metricLoggerProvider = metricLoggerProvider;
        _executor = executor;
    }

    /// <summary>
    /// Runs the Hardened chain, and hands the request on if the chain produced nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The begin, the duration and the end come from <c>IRequestExecutor</c>, which is what every
    /// other host uses. This one used to bracket the chain itself and, before that, not at all: an
    /// ASP.NET-hosted application saw <c>RequestMapped</c> and <c>RequestFailed</c> from inside the
    /// pipeline but never a begin, an end, or a <c>TotalRequestDuration</c>.
    /// </para>
    /// <para>
    /// <see cref="HostFailurePolicy.Answer500"/>, so a throw from the chain is answered here rather
    /// than reaching ASP.NET, whose handler logs against the server rather than the application's
    /// <c>IRequestLogger</c> and aborts the connection mid-body once the response has started. The
    /// commonest way to arrive there is a filter writing a response header after <c>Next()</c>: the
    /// ASP.NET header dictionary is read-only once the response has started and its setter throws.
    /// </para>
    /// <para>
    /// Only the Hardened chain is covered. An exception from the fallthrough delegate belongs to
    /// whatever middleware is behind <c>UseHardened</c>, and to the exception handling that
    /// application installed for it — so the fallthrough sits outside <c>RunChain</c> and inside
    /// the <c>finally</c> that closes the request out.
    /// </para>
    /// </remarks>
    public async Task HandleRequest(HttpContext context, RequestDelegate requestDelegate) {
        var executionContext = GetExecutionContext(context, _metricLoggerProvider);

        _executor.Begin(executionContext);

        try {
            await _executor.RunChain(executionContext, HostFailurePolicy.Answer500);

            if (!Answered(executionContext)) {
                await requestDelegate(context);
            }
        }
        finally {
            // In a finally because the close-out ran as straight-line statements after the chain,
            // so an exception escaping to ASP.NET's own handler took it all with it: no duration,
            // no end, and no flush. The fallthrough delegate can still throw, which is what makes
            // this a finally rather than IRequestExecutor.Run.
            _executor.End(executionContext);
        }
    }

    /// <summary>
    /// Whether the Hardened chain answered this request, and so whether it stops here rather than
    /// continuing down the ASP.NET pipeline.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This used to ask <c>context.Response.HasStarted</c>, which turns true only once response
    /// <em>bytes</em> have flushed. A status and headers flush nothing, so every response with no
    /// body fell through to the terminal delegate and came back as ASP.NET's 404: a 204, a 308
    /// redirect, a 405 with its <c>Allow</c> header, a <c>HEAD</c>, and any 200 from a handler
    /// returning <c>Task</c>. The side effect had already happened — a <c>DELETE</c> really did
    /// delete — and the caller was told the resource did not exist. The Kestrel host was unaffected
    /// because it has no fallthrough to fall into.
    /// </para>
    /// <para>
    /// Four signals, because the pipeline has four ways to answer and they set different things:
    /// </para>
    /// <list type="bullet">
    /// <item><c>HandlerInfo</c> — routing selected a handler. Set by
    /// <c>WebExecutionHandlerService.Dispatch</c> before the handler runs, so it holds even when the
    /// handler writes nothing at all. This is the one the old check had no equivalent of.</item>
    /// <item><c>Status</c> — static content (200 or 304), a 405, a 308 trailing-slash redirect, or a
    /// handler that set one itself.</item>
    /// <item><c>ResponseValue</c> — a handler returned something not yet serialized.</item>
    /// <item><c>ResponseStarted</c> — bytes are already on the wire. The old signal, still
    /// sufficient, now merely no longer necessary.</item>
    /// </list>
    /// <para>
    /// A path Hardened declares nothing for reaches none of them, because this host registers
    /// <see cref="AspNetResourceNotFoundHandler"/> in place of the framework's terminal one exactly
    /// so the status stays unset. That request falls through — which is the reason the fallthrough
    /// exists, so static files, another middleware or MVC behind <c>UseHardened()</c> get their
    /// turn, and ASP.NET's own 404 answers if none of them do.
    /// </para>
    /// </remarks>
    private static bool Answered(IExecutionContext executionContext) {
        var response = executionContext.Response;

        return executionContext.HandlerInfo != null
               || response.Status.HasValue
               || response.ResponseValue != null
               || response.ResponseStarted;
    }

    private IExecutionContext GetExecutionContext(
        HttpContext context,
        IMetricLoggerProvider metricLoggerProvider) {
        return new AspNetExecutionContext(context, metricLoggerProvider.CreateLogger("asp-net-session"));
    }
}
