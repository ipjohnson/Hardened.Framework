using DependencyModules.Runtime.Attributes;
using Hardened.Amz.Function.Lambda.Runtime.Execution;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Runtime.Headers;
using Hardened.Shared.Runtime.Diagnostics;
using Hardened.Shared.Runtime.Metrics;
using Microsoft.Extensions.Primitives;

namespace Hardened.Amz.Function.Lambda.Streaming.Impl;

public interface IStreamingFunctionRequestMapper {
    IExecutionContext CreateExecutionContext(
        IServiceProvider rootServiceProvider,
        IServiceProvider requestServices,
        InvocationData invocation,
        ResponseStream responseStream,
        IMetricLogger metricLogger);
}

[SingletonService(Using = RegistrationType.Try)]
public class StreamingFunctionRequestMapper : IStreamingFunctionRequestMapper {
    private readonly IKnownServices _knownServices;

    public StreamingFunctionRequestMapper(IKnownServices knownServices) {
        _knownServices = knownServices;
    }

    public IExecutionContext CreateExecutionContext(
        IServiceProvider rootServiceProvider,
        IServiceProvider requestServices,
        InvocationData invocation,
        ResponseStream responseStream,
        IMetricLogger metricLogger) {
        var functionName = invocation.LambdaContext.FunctionName;

        var headers = new Dictionary<string, StringValues>();

        var request = new LambdaExecutionRequest(
            "Invoke",
            functionName,
            invocation.Body,
            headers);

        var responseHeaders = new HeaderCollectionStringValues();
        var response = new LambdaExecutionResponse(responseStream, responseHeaders);

        return new LambdaExecutionContext(
            rootServiceProvider,
            requestServices,
            _knownServices,
            request,
            response,
            metricLogger,
            MachineTimestamp.Now);
    }
}
