using Amazon.Lambda.APIGatewayEvents;
using Hardened.Amz.Shared.Lambda.Runtime.Streaming;
using Hardened.Amz.Shared.Lambda.Testing;
using Hardened.Amz.Web.Lambda.Runtime.Impl;
using Hardened.Shared.Runtime.Application;
using Microsoft.Extensions.Options;

namespace Hardened.Amz.Web.Lambda.Harness;

public interface IRequestToLambdaService {
    Task HandleRequest(HttpContext context, Func<Task> next);
}

/// <summary>
/// An HTTP request in, an API Gateway event out, and the application's response back onto the
/// HTTP response - as the payload when the application is buffered, as a stream when it is
/// deployed in stream mode.
/// </summary>
/// <remarks>
/// The mode is the application's own <see cref="ILambdaResponseModeConfiguration"/>, read from its
/// container, so the harness serves what the deployment would. A handler that is not a Hardened
/// application - a hand-written <see cref="IApiGatewayV2Handler"/> - is buffered.
/// </remarks>
public class RequestToLambdaService<T> : IRequestToLambdaService where T : IApiGatewayV2Handler, new() {
    private readonly IApiGatewayV2Handler _handler;
    private readonly IStreamingEventProcessor? _streaming;

    public RequestToLambdaService() {
        _handler = new T();
        _streaming = StreamingProcessor(_handler);
    }

    private static IStreamingEventProcessor? StreamingProcessor(IApiGatewayV2Handler handler) {
        if (handler is not IApplicationRoot application) {
            return null;
        }

        var services = application.Provider;
        var mode = services.GetRequiredService<IOptions<ILambdaResponseModeConfiguration>>().Value.Mode;

        return mode == LambdaResponseMode.Stream
            ? services.GetRequiredService<IStreamingEventProcessor>()
            : null;
    }

    public async Task HandleRequest(HttpContext context, Func<Task> next) {
        var request = await ConvertHttpContextToRequest(context);
        var lambdaContext = TestLambdaContext.FromName(typeof(T).Name);

        if (_streaming != null) {
            await _streaming.Process(request, lambdaContext, new HttpResponseStreamFactory(context));

            return;
        }

        var response = await _handler.Invoke(request, lambdaContext);

        await SendResponse(context, response);
    }

    private async Task SendResponse(HttpContext context, APIGatewayHttpApiV2ProxyResponse response) {
        context.Response.StatusCode = response.StatusCode;

        CopyHeadersToResponse(response.Headers, context.Response.Headers);

        if (response.IsBase64Encoded) {
            var contentBytes = Convert.FromBase64String(response.Body);

            await context.Response.BodyWriter.WriteAsync(contentBytes, context.RequestAborted);
        }
        else {
            await context.Response.WriteAsync(response.Body);
        }
    }

    private void CopyHeadersToResponse(IDictionary<string, string> headers, IHeaderDictionary responseHeaders) {
        
        foreach (var kvpHeader in headers) {
            responseHeaders[kvpHeader.Key] = kvpHeader.Value;
        }
    }

    private void CopyHeadersFromRequest(IHeaderDictionary requestHeaders, IDictionary<string, string> headers) {
        foreach (var kvpHeader in requestHeaders) {
            // ToString() rather than the implicit StringValues conversion, which is nullable
            // (CS8601). Repeated headers join on "," here, which is what API Gateway's payload
            // format 2.0 does before a real invocation ever reaches the function.
            headers[kvpHeader.Key] = kvpHeader.Value.ToString();
        }
    }

    private async Task<APIGatewayHttpApiV2ProxyRequest> ConvertHttpContextToRequest(HttpContext context) {
        var httpRequest = context.Request;
        var request = new APIGatewayHttpApiV2ProxyRequest();

        request.RequestContext = new APIGatewayHttpApiV2ProxyRequest.ProxyRequestContext {
            Http = new APIGatewayHttpApiV2ProxyRequest.HttpDescription {
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

        foreach (var queryPair in context.Request.Query) {
            request.QueryStringParameters[queryPair.Key] = queryPair.Value;
        }

        if (context.Request.Body.CanRead) {
            using var textReader = new StreamReader(context.Request.Body);

            request.Body = await textReader.ReadToEndAsync();
        }
        else {
            request.Body = "";
        }

        return request;
    }
}
