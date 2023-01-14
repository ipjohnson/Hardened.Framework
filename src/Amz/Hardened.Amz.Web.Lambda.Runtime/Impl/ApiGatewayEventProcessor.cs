using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Hardened.Amz.Shared.Lambda.Runtime.Execution;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Logging;
using Hardened.Requests.Abstract.Metrics;
using Hardened.Requests.Abstract.Middleware;
using Hardened.Shared.Runtime.Collections;
using Hardened.Shared.Runtime.Diagnostics;
using Hardened.Shared.Runtime.Metrics;
using Microsoft.Extensions.DependencyInjection;
using System.Text;
using MSLogging = Microsoft.Extensions.Logging;

namespace Hardened.Amz.Web.Lambda.Runtime.Impl;

public interface IApiGatewayEventProcessor
{
    Task<APIGatewayHttpApiV2ProxyResponse> Process(APIGatewayHttpApiV2ProxyRequest request, ILambdaContext context);
}

public partial class ApiGatewayEventProcessor : IApiGatewayEventProcessor
{
    private static readonly MemoryStream _emptyStream = new (Array.Empty<byte>());
    private readonly IServiceProvider _serviceProvider;
    private readonly IMiddlewareService _middlewareService;
    private readonly IMemoryStreamPool _memoryStreamPool;
    private readonly IRequestLogger _requestLogger;
    private readonly IMetricLoggerProvider _metricLoggerProvider;
    private readonly MSLogging.ILogger<ApiGatewayEventProcessor> _logger;
    private readonly IStringBuilderPool _stringBuilderPool;
    private readonly IKnownServices _knownServices;
    private readonly ILambdaContextAccessor _lambdaContextAccessor;

    public ApiGatewayEventProcessor(
        IServiceProvider serviceProvider, 
        IMiddlewareService middlewareService, 
        IMemoryStreamPool memoryStreamPool,
        IRequestLogger requestLogger, 
        MSLogging.ILogger<ApiGatewayEventProcessor> logger, 
        IMetricLoggerProvider metricLoggerProvider,
        IKnownServices knownServices, 
        ILambdaContextAccessor lambdaContextAccessor, 
        IStringBuilderPool stringBuilderPool)
    {
        _serviceProvider = serviceProvider;
        _middlewareService = middlewareService;
        _memoryStreamPool = memoryStreamPool;
        _requestLogger = requestLogger;
        _logger = logger;
        _metricLoggerProvider = metricLoggerProvider;
        _knownServices = knownServices;
        _lambdaContextAccessor = lambdaContextAccessor;
        _stringBuilderPool = stringBuilderPool;
    }

    public async Task<APIGatewayHttpApiV2ProxyResponse> Process(APIGatewayHttpApiV2ProxyRequest request, ILambdaContext context)
    {
        var requestStartTimestamp = MachineTimestamp.Now;
        
        _lambdaContextAccessor.Context = context;

        var response = new APIGatewayHttpApiV2ProxyResponse
        {
            StatusCode = 200
        };
            
        using var scope = _serviceProvider.CreateScope();
        using var memoryStreamReservation = _memoryStreamPool.Get();
        using var inputBodyStreamReservation = _memoryStreamPool.Get();
            
        var executionContext = CreateExecutionContext(scope, request, response, requestStartTimestamp, inputBodyStreamReservation.Item);

        executionContext.Response.Body = memoryStreamReservation.Item;

        _requestLogger.RequestBegin(executionContext);

        var chain = _middlewareService.GetExecutionChain(executionContext);

        await chain.Next();
            
        if (executionContext.Response.Status.HasValue)
        {
            response.StatusCode = executionContext.Response.Status.Value;
        }

        CopyHeadersAndCookies(executionContext, response);

        if (executionContext.Response.IsBinary)
        {
            response.IsBase64Encoded = true;
            response.Body = Convert.ToBase64String(memoryStreamReservation.Item.ToArray());
        }
        else
        {
            response.Body = Encoding.UTF8.GetString(memoryStreamReservation.Item.ToArray());
        }

        executionContext.RequestMetrics.Record(RequestMetrics.TotalRequestDuration, requestStartTimestamp.GetElapsedMilliseconds());

        _requestLogger.RequestEnd(executionContext);
        executionContext.RequestMetrics.Dispose();

        return response;
    }

    private void CopyHeadersAndCookies(IExecutionContext executionContext, APIGatewayHttpApiV2ProxyResponse response)
    {
        response.Headers = executionContext.Response.Headers.ToStringDictionary();

        var cookies = executionContext.Response.Cookies.Cookies;

        if (cookies.Count > 0)
        {
            using var stringBuilderReservation = _stringBuilderPool.Get();
            var stringBuilder = stringBuilderReservation.Item;
            var cookieArray = new string[cookies.Count];
            var i = 0;
            foreach (var cookiePair in cookies)
            {
                stringBuilder.Append(cookiePair.Key);
                stringBuilder.Append('=');
                stringBuilder.Append(cookiePair.Value);
                cookiePair.Value.Item2.AppendSettings(stringBuilder);
                cookieArray[i] = stringBuilder.ToString();
                i++;
                stringBuilder.Clear();
            }

            response.Cookies = cookieArray;
        }
        else
        {
            response.Cookies = Array.Empty<string>();
        }
    }

    private IExecutionContext CreateExecutionContext(IServiceScope scope,
        APIGatewayHttpApiV2ProxyRequest request,
        APIGatewayHttpApiV2ProxyResponse response,
        MachineTimestamp starTime, MemoryStream memoryStream)
    {
        return new ApiGatewayV2ExecutionContext(
            _serviceProvider,
            scope.ServiceProvider,
            _knownServices,
            new ApiGatewayV2ExecutionRequest(request) {Body = CreateBodyFromRequest(request, memoryStream)},
            new ApiGatewayV2ExecutionResponse(response),
            _metricLoggerProvider.CreateLogger("HardenedRequests"), 
            starTime);
    }

    private Stream CreateBodyFromRequest(APIGatewayHttpApiV2ProxyRequest request, MemoryStream memoryStream)
    {
        if (string.IsNullOrEmpty(request.Body))
        {
            return _emptyStream;
        }
        
        byte[] bytes = request.IsBase64Encoded ?
            Convert.FromBase64String(request.Body) :
            Encoding.UTF8.GetBytes(request.Body);

        memoryStream.Write(bytes, 0, bytes.Length);
        memoryStream.Position = 0;

        return memoryStream;
    }
}