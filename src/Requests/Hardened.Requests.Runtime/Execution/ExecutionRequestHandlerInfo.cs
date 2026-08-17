using Hardened.Requests.Abstract.Authorization;
using Hardened.Requests.Abstract.Execution;

namespace Hardened.Requests.Runtime.Execution;

public class ExecutionRequestHandlerInfo : IExecutionRequestHandlerInfo {
    public ExecutionRequestHandlerInfo(
        string path,
        string method,
        Type handlerType,
        string invokeMethod,
        IReadOnlyList<IExecutionRequestParameter>? parameters = null,
        IReadOnlyList<object>? metadata = null,
        Requirement? requirement = null) {
        Path = path;
        Method = method;
        HandlerType = handlerType;
        InvokeMethod = invokeMethod;
        Parameters = parameters ?? new List<IExecutionRequestParameter>();
        Metadata = metadata ?? Array.Empty<object>();
        Requirement = requirement ?? IExecutionRequestHandlerInfo.RequirementFrom(Metadata);
    }

    public string Path { get; }

    public string Method { get; }

    public Type HandlerType { get; }

    public string InvokeMethod { get; }

    public IReadOnlyList<IExecutionRequestParameter> Parameters { get; }

    public IReadOnlyList<object> Metadata { get; }

    /// <inheritdoc />
    /// <remarks>
    /// Computed once here rather than per read. The generated handler holds this in a static field,
    /// so the walk over metadata happens once per handler type for the life of the process.
    /// </remarks>
    public Requirement? Requirement { get; }
}
