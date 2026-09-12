using System.Net;
using Hardened.Azure.Functions.Runtime.Adapters;
using Hardened.Azure.Functions.Runtime.Execution;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Hardened.Azure.Functions.Http;

/// <summary>
/// The HTTP trigger: one anonymous function catching every method under every path.
/// </summary>
/// <remarks>
/// <para>
/// Web-shaped: the handler sees a path, a query string, headers, cookies and a body, and answers
/// with a status. A throw is answered with a 500 rather than rethrown, because the caller is on
/// the other end of an HTTP connection and a failed invocation would give them a 500 with the
/// host's text in it rather than the status and body the application chose.
/// </para>
/// <para>
/// <b>Streams both ways, no base64 branch.</b> <c>HttpRequestData.Body</c> is a stream the host
/// filled and <c>HttpResponseData.Body</c> is a stream the host reads, so the request's body is
/// handed to the pipeline as it is and the response's is the buffer the pipeline wrote - which is
/// the one thing API Gateway, with its string-bodied proxy event, could not offer.
/// </para>
/// <para>
/// Routed by Hardened's table, not the host's: the function's route is <c>{*path}</c>, the host
/// hands over what it matched, and <c>GET /orders/{id}</c> is dispatched exactly as it is on
/// Kestrel.
/// </para>
/// </remarks>
public sealed class HttpAdapter : ITriggerAdapter {
    /// <summary>Whether the shim was generated for this family, which is a type check.</summary>
    public bool Handles(FunctionsTrigger trigger) => trigger.Data is HttpRequestData;

    public IExecutionRequest CreateRequest(FunctionsTrigger trigger, FunctionContext context) =>
        new HttpFunctionRequest((HttpRequestData)trigger.Data, RoutedPath(context));

    /// <summary>
    /// The path Hardened routes on: what the host matched under <c>{*path}</c>, which is the
    /// request's path with the host's own route prefix taken off.
    /// </summary>
    /// <remarks>
    /// The host serves every function under a prefix - <c>api</c> unless <c>host.json</c> says
    /// otherwise - and a route registered as <c>/orders/{id}</c> has no idea of it. The route
    /// value is the path as the application would see it on any other host, and reading it is what
    /// keeps the prefix a deployment setting. Null where nothing routed, as under the envelope
    /// tier's test host, which then falls back to the URL's own path.
    /// </remarks>
    internal static string? RoutedPath(FunctionContext context) =>
        context.BindingContext.BindingData.TryGetValue("path", out var path) && path is string routed
            ? "/" + routed.TrimStart('/')
            : null;

    /// <summary>
    /// Answered rather than rethrown. The caller is waiting on a connection, and a failed
    /// invocation would give them the host's 500 with nothing the application chose in it.
    /// </summary>
    public HostFailurePolicy FailurePolicy => HostFailurePolicy.Answer500;

    public IExecutionResponse CreateResponse(Stream output) => new HttpFunctionResponse(output);

    /// <summary>
    /// The response the host sends: status, headers, cookies, and the buffer the pipeline wrote
    /// as the body, without a copy.
    /// </summary>
    public ValueTask<object?> WriteResponse(IExecutionContext context, FunctionContext functionContext) {
        var response = (HttpFunctionResponse)context.Response;
        var request = ((HttpFunctionRequest)context.Request).Data;

        var data = request.CreateResponse();

        // Null means "handled, no opinion" and becomes 200; the not-found handler has set a 404
        // by now if the routing table did not match.
        data.StatusCode = (HttpStatusCode)(response.Status ?? 200);

        foreach (var header in response.Headers) {
            data.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
        }

        foreach (var cookie in response.Cookies.Cookies) {
            // Item1 is the value and Item2 the options; see LambdaHttpAdapter for the tuple's history.
            data.Cookies.Append(Cookie(cookie.Key, cookie.Value.Item1, cookie.Value.Item2));
        }

        var body = response.Body;

        body.Position = 0;
        data.Body = body;

        return new ValueTask<object?>(data);
    }

    /// <summary>
    /// The worker's cookie, which the host renders as the Set-Cookie header, from the options the
    /// application set.
    /// </summary>
    /// <remarks>
    /// The worker's <c>SameSite.None</c> means "unspecified" and <c>ExplicitNone</c> is what
    /// writes <c>SameSite=None</c>, which is the opposite of what the names suggest.
    /// </remarks>
    private static HttpCookie Cookie(string name, string value, CookieSetOptions options) =>
        new(name, value) {
            Expires = options.Expires.HasValue
                ? new DateTimeOffset(options.Expires.Value.ToUniversalTime(), TimeSpan.Zero)
                : null,
            MaxAge = options.MaxAge,
            Domain = options.Domain,
            Path = options.Path,
            Secure = options.Secure,
            HttpOnly = options.HttpOnly,
            SameSite = options.SameSite switch {
                Hardened.Requests.Abstract.Headers.SameSite.Strict => Microsoft.Azure.Functions.Worker.Http.SameSite.Strict,
                Hardened.Requests.Abstract.Headers.SameSite.Lax => Microsoft.Azure.Functions.Worker.Http.SameSite.Lax,
                Hardened.Requests.Abstract.Headers.SameSite.None => Microsoft.Azure.Functions.Worker.Http.SameSite.ExplicitNone,
                _ => Microsoft.Azure.Functions.Worker.Http.SameSite.None
            }
        };
}
