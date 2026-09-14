using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.PathTokens;
using Hardened.Web.Runtime.Responses;

namespace Hardened.Web.Runtime.Handlers;

/// <summary>
/// What a routing table found for a request.
/// </summary>
/// <param name="Handler">
/// What will answer, or null when the path matched but this verb has no route on it.
/// </param>
/// <param name="Allow">
/// The verbs that <em>are</em> declared at this path, when <paramref name="Handler"/> is null.
/// </param>
/// <remarks>
/// <para>
/// The null-handler case is what a 405 is made of. The table already knew: the leaf switch fell to
/// <c>default: return null</c> <em>having matched the path</em>, and threw that away - so a request
/// to a real resource with the wrong verb came back indistinguishable from a request to a URL
/// nobody declared. Every peer except Express returns 405, it is in RFC 9110, API Gateway and
/// CloudFront cache the two differently, and generated clients expect it.
/// </para>
/// <para>
/// It is reported rather than answered here because providers are consulted in turn: another one
/// may have this path under this verb, and answering 405 from the first that path-matched would
/// shadow it. <c>WebExecutionHandlerService</c> collects what was allowed and only answers once
/// every provider has declined.
/// </para>
/// <para>
/// <b>Nothing here is per request.</b> It used to carry the matched route's token values, which is
/// what stopped a route with a token in it reusing one of these: the handler is a singleton the
/// table builds once, and the verb set is fixed at compile time, but the values were not. The
/// values go into <see cref="IExecutionRequest.PathTokens"/> instead - storage the request already
/// owns - so every leaf of every table now answers with one record built on first use and held in a
/// field, and a match allocates nothing but the strings the route captured.
/// </para>
/// </remarks>
public record RequestHandlerInfo(IExecutionRequestHandler? Handler, string? Allow = null)
{
    /// <summary>
    /// A path that matched under other verbs. Built once per distinct verb set by the generated
    /// table, since it carries nothing per request.
    /// </summary>
    public static RequestHandlerInfo MethodNotAllowed(string allow) => new(null, allow);
}

/// <param name="pathTokens">
/// Where a provider that matched a route with tokens in it writes their values. Owned by the
/// caller, which puts it on the request once it has decided which provider answered - a table is
/// asked about a path it may not have, and one that declines must not have changed anything.
/// </param>
public interface IWebExecutionRequestHandlerProvider
{
    RequestHandlerInfo? GetExecutionRequestHandler(
        IExecutionContext context,
        ref PathTokenCollection pathTokens
    );
}
