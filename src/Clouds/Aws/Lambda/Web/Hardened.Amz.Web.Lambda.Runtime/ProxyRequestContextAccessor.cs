using Amazon.Lambda.APIGatewayEvents;
using DependencyModules.Runtime.Attributes;

namespace Hardened.Amz.Web.Lambda.Runtime;

public interface IProxyRequestContextAccessor {
    APIGatewayHttpApiV2ProxyRequest.ProxyRequestContext ProxyRequestContext { get; set; }
}

[SingletonService(Using = RegistrationType.Try)]
public class ProxyRequestContextAccessor : IProxyRequestContextAccessor {
    /// <summary>
    /// The request context of the invocation in flight, set by <c>ApiGatewayEventProcessor</c>
    /// before the middleware chain runs.
    /// </summary>
    /// <remarks>
    /// Initialised rather than left default (CS8618). The interface promises non-null, and this is
    /// a singleton: anything resolving it outside an invocation — a startup service, a filter on a
    /// forked chain — would otherwise be handed the null the contract says cannot happen. An empty
    /// context reads as "no request", which is what is true at that point.
    /// </remarks>
    public APIGatewayHttpApiV2ProxyRequest.ProxyRequestContext ProxyRequestContext { get; set; } =
        new();
}