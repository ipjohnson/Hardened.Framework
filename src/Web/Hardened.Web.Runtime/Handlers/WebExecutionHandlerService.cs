using DependencyModules.Runtime.Attributes;
using Hardened.Requests.Abstract.Errors;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.Logging;
using Hardened.Requests.Runtime.PathTokens;
using Hardened.Web.Runtime.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Hardened.Web.Runtime.Handlers;

public interface IWebExecutionHandlerService : IExecutionFilter { }

[SingletonService(Using = RegistrationType.Try)]
public partial class WebExecutionHandlerService : IWebExecutionHandlerService {
    private readonly IEnumerable<IWebExecutionRequestHandlerProvider> _handlers;
    private readonly IResourceNotFoundHandler _resourceNotFoundHandler;
    private readonly IMethodNotAllowedHandler _methodNotAllowedHandler;
    private readonly IRequestLogger _requestLogger;
    private readonly IWebRoutingConfiguration _routing;

    /// <param name="fallbacks">
    /// Providers that answer whatever nothing else claimed, appended after every ordinary one.
    /// See <see cref="IFallbackRequestHandlerProvider"/>: a package shipping a provider that can
    /// shadow any path cannot control where its registration lands relative to another module's,
    /// so being asked last is a property of the type rather than of the registration site.
    /// </param>
    public WebExecutionHandlerService(
        IEnumerable<IWebExecutionRequestHandlerProvider> handlers,
        IEnumerable<IFallbackRequestHandlerProvider> fallbacks,
        IResourceNotFoundHandler resourceNotFoundHandler,
        IMethodNotAllowedHandler methodNotAllowedHandler,
        IRequestLogger requestLogger,
        IOptions<IWebRoutingConfiguration> routing) {
        _resourceNotFoundHandler = resourceNotFoundHandler;
        _methodNotAllowedHandler = methodNotAllowedHandler;
        _requestLogger = requestLogger;
        _routing = routing.Value;

        // A fallback registers as both, so it would otherwise be walked twice - once in its own
        // right and once among the ordinary providers, where being asked early is the whole thing
        // this avoids.
        _handlers = handlers.Where(handler => handler is not IFallbackRequestHandlerProvider)
            .Reverse()
            .Concat(fallbacks.Reverse())
            .ToArray();
    }

    public Task Execute(IExecutionChain chain) {
        var context = chain.Context;

        // What the tables that path-matched but verb-missed said was allowed. Collected across
        // every provider rather than answered by the first, because a later one may well have this
        // path under this verb - and a 405 from the first that recognised the path would shadow it.
        string? allow = null;

        var match = Match(context, context.Request.Path, ref allow);

        if (match != null) {
            return Dispatch(context, match);
        }

        return ResolvedFromSecondarySources(chain, context, allow);
    }

    /// <summary>
    /// The first provider with a handler for <paramref name="path"/>, recording what any that
    /// merely recognised it said was allowed.
    /// </summary>
    private RequestHandlerInfo? Match(IExecutionContext context, string path, ref string? allow) {
        var probe = string.Equals(path, context.Request.Path, StringComparison.Ordinal)
            ? context
            : context.Clone(request: context.Request.Clone(path: path));

        foreach (var provider in _handlers) {
            var handler = provider.GetExecutionRequestHandler(probe);

            if (handler == null) {
                continue;
            }

            if (handler.Handler == null) {
                allow = Merge(allow, handler.Allow);

                continue;
            }

            return handler;
        }

        return null;
    }

    private Task Dispatch(IExecutionContext context, RequestHandlerInfo match) {
        context.Request.PathTokens = match.PathTokens;
        context.HandlerInfo = match.Handler!.HandlerInfo;

        _requestLogger.RequestMapped(context);

        var handlerChain = match.Handler.GetExecutionChain(context);

        // A HEAD reaches the GET handler - the routing table sends it there - and must run it in
        // full to produce the same headers, so the body is dropped on the way out rather than never
        // asked for.
        if (HeadRequest.IsHead(context)) {
            return HeadRequest.ExecuteWithoutBody(handlerChain, context);
        }

        return handlerChain.Next();
    }

    /// <remarks>
    /// Static content used to be tried here, ahead of everything else in this method, by calling a
    /// handler directly. It is an <see cref="IFallbackRequestHandlerProvider"/> now, which puts it
    /// inside <see cref="Match"/> instead and after every ordinary provider. The observable order is
    /// unchanged: a route an application declared still wins, a file still beats both the
    /// trailing-slash alternative and the 405, and only a request nothing has a file for reaches
    /// the bottom of this method. What changed is that a static request now runs a handler's filter
    /// chain, so authorization applies to it.
    /// </remarks>
    private async Task ResolvedFromSecondarySources(
        IExecutionChain chain, IExecutionContext context, string? allow) {
        // The other spelling, when the application asked for one. Tried after static content, so a
        // file that exists at the path as written still wins, and before the 405, because a route
        // reached by normalising is a route that answers this verb.
        if (await TrailingSlash(context)) {
            return;
        }

        // 405 before 404, and only after static content: a path that a table recognised under
        // another verb is a resource that exists, which is the whole distinction. API Gateway and
        // CloudFront cache the two differently, and a generated client reads them differently.
        if (allow != null) {
            await _methodNotAllowedHandler.Handle(context, allow);

            return;
        }

        await _resourceNotFoundHandler.Handle(chain);
    }

    /// <summary>
    /// Whether the request was answered by the route the path would have matched with its trailing
    /// slash added or removed.
    /// </summary>
    /// <remarks>
    /// <c>/orders</c> and <c>/orders/</c> are unrelated routes and always have been, so a route a
    /// client might link either way had to be declared twice. Off unless the application asks:
    /// strict is what an OpenAPI document says, and it is what every existing application already
    /// behaves as.
    /// </remarks>
    private async Task<bool> TrailingSlash(IExecutionContext context) {
        var policy = _routing.TrailingSlash;

        if (policy == Configuration.TrailingSlash.Strict) {
            return false;
        }

        var alternative = Alternative(context.Request.Path);

        if (alternative == null) {
            return false;
        }

        // Deliberately does not accumulate into the caller's allow. A 405 should report what the
        // path as written answers; folding in another path's verbs would tell a client it may call
        // something at a URL where it may not.
        string? ignored = null;

        var match = Match(context, alternative, ref ignored);

        if (match == null) {
            return false;
        }

        if (policy == Configuration.TrailingSlash.Redirect) {
            context.Response.Status = 308;
            context.Response.Headers[KnownHeaders.Location] = new StringValues(alternative);
            context.Response.ShouldSerialize = false;

            return true;
        }

        await Dispatch(context, match);

        return true;
    }

    /// <summary>
    /// The same path with its trailing slash added or removed, or null when there is no other
    /// spelling - the root is <c>/</c> either way.
    /// </summary>
    private static string? Alternative(string path) {
        if (path.Length <= 1) {
            return null;
        }

        return path[path.Length - 1] == '/'
            ? path.Substring(0, path.Length - 1)
            : path + "/";
    }

    /// <summary>
    /// Two tables' allowed verbs as one header value.
    /// </summary>
    /// <remarks>
    /// Rare - it needs two providers declaring the same path under different verbs - but the
    /// alternative is reporting one of them and leaving a client to conclude the other verb is
    /// unavailable when it is not.
    /// </remarks>
    private static string? Merge(string? existing, string? addition) {
        if (string.IsNullOrEmpty(addition)) {
            return existing;
        }

        if (string.IsNullOrEmpty(existing)) {
            return addition;
        }

        return existing + ", " + addition;
    }
}
