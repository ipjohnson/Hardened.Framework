using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.PathTokens;
using Hardened.Requests.Runtime.Authorization;
using Hardened.Requests.Runtime.Execution;
using Hardened.Web.Runtime.Handlers;
using Microsoft.Extensions.Primitives;

namespace Hardened.Web.Runtime.Routing;

/// <summary>
/// Serves the routes an <see cref="IRouteRegistration"/> registered.
/// </summary>
/// <remarks>
/// <para>
/// One more <see cref="IWebExecutionRequestHandlerProvider"/>, which is how routing tables
/// themselves are registered and how the health endpoints and the OpenAPI document are served. It
/// needs no change to the dispatch path at all, and everything below the match - the filter chain,
/// authorization, serialization, the response cache, compression, logging - is the same code that
/// runs for an attribute route.
/// </para>
/// <para>
/// <b>Registered before anything an application declares</b>, because providers are consulted in
/// reverse registration order. So the compiled table is asked first, and a route written as an
/// attribute always beats a route registered in a loop. That is the rule easiest to explain and
/// hardest to trip over, and it does not depend on which module an application listed first.
/// </para>
/// </remarks>
public sealed class RegisteredRouteProvider : IWebExecutionRequestHandlerProvider
{
    private readonly IServiceProvider _rootProvider;
    private readonly bool _expectsRegistrations;

    private RuntimeRouteTable? _table;
    private IExecutionRequestHandler? _pending;

    public RegisteredRouteProvider(IServiceProvider rootProvider, bool expectsRegistrations)
    {
        _rootProvider = rootProvider;
        _expectsRegistrations = expectsRegistrations;

        // An application with no registration has nothing to wait for, so it is ready before
        // startup runs and every request falls straight through an empty table.
        if (!expectsRegistrations)
        {
            _table = RuntimeRouteTable.Empty;
        }
    }

    /// <summary>Whether registration has closed and the table has been published.</summary>
    public bool IsReady => _table != null;

    /// <summary>
    /// Publishes the table. Called once, when registration closes.
    /// </summary>
    public void Publish(RuntimeRouteTable table)
    {
        _table = table;
    }

    public RequestHandlerInfo? GetExecutionRequestHandler(
        IExecutionContext context,
        ref PathTokenCollection pathTokens
    )
    {
        var table = _table;

        if (table != null)
        {
            return table.Match(
                context.Request.Path.AsSpan(),
                context.Request.Method,
                ref pathTokens
            );
        }

        // Registration has not closed. A 404 here would be a lie, and a lie a CDN or an API gateway
        // caches; 503 says come back, which is what is actually true.
        return new RequestHandlerInfo(_pending ??= PendingHandler.For(_rootProvider));
    }

    /// <summary>
    /// What answers while the table is still being built.
    /// </summary>
    /// <remarks>
    /// A handler rather than a status written inline, so the answer goes out through the same chain
    /// every other response does - the same reason <c>HealthCheckController</c> exists rather than
    /// the health provider answering in place.
    /// </remarks>
    private sealed class PendingHandler : BaseExecutionHandler<RouteRegistrationController>
    {
        /// <summary>
        /// Anonymous, and that is the difference from <c>HealthCheckProvider</c>, which deliberately
        /// carries nothing. A 503 saying the application is not serving yet tells a caller nothing
        /// it could not learn by waiting, and under a default-deny posture the alternative is a 401
        /// that says something false about why the request failed.
        /// </summary>
        private static readonly object[] Metadata = [new AllowAnonymousAttribute()];

        private PendingHandler(ExecutionHandlerSetup setup)
            : base(setup) { }

        public static PendingHandler For(IServiceProvider serviceProvider) =>
            new(
                ExecutionHelper.AsyncStandardFilterEmptyParameters<RouteRegistrationController>(
                    serviceProvider,
                    new ExecutionRequestHandlerInfo(
                        "/",
                        "GET",
                        typeof(RouteRegistrationController),
                        nameof(RouteRegistrationController.Pending),
                        [],
                        Metadata
                    ),
                    (context, controller) => controller.Pending(context),
                    ExecutionHelper.GetFilterInfo(Metadata)
                )
            );
    }
}

/// <summary>
/// Answers a request that arrived before route registration finished.
/// </summary>
public class RouteRegistrationController
{
    public Task Pending(IExecutionContext context)
    {
        context.Response.Status = 503;
        context.Response.Headers[KnownHeaders.RetryAfter] = new StringValues("1");

        // Nothing to write and nothing to serialize, for the reason MethodNotAllowedHandler gives:
        // the response is the status and the header.
        context.Response.ShouldSerialize = false;

        return Task.CompletedTask;
    }
}
