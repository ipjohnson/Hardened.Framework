using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.PathTokens;
using Hardened.Web.Runtime.Handlers;

namespace Hardened.Web.StaticContent.Tests;

/// <summary>
/// Asks a provider what answers a request, supplying the token destination.
/// </summary>
/// <remarks>
/// The destination belongs to <c>WebExecutionHandlerService</c>, which puts what a match wrote on
/// the request once it has decided which provider answered. A test asking one provider directly has
/// no service to own it, and nothing a static mount matches binds a token anyway.
/// </remarks>
internal static class ProviderMatch
{
    public static RequestHandlerInfo? Match(
        this IWebExecutionRequestHandlerProvider provider,
        IExecutionContext context
    )
    {
        var pathTokens = default(PathTokenCollection);

        return provider.GetExecutionRequestHandler(context, ref pathTokens);
    }
}
