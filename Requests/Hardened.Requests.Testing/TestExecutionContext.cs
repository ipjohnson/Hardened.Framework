using Hardened.Requests.Abstract.Execution;

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
    }
}
