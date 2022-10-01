namespace Hardened.Requests.Abstract.Execution;

public interface IExecutionRequestHandlerInfo
{
    string Path { get; }

    string Method { get; }

    Type HandlerType { get; }

    string InvokeMethod { get; }

    int? SuccessStatus => null;

    int? FailureStatus => null;

    int? NullResponseStatus => null;

    IReadOnlyList<IExecutionRequestParameter> Parameters { get; }
}