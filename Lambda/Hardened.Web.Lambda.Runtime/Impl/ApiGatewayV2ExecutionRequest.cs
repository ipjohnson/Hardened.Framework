using Amazon.Lambda.APIGatewayEvents;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;

namespace Hardened.Web.Lambda.Runtime.Impl
{
    internal class ApiGatewayV2ExecutionRequest : IExecutionRequest
    {
        private readonly APIGatewayHttpApiV2ProxyRequest _proxyRequest;
        private readonly string _path;

        public ApiGatewayV2ExecutionRequest(APIGatewayHttpApiV2ProxyRequest request)
        {
            _proxyRequest = request;
            _path = request.RawPath.Replace("/Beta", "");
        }

        public object Clone()
        {
            throw new NotImplementedException();
        }

        public string Method => _proxyRequest.RequestContext.Http.Method;

        public string Path => _path;

        public string? ContentType => "application/json";

        public string? Accepts => "application/json";

        public IExecutionRequestParameters? Parameters { get; set; }

        public Stream Body { get; set; }
        public IHeaderCollection Headers { get; }
    }
}
