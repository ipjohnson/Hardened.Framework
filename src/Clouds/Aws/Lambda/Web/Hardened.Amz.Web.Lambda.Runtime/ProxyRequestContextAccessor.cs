using Amazon.Lambda.APIGatewayEvents;
using DependencyModules.Runtime.Attributes;

namespace Hardened.Amz.Web.Lambda.Runtime;

public interface IProxyRequestContextAccessor {
    APIGatewayHttpApiV2ProxyRequest.ProxyRequestContext ProxyRequestContext { get; set; }
}

[SingletonService(Using = RegistrationType.Try)]
public class ProxyRequestContextAccessor : IProxyRequestContextAccessor{
    public APIGatewayHttpApiV2ProxyRequest.ProxyRequestContext ProxyRequestContext { get; set; }
}