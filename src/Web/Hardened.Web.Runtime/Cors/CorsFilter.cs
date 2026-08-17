using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Web.Runtime.Handlers;
using Microsoft.Extensions.Primitives;

namespace Hardened.Web.Runtime.Cors;

/// <summary>
/// Answers cross-origin preflights, and marks cross-origin responses so a browser will let a script
/// read them.
/// </summary>
/// <remarks>
/// <para>
/// <b>A preflight and an actual request are different responses.</b> Preflight is an
/// <c>OPTIONS</c> carrying <c>Access-Control-Request-Method</c>; it is answered here and goes no
/// further. Everything else continues down the chain and is only annotated on the way past. The
/// previous version treated every <c>OPTIONS</c> as a preflight and answered 204, which meant a
/// handler that legitimately answered <c>OPTIONS</c> could never be reached.
/// </para>
/// <para>
/// <b>Every CORS response varies by <c>Origin</c>.</b> The allow header is built from the request's
/// own <c>Origin</c>, so a cache that stored one without recording that dependency would serve one
/// origin's response - allow header included - to the next origin that asked. That is the single
/// most consequential thing in this file.
/// </para>
/// <para>
/// <b>The advertised verbs come from the routing table.</b> A preflight asks whether a specific
/// verb is allowed at a specific path, and the table already computes the answer for its own 405s.
/// Answering from a configured string instead advertises <c>DELETE</c> on read-only resources.
/// </para>
/// </remarks>
public class CorsFilter : IExecutionFilter {
    private readonly CorsConfiguration _config;
    private readonly IEnumerable<IWebExecutionRequestHandlerProvider> _routing;

    /// <param name="routing">
    /// The routing tables, used only to answer "which verbs does this path have". Empty is fine -
    /// an application with no web routing falls back to the configured verb list.
    /// </param>
    public CorsFilter(
        CorsConfiguration config,
        IEnumerable<IWebExecutionRequestHandlerProvider>? routing = null) {
        _config = config;
        _routing = routing ?? Array.Empty<IWebExecutionRequestHandlerProvider>();
    }

    public Task Execute(IExecutionChain chain) {
        var context = chain.Context;
        var request = context.Request;

        if (!request.Headers.TryGetValue(KnownHeaders.Origin, out var originValues)) {
            return chain.Next();
        }

        var origin = originValues.ToString();

        if (string.IsNullOrEmpty(origin)) {
            return chain.Next();
        }

        // Set whether or not the origin turns out to be allowed. The response was decided by
        // looking at Origin either way, and a cache that stored the refusal without this would
        // replay it to an origin that is allowed.
        context.Response.Headers[KnownHeaders.Vary] = KnownHeaders.Origin;

        var allowed = _config.IsOriginAllowed(origin);

        if (!IsPreflight(request)) {
            if (allowed) {
                WriteActualHeaders(context, origin);
            }

            return chain.Next();
        }

        // A preflight is answered here whatever the verdict. A rejected one simply carries no CORS
        // headers, which is what tells the browser not to send the real request - and it must not
        // reach a handler, because the real request has not happened yet.
        return Preflight(context, origin, allowed);
    }

    /// <summary>
    /// An <c>OPTIONS</c> that names the verb it is asking about. The header is what distinguishes a
    /// preflight from an ordinary <c>OPTIONS</c>, which a handler may want.
    /// </summary>
    private static bool IsPreflight(IExecutionRequest request) =>
        string.Equals(request.Method, "OPTIONS", StringComparison.OrdinalIgnoreCase) &&
        request.Headers.ContainsKey(KnownHeaders.Cors.AccessControlRequestMethod);

    /// <summary>
    /// What an allowed cross-origin response carries. Not the preflight set: <c>Allow-Methods</c>,
    /// <c>Allow-Headers</c> and <c>Max-Age</c> mean nothing here and were only ever noise.
    /// </summary>
    private void WriteActualHeaders(IExecutionContext context, string origin) {
        var headers = context.Response.Headers;

        headers[KnownHeaders.Cors.AccessControlAllowOrigin] = AllowOriginValue(origin);

        if (_config.AllowCredentials && !_config.AllowAnyOrigin) {
            headers[KnownHeaders.Cors.AccessControlAllowCredentials] = "true";
        }

        if (_config.ExposedHeaders.Count > 0) {
            headers[KnownHeaders.Cors.AccessControlExposeHeaders] =
                string.Join(", ", _config.ExposedHeaders);
        }
    }

    private Task Preflight(IExecutionContext context, string origin, bool originAllowed) {
        var response = context.Response;

        // 204 either way, with no body. A preflight is a question about a future request; the
        // answer is entirely in the headers, and a rejected one is a 204 that simply lacks them.
        response.Status = 204;
        response.ShouldSerialize = false;

        if (!originAllowed) {
            return Task.CompletedTask;
        }

        var requestedMethod =
            context.Request.Headers[KnownHeaders.Cors.AccessControlRequestMethod].ToString();

        var requestedHeaders = RequestedHeaders(context);

        // Asking for a header that is not allowed fails the whole preflight rather than being
        // trimmed from the answer. Echoing a subset would have the browser block the real request
        // anyway, having been told the preflight succeeded.
        if (!_config.AreHeadersAllowed(requestedHeaders)) {
            return Task.CompletedTask;
        }

        var allowedMethods = AllowedMethods(context, requestedMethod);

        if (allowedMethods == null) {
            return Task.CompletedTask;
        }

        var headers = response.Headers;

        headers[KnownHeaders.Cors.AccessControlAllowOrigin] = AllowOriginValue(origin);
        headers[KnownHeaders.Cors.AccessControlAllowMethods] = allowedMethods;
        headers[KnownHeaders.Cors.AccessControlMaxAge] = _config.MaxAgeSec.ToString();

        // Echoed rather than listing everything configured, which is what the specification asks
        // for and keeps the header from growing with the configuration.
        if (requestedHeaders.Count > 0) {
            headers[KnownHeaders.Cors.AccessControlAllowHeaders] = string.Join(", ", requestedHeaders);
        }

        if (_config.AllowCredentials && !_config.AllowAnyOrigin) {
            headers[KnownHeaders.Cors.AccessControlAllowCredentials] = "true";
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// The verbs to advertise, or null when the requested one is not among them.
    /// </summary>
    /// <remarks>
    /// Asks the routing tables the same question they answer for a 405: is there a handler here for
    /// this verb, and if not, what verbs does this path have. A path no table recognises falls back
    /// to the configured list, because "no route" is also what a request for static content looks
    /// like.
    /// </remarks>
    private string? AllowedMethods(IExecutionContext context, string requestedMethod) {
        if (string.IsNullOrEmpty(requestedMethod)) {
            return null;
        }

        var probe = context.Clone(
            request: context.Request.Clone(method: requestedMethod));

        string? pathVerbs = null;

        foreach (var provider in _routing) {
            var match = provider.GetExecutionRequestHandler(probe);

            if (match == null) {
                continue;
            }

            if (match.Handler != null) {
                // The requested verb routes. Advertise it alongside whatever else the path has.
                return Merge(pathVerbs, requestedMethod);
            }

            pathVerbs = Merge(pathVerbs, match.Allow);
        }

        // The path exists under other verbs but not this one: a real answer, and a refusal.
        if (pathVerbs != null) {
            return null;
        }

        return _config.FallbackMethods;
    }

    private static List<string> RequestedHeaders(IExecutionContext context) {
        if (!context.Request.Headers.TryGetValue(
                KnownHeaders.Cors.AccessControlRequestHeaders, out var value)) {
            return new List<string>();
        }

        var requested = value.ToString();

        if (string.IsNullOrWhiteSpace(requested)) {
            return new List<string>();
        }

        return requested
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }

    /// <summary>
    /// The origin as written, or <c>*</c> when any is allowed and no credentials are in play.
    /// </summary>
    /// <remarks>
    /// Echoing the caller's origin even under <c>AllowAnyOrigin</c> would be more precise, but
    /// <c>*</c> is cacheable across origins and that is the whole reason to have configured it.
    /// </remarks>
    private StringValues AllowOriginValue(string origin) =>
        _config.AllowAnyOrigin && !_config.AllowCredentials ? "*" : origin;

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
