using Amazon.Lambda.APIGatewayEvents;
using Hardened.Shared.Lambda.Testing;
using Hardened.Web.Lambda.Runtime.Impl;

namespace Hardened.Web.Lambda.Harness
{
    public interface IRequestToLambdaService
    {
        Task HandleRequest(HttpContext context, Func<Task> next);
    }

    public class RequestToLambdaService<T> : IRequestToLambdaService where T : IApiGatewayV2Handler, new()
    {
        private readonly IApiGatewayV2Handler _handler;

        public RequestToLambdaService()
        {
            _handler = new T();
        }
        public async Task HandleRequest(HttpContext context, Func<Task> next)
        {
            var request = await ConvertHttpContextToRequest(context);

            var response =
                await _handler.Invoke(request, TestLambdaContext.FromName(typeof(T).Name));

            await SendResponse(context, response);
        }

        private async Task SendResponse(HttpContext context, APIGatewayHttpApiV2ProxyResponse response)
        {
            context.Response.StatusCode = response.StatusCode;

            CopyHeaders(context.Response.Headers, response.Headers);

            if (response.IsBase64Encoded)
            {
                var contentBytes = Convert.FromBase64String(response.Body);

                await context.Response.BodyWriter.WriteAsync(contentBytes, context.RequestAborted);
            }
            else
            {
                await context.Response.WriteAsync(response.Body);
            }
        }

        private void CopyHeaders(IHeaderDictionary responseHeaders, IDictionary<string, string> headers)
        {
            foreach (var kvpHeader in headers)
            {
                responseHeaders[kvpHeader.Key] = kvpHeader.Value;
            }
        }

        private async Task<APIGatewayHttpApiV2ProxyRequest> ConvertHttpContextToRequest(HttpContext context)
        {
            var httpRequest = context.Request;
            var request = new APIGatewayHttpApiV2ProxyRequest();

            request.RequestContext = new APIGatewayHttpApiV2ProxyRequest.ProxyRequestContext
            {
                Http = new APIGatewayHttpApiV2ProxyRequest.HttpDescription
                {
                    Method = httpRequest.Method,
                    Path = httpRequest.Path,
                    Protocol = httpRequest.Protocol,
                    UserAgent = httpRequest.Headers.UserAgent
                }
            };
            request.Headers = new Dictionary<string, string>();
            request.RawPath = context.Request.Path;
            request.RawQueryString = context.Request.QueryString.ToString().TrimStart('?');

            request.QueryStringParameters = new Dictionary<string, string>();

            foreach (var queryPair in context.Request.Query)
            {
                request.QueryStringParameters[queryPair.Key] = queryPair.Value;
            }

            if (context.Response.Body.CanRead)
            {
                using var textReader = new StreamReader(context.Response.Body);

                request.Body = await textReader.ReadToEndAsync();
            }
            else
            {
                request.Body = "";
            }

            return request;
        }
    }
}
