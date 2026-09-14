using Hardened.Requests.Abstract.Execution;
using Hardened.Shared.Runtime.Metrics;
using Hardened.Web.Kestrel.Runtime.Impl;
using Microsoft.AspNetCore.Http;

namespace Hardened.Gcp.Functions.Runtime.Hosting;

/// <summary>
/// Runs a request the Functions Framework handed over through the Hardened chain.
///
/// This is <see cref="HardenedHttpApplication"/>'s job on a host that owns the server. The
/// difference is what the host has already done by the time this is reached: Google's
/// <c>Host.CreateDefaultBuilder</c> built the request scope, and its ASP.NET Core pipeline built
/// the <c>HttpContext</c>. What is left is the same three steps against the same feature
/// collection.
/// </summary>
/// <remarks>
/// <para>
/// <b>The features are the seam.</b> <see cref="FeatureExecutionContext"/> is built from an
/// <c>IFeatureCollection</c> and reads <c>IHttpRequestFeature</c>, <c>IHttpResponseFeature</c> and
/// <c>IHttpResponseBodyFeature</c> off it; <c>HttpContext.Features</c> is an
/// <c>IFeatureCollection</c> and ASP.NET Core supplies all three. So both hosts construct the same
/// context from the same interfaces, and every line below it - the request, the response, the
/// front door, routing and the handler - is the same compiled code on either. That is what makes
/// the two deployment models comparable rather than merely similar.
/// </para>
/// <para>
/// <b>No scope is created and none is disposed.</b> <c>HardenedHttpApplication</c> opens one
/// because <c>IHttpApplication&lt;T&gt;</c> is handed a bare feature collection and Kestrel has no
/// notion of services. Here the request already has a scope and the host owns its lifetime, which
/// is the arrangement <c>IRequestExecutor.End</c> describes for ASP.NET Core.
/// </para>
/// <para>
/// <b>A singleton, resolved once.</b> The Functions Framework registers the
/// <c>FUNCTION_TARGET</c> type as scoped, so the generated entry type is constructed per request;
/// everything it needs is held here instead, where <c>IServiceProvider</c> is the root provider
/// rather than the request's. The generated type is then one field and one forwarding call.
/// </para>
/// <para>
/// <b>Hosting diagnostics are raised, and on the Kestrel host they are not.</b> The request
/// reached here through Google's ASP.NET Core pipeline, which starts an <c>Activity</c> and writes
/// the <c>Microsoft.AspNetCore.Hosting</c> <c>DiagnosticSource</c> and <c>EventSource</c> events
/// that <see cref="HardenedHttpApplication"/> documents itself as omitting. An OpenTelemetry setup
/// subscribed to those names observes this host and does not observe that one. It is a per-request
/// cost this host pays, and it is the ordinary ASP.NET Core cost rather than anything Hardened
/// adds.
/// </para>
/// </remarks>
public sealed class CloudFunctionHost
{
    private readonly IServiceProvider _rootServiceProvider;
    private readonly IRequestExecutor _executor;
    private readonly IMetricLoggerProvider _metricLoggerProvider;

    public CloudFunctionHost(
        IServiceProvider rootServiceProvider,
        IRequestExecutor executor,
        IMetricLoggerProvider metricLoggerProvider
    )
    {
        _rootServiceProvider = rootServiceProvider;
        _executor = executor;
        _metricLoggerProvider = metricLoggerProvider;
    }

    /// <summary>
    /// Begin, run the chain, complete the response, end - the order
    /// <see cref="HardenedHttpApplication"/> holds across its three callbacks.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="HostFailurePolicy.Answer500"/> for the reason the web hosts give: Google's
    /// pipeline does have a handler for a function that throws, but it logs against the host and
    /// answers its own 500, so the application's <c>IRequestLogger</c> never sees the failure.
    /// </para>
    /// <para>
    /// <c>CompleteAsync</c> in a <c>finally</c> and inside the one that ends the request, so a
    /// response with no body - a 204, or the 500 the failure policy sets - flushes its headers
    /// before the duration is recorded. <c>Begin</c>/<c>RunChain</c>/<c>End</c> rather than
    /// <c>IRequestExecutor.Run</c>, which would put <c>End</c> inside and reverse that order.
    /// </para>
    /// </remarks>
    public async Task HandleAsync(HttpContext context)
    {
        var execution = new FeatureExecutionContext(
            _rootServiceProvider,
            context.RequestServices,
            context.Features,
            _metricLoggerProvider.CreateLogger("cloud-functions-session")
        );

        _executor.Begin(execution);

        try
        {
            try
            {
                await _executor.RunChain(execution, HostFailurePolicy.Answer500);
            }
            finally
            {
                await execution.CompleteAsync();
            }
        }
        finally
        {
            _executor.End(execution);
        }
    }
}
