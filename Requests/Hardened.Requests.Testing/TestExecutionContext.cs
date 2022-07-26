using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Runtime.Execution;
using Hardened.Shared.Runtime.Diagnostics;
using Hardened.Shared.Runtime.Metrics;

namespace Hardened.Requests.Testing
{
    public class TestExecutionContext : IExecutionContext
    {
        public TestExecutionContext(
            IServiceProvider rootServiceProvider,
            IServiceProvider requestServices,
            IExecutionRequest request,
            IExecutionResponse response)
        {
            RootServiceProvider = rootServiceProvider;
            RequestServices = requestServices;
            Request = request;
            Response = response;
            RequestMetrics = new NullMetricsLogger();
            StartTime = MachineTimestamp.Now;
        }

        public object Clone()
        {
            throw new NotImplementedException();
        }

        public IServiceProvider RootServiceProvider { get; }

        public IServiceProvider RequestServices { get; }

        public IExecutionRequest Request { get; }

        public IExecutionResponse Response { get; }

        public object? HandlerInstance { get; set; }

        public IExecutionRequestHandlerInfo? HandlerInfo { get; set; }

        public DefaultOutputFunc? DefaultOutput { get; set; }

        public IMetricLogger RequestMetrics { get; }

        public MachineTimestamp StartTime { get; }
    }
}
