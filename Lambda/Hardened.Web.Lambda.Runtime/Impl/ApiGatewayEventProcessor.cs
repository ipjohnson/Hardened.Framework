using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Logging;
using Hardened.Requests.Abstract.Metrics;
using Hardened.Requests.Abstract.Middleware;
using Hardened.Requests.Runtime.Execution;
using Hardened.Shared.Runtime.Collections;
using Hardened.Shared.Runtime.Diagnostics;
using Hardened.Shared.Runtime.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MSLogging = Microsoft.Extensions.Logging;

namespace Hardened.Web.Lambda.Runtime.Impl
{
    public interface IApiGatewayEventProcessor
    {
        Task<APIGatewayHttpApiV2ProxyResponse> Process(APIGatewayHttpApiV2ProxyRequest request, ILambdaContext context);
    }

    public partial class ApiGatewayEventProcessor : IApiGatewayEventProcessor
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IMiddlewareService _middlewareService;
        private readonly IMemoryStreamPool _memoryStreamPool;
        private readonly IRequestLogger _requestLogger;
        private readonly IMetricLoggerProvider _metricLoggerProvider;
        private readonly MSLogging.ILogger<ApiGatewayEventProcessor> _logger;

        public ApiGatewayEventProcessor(
            IServiceProvider serviceProvider, 
            IMiddlewareService middlewareService, 
            IMemoryStreamPool memoryStreamPool,
            IRequestLogger requestLogger, 
            MSLogging.ILogger<ApiGatewayEventProcessor> logger, 
            IMetricLoggerProvider metricLoggerProvider)
        {
            _serviceProvider = serviceProvider;
            _middlewareService = middlewareService;
            _memoryStreamPool = memoryStreamPool;
            _requestLogger = requestLogger;
            _logger = logger;
            _metricLoggerProvider = metricLoggerProvider;
        }

        public async Task<APIGatewayHttpApiV2ProxyResponse> Process(APIGatewayHttpApiV2ProxyRequest request, ILambdaContext context)
        {
            var requestStartTimestamp = MachineTimestamp.Now;

            var response = new APIGatewayHttpApiV2ProxyResponse
            {
                Headers = new Dictionary<string, string>(),
                StatusCode = 200
            };
            
            using var scope = _serviceProvider.CreateScope();
            using var memoryStreamReservation = _memoryStreamPool.Get();
            
            var executionContext = CreateExecutionContext(scope, request, response, requestStartTimestamp);

            executionContext.Response.Body = memoryStreamReservation.Item;

            _requestLogger.RequestBegin(executionContext);

            var chain = _middlewareService.GetExecutionChain(executionContext);

            await chain.Next();
            
            if (executionContext.Response.Status.HasValue)
            {
                response.StatusCode = executionContext.Response.Status.Value;
            }
            
            response.Headers["Content-Type"] = executionContext.Response.ContentType;

            response.Body = Encoding.UTF8.GetString(memoryStreamReservation.Item.ToArray());
            
            executionContext.RequestMetrics.Record(RequestMetrics.TotalRequestDuration, requestStartTimestamp.GetElapsedMilliseconds());

            _requestLogger.RequestEnd(executionContext);
            executionContext.RequestMetrics.Dispose();

            return response;
        }

        private IExecutionContext CreateExecutionContext(IServiceScope scope,
            APIGatewayHttpApiV2ProxyRequest request,
            APIGatewayHttpApiV2ProxyResponse response, 
            MachineTimestamp starTime)
        {
            return new ApiGatewayV2ExecutionContext(
                _serviceProvider,
                scope.ServiceProvider,
                new ApiGatewayV2ExecutionRequest(request),
                new ApiGatewayV2ExecutionResponse(response),
                _metricLoggerProvider.CreateLogger("HardenedRequests"), 
                starTime);
        }
    }
}
