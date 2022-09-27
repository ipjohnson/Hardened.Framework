using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;

namespace Hardened.Web.Lambda.Runtime.Impl
{
    public interface IApiGatewayV2Handler
    {
        Task<APIGatewayHttpApiV2ProxyResponse> Invoke(
            APIGatewayHttpApiV2ProxyRequest request, ILambdaContext context);
    }
}
