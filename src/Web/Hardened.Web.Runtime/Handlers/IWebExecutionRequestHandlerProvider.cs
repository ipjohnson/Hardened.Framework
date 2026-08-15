using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.PathTokens;
using Hardened.Requests.Runtime.PathTokens;

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
/// </remarks>
public record RequestHandlerInfo(
    IExecutionRequestHandler? Handler, PathTokenCollection PathTokens, string? Allow = null) {

    /// <summary>
    /// A path that matched under other verbs. Built once per distinct verb set by the generated
    /// table, since it carries nothing per request.
    /// </summary>
    public static RequestHandlerInfo MethodNotAllowed(string allow) =>
        new(null, PathTokenCollection.Empty, allow);
}

public interface IWebExecutionRequestHandlerProvider {
    RequestHandlerInfo? GetExecutionRequestHandler(IExecutionContext context);
}