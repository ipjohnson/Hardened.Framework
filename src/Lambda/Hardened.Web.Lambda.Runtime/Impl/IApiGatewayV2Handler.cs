using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hardened.Web.Lambda.Runtime.Impl
{
    public interface IApiGatewayV2Handler
    {
        Task<APIGatewayHttpApiV2ProxyResponse> Invoke(
            APIGatewayHttpApiV2ProxyRequest request, ILambdaContext context);
    }
}
