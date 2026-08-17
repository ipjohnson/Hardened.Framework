using Hardened.Requests.Abstract.Authorization;
using Hardened.Requests.Abstract.Execution;
using Hardened.Shared.Runtime.Diagnostics;
using Hardened.Shared.Runtime.Metrics;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Web.Kestrel.Runtime.Impl;

/// <summary>
/// An execution context built straight from a server's feature collection, with no
/// <c>HttpContext</c> in between.
/// </summary>
public sealed class FeatureExecutionContext : IExecutionContext {
    /// <summary>
    /// Held concretely so the response can be completed at the end of the request.
    /// <see cref="Response"/> is typed as the interface and a fork may replace it, but completion
    /// always belongs to the body feature the server supplied.
    /// </summary>
    private readonly FeatureExecutionResponse _featureResponse;

    public FeatureExecutionContext(
        IServiceProvider rootServiceProvider,
        IServiceProvider requestServices,
        IFeatureCollection features,
        IMetricLogger metricLogger) {
        RootServiceProvider = rootServiceProvider;
        RequestServices = requestServices;
        KnownServices = requestServices.GetRequiredService<IKnownServices>();

        var requestFeature = features.Get<IHttpRequestFeature>() ??
            throw new InvalidOperationException(
                "The server did not supply an IHttpRequestFeature.");
        var responseFeature = features.Get<IHttpResponseFeature>() ??
            throw new InvalidOperationException(
                "The server did not supply an IHttpResponseFeature.");
        var responseBodyFeature = features.Get<IHttpResponseBodyFeature>() ??
            throw new InvalidOperationException(
                "The server did not supply an IHttpResponseBodyFeature.");

        _featureResponse = new FeatureExecutionResponse(responseFeature, responseBodyFeature);

        Request = new FeatureExecutionRequest(requestFeature);
        Response = _featureResponse;

        // Without this a client disconnect never reaches the handler and the application keeps
        // working on a response nobody will read. HostingApplication wires the same feature onto
        // HttpContext.RequestAborted.
        CancellationToken =
            features.Get<IHttpRequestLifetimeFeature>()?.RequestAborted ?? CancellationToken.None;

        RequestMetrics = metricLogger;
        StartTime = MachineTimestamp.Now;
    }

    private FeatureExecutionContext(
        FeatureExecutionContext source,
        IExecutionRequest request,
        IExecutionResponse response,
        IServiceProvider requestServices,
        IMetricLogger metricLogger) {
        RootServiceProvider = source.RootServiceProvider;
        RequestServices = requestServices;
        KnownServices = source.KnownServices;
        _featureResponse = source._featureResponse;
        Request = request;
        Response = response;
        RequestMetrics = metricLogger;
        StartTime = source.StartTime;
        CancellationToken = source.CancellationToken;
    }

    public IExecutionContext Clone(
        IExecutionRequest? request,
        IExecutionResponse? response,
        IServiceProvider? serviceProvider,
        IMetricLogger? metricLogger) {
        return new FeatureExecutionContext(
            this,
            request ?? Request,
            response ?? Response,
            serviceProvider ?? RequestServices,
            metricLogger ?? RequestMetrics) {
            HandlerInstance = HandlerInstance,
            HandlerInfo = HandlerInfo,
            // The reference, not a copy: a fork is the same caller.
            CallerPrincipal = CallerPrincipal
        };
    }

    public IServiceProvider RootServiceProvider { get; }

    public IKnownServices KnownServices { get; }

    public IServiceProvider RequestServices { get; }

    public IExecutionRequest Request { get; }

    public IExecutionResponse Response { get; }

    public ICallerPrincipal CallerPrincipal { get; set; } = AnonymousCallerPrincipal.Instance;

    public object? HandlerInstance { get; set; }

    public IExecutionRequestHandlerInfo? HandlerInfo { get; set; }

    public DefaultOutputFunc? DefaultOutput { get; set; }

    public IMetricLogger RequestMetrics { get; }

    public MachineTimestamp StartTime { get; }

    public CancellationToken CancellationToken { get; }

    /// <summary>
    /// Flushes and completes the response. Required by Kestrel — see
    /// <see cref="FeatureExecutionResponse.CompleteAsync"/>.
    /// </summary>
    public ValueTask CompleteAsync() => _featureResponse.CompleteAsync();
}
