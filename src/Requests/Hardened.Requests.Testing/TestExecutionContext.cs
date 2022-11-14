using Hardened.Requests.Abstract.Execution;
using Hardened.Shared.Runtime.Diagnostics;
using Hardened.Shared.Runtime.Metrics;

namespace Hardened.Requests.Testing;

public class TestExecutionContext : IExecutionContext
{
    public TestExecutionContext(
        IServiceProvider rootServiceProvider,
        IServiceProvider requestServices, 
        IKnownServices knownServices,
        IExecutionRequest request,
        IExecutionResponse response,
        CancellationToken cancellationToken)
    {
        RootServiceProvider = rootServiceProvider;
        RequestServices = requestServices;
        Request = request;
        Response = response;
        KnownServices = knownServices;
        RequestMetrics = new NullMetricsLogger();
        StartTime = MachineTimestamp.Now;
        CancellationToken = cancellationToken;
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

    public CancellationToken CancellationToken { get; }
}