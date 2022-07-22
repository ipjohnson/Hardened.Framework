using Hardened.Requests.Abstract.Execution;
using Hardened.Shared.Runtime.Diagnostics;
using Hardened.Shared.Runtime.Metrics;

namespace Hardened.Web.Lambda.Runtime.Impl
{
    internal class ApiGatewayV2ExecutionContext : IExecutionContext
    {
        public ApiGatewayV2ExecutionContext(
            IServiceProvider rootServiceProvider, 
            IServiceProvider requestServices, 
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
