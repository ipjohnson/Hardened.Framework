using System.Text;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Middleware;
using Hardened.Shared.Runtime.Collections;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Web.Lambda.Runtime.Impl
{
    public interface IApiGatewayEventProcessor
    {
        Task<APIGatewayHttpApiV2ProxyResponse> Process(APIGatewayHttpApiV2ProxyRequest request, ILambdaContext context);
    }

    public class ApiGatewayEventProcessor : IApiGatewayEventProcessor
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IMiddlewareService _middlewareService;
        private readonly IMemoryStreamPool _memoryStreamPool;

        public ApiGatewayEventProcessor(
            IServiceProvider serviceProvider, IMiddlewareService middlewareService, IMemoryStreamPool memoryStreamPool)
        {
            _serviceProvider = serviceProvider;
            _middlewareService = middlewareService;
            _memoryStreamPool = memoryStreamPool;
        }

        public async Task<APIGatewayHttpApiV2ProxyResponse> Process(APIGatewayHttpApiV2ProxyRequest request, ILambdaContext context)
        {
            context.Logger.LogInformation("Got here");
            
            var response = new APIGatewayHttpApiV2ProxyResponse
            {
                Headers = new Dictionary<string, string>(),
                StatusCode = 200
            };
            
            using var scope = _serviceProvider.CreateScope();
            using var memoryStreamReservation = _memoryStreamPool.Get();
            
            var executionContext = CreateExecutionContext(scope, request, response);

            executionContext.Response.Body = memoryStreamReservation.Item;

            var chain = _middlewareService.GetExecutionChain(executionContext);

            await chain.Next();
            
            if (executionContext.Response.Status.HasValue)
            {
                response.StatusCode = executionContext.Response.Status.Value;
            }
            
            response.Headers["Content-Type"] = executionContext.Response.ContentType;

            response.Body = Encoding.UTF8.GetString(memoryStreamReservation.Item.ToArray());

            context.Logger.LogInformation("Finished here");

            return response;
        }

        private IExecutionContext CreateExecutionContext(IServiceScope scope,
            APIGatewayHttpApiV2ProxyRequest request,
            APIGatewayHttpApiV2ProxyResponse response)
        {
            return new ApiGatewayV2ExecutionContext(
                _serviceProvider,
                scope.ServiceProvider,
                new ApiGatewayV2ExecutionRequest(request),
                new ApiGatewayV2ExecutionResponse(response));
        }
    }
}
