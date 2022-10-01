using Hardened.Requests.Abstract.Execution;
using Hardened.Shared.Runtime.Diagnostics;
using Hardened.Shared.Runtime.Metrics;

namespace Hardened.Web.Lambda.Runtime.Impl;

internal class ApiGatewayV2ExecutionContext : IExecutionContext
{
    public ApiGatewayV2ExecutionContext(
        IServiceProvider rootServiceProvider, 
        IServiceProvider requestServices,
        IKnownServices knownServices,
        IExecutionRequest request, 
        IExecutionResponse response, 
        IMetricLogger requestMetrics, 
        MachineTimestamp startTime)
    {
        RootServiceProvider = rootServiceProvider;
        RequestServices = requestServices;
        Request = request;
        Response = response;
        RequestMetrics = requestMetrics;
        StartTime = startTime;
        KnownServices = knownServices;
    }

    public object Clone()
    {
        throw new NotImplementedException();
    }

    public IServiceProvider RootServiceProvider { get; }
    public IKnownServices KnownServices { get; }

    public IServiceProvider RequestServices { get; }
        
    public IExecutionRequest Request { get; }

    public IExecutionResponse Response { get; }

    public object? HandlerInstance { get; set; }

    public IExecutionRequestHandlerInfo? HandlerInfo { get; set; }

    public DefaultOutputFunc? DefaultOutput { get; set; }

    public IMetricLogger RequestMetrics { get; }

    public MachineTimestamp StartTime { get; }
}