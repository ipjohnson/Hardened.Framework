using Hardened.Requests.Abstract.Execution;

namespace Hardened.Requests.Runtime.Execution;

public class ExecutionRequestHandlerInfo : IExecutionRequestHandlerInfo {
    public ExecutionRequestHandlerInfo(
        string path,
        string method,
        Type handlerType,
        string invokeMethod,
        IReadOnlyList<IExecutionRequestParameter>? parameters = null,
        IReadOnlyList<object>? metadata = null) {
        Path = path;
        Method = method;
        HandlerType = handlerType;
        InvokeMethod = invokeMethod;
        Parameters = parameters ?? new List<IExecutionRequestParameter>();
        Metadata = metadata ?? Array.Empty<object>();
    }

    public string Path { get; }

    public string Method { get; }

    public Type HandlerType { get; }

    public string InvokeMethod { get; }

    public IReadOnlyList<IExecutionRequestParameter> Parameters { get; }

    public IReadOnlyList<object> Metadata { get; }
}