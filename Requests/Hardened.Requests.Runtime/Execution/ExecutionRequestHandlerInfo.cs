using Hardened.Requests.Abstract.Execution;

namespace Hardened.Requests.Runtime.Execution
{
    public class ExecutionRequestHandlerInfo : IExecutionRequestHandlerInfo
    {
        public ExecutionRequestHandlerInfo(string path, string method, Type handlerType, string invokeMethod)
        {
            Path = path;
            Method = method;
            HandlerType = handlerType;
            InvokeMethod = invokeMethod;
        }

        public string Path { get; }

        public string Method { get; }

        public Type HandlerType { get; }

        public string InvokeMethod { get; }
    }
}
