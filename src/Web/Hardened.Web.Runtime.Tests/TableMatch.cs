using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.PathTokens;
using Hardened.Web.Runtime.Handlers;
using Hardened.Web.Runtime.Routing;

namespace Hardened.Web.Runtime.Tests;

/// <summary>
/// Matching a table or a provider without the service that owns the token destination.
/// </summary>
/// <remarks>
/// A match writes what it captured into storage the request already owns, so
/// <c>WebExecutionHandlerService</c> hands every provider somewhere to put it and puts the result
/// on the request once it knows which one answered. A test driving one table directly has no
/// service to supply that, so these declare it and hand back both halves.
/// </remarks>
internal static class TableMatch
{
    public static RequestHandlerInfo? Matched(
        this RuntimeRouteTable table,
        string path,
        string method
    )
    {
        var pathTokens = default(PathTokenCollection);

        return table.Match(path.AsSpan(), method, ref pathTokens);
    }

    /// <summary>What the route that matched bound, for a match the test expects to succeed.</summary>
    public static PathTokenCollection Tokens(
        this RuntimeRouteTable table,
        string path,
        string method
    )
    {
        var pathTokens = default(PathTokenCollection);

        table.Match(path.AsSpan(), method, ref pathTokens);

        return pathTokens;
    }

    public static RequestHandlerInfo? Match(
        this IWebExecutionRequestHandlerProvider provider,
        IExecutionContext context
    )
    {
        var pathTokens = default(PathTokenCollection);

        return provider.GetExecutionRequestHandler(context, ref pathTokens);
    }

    public static PathTokenCollection Tokens(
        this IWebExecutionRequestHandlerProvider provider,
        IExecutionContext context
    )
    {
        var pathTokens = default(PathTokenCollection);

        provider.GetExecutionRequestHandler(context, ref pathTokens);

        return pathTokens;
    }
}
