using Amazon.Lambda.APIGatewayEvents;
using Hardened.Amz.Shared.Lambda.Testing;
using Hardened.Amz.Web.Lambda.Runtime.Impl;

namespace Hardened.Amz.Web.Lambda.Harness;

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

        CopyHeadersToResponse(response.Headers, context.Response.Headers);

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

    private void CopyHeadersToResponse(IDictionary<string, string> headers,IHeaderDictionary responseHeaders)
    {
        foreach (var kvpHeader in headers)
        {
            responseHeaders[kvpHeader.Key] = kvpHeader.Value;
        }
    }
    
    private void CopyHeadersFromRequest(IHeaderDictionary requestHeaders,IDictionary<string, string> headers)
    {
        foreach (var kvpHeader in requestHeaders)
        {
            headers[kvpHeader.Key] = kvpHeader.Value;
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
        
        request.RawPath = context.Request.Path;
        
        request.Headers = new Dictionary<string, string>();
        CopyHeadersFromRequest(context.Request.Headers, request.Headers);
        
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