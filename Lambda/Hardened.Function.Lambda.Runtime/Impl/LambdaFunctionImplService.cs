using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Amazon.Lambda.Core;
using Hardened.Function.Lambda.Runtime.Execution;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.Middleware;
using Hardened.Requests.Runtime.Headers;
using Hardened.Shared.Runtime.Collections;
using Hardened.Shared.Runtime.Diagnostics;
using Hardened.Shared.Runtime.Metrics;
using Hardened.Shared.Runtime.Utilities;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Function.Lambda.Runtime.Impl
{
    public interface ILambdaFunctionImplService
    {
        Task<Stream> InvokeFunction(Stream stream, ILambdaContext context);
    }

    public class LambdaFunctionImplService : ILambdaFunctionImplService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IMiddlewareService _middlewareService;
        private readonly IMemoryStreamPool _memoryStreamPool;
        private readonly IKnownServices _knownServices;

        public LambdaFunctionImplService(
            IMiddlewareService middlewareService,
            IMemoryStreamPool memoryStreamPool, 
            IServiceProvider serviceProvider, 
            IKnownServices knownServices)
        {
            _middlewareService = middlewareService;
            _memoryStreamPool = memoryStreamPool;
            _serviceProvider = serviceProvider;
            _knownServices = knownServices;
        }

        public async Task<Stream> InvokeFunction(Stream stream, ILambdaContext context)
        {
            var now = MachineTimestamp.Now;

            await using var requestContext = _serviceProvider.CreateAsyncScope();
            var responseStream = new MemoryStreamPoolWrapper(_memoryStreamPool.Get());

            var customContext = context.ClientContext.Custom;

            IHeaderCollection headerCollection = customContext != null
                ? new HeaderCollectionStringDictionary(customContext)
                : new HeaderCollectionStringValues();

            var request =
                new LambdaExecutionRequest("Invoke", context.FunctionName, stream, headerCollection);
            var response = new LambdaExecutionResponse(responseStream, new HeaderCollectionStringValues());

            var lambdaExecutionContext = new LambdaExecutionContext(
                _serviceProvider, 
                requestContext.ServiceProvider,
                _knownServices,
                request, 
                response,
                new NullMetricsLogger(),
                now);

            await _middlewareService.GetExecutionChain(lambdaExecutionContext).Next();

            responseStream.Position = 0;

            return responseStream;
        }
    }
}
