using Amazon.Lambda.APIGatewayEvents;

namespace Hardened.Amz.Web.Lambda.Runtime;

public interface IProxyRequestContextAccessor {
    APIGatewayHttpApiV2ProxyRequest.ProxyRequestContext ProxyRequestContext { get; set; }
}

public class ProxyRequestContextAccessor : IProxyRequestContextAccessor{
    public APIGatewayHttpApiV2ProxyRequest.ProxyRequestContext ProxyRequestContext { get; set; }
}