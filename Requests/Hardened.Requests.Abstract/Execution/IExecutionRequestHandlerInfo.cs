namespace Hardened.Requests.Abstract.Execution
{
    public interface IExecutionRequestHandlerInfo
    {
        string Path { get; }

        string Method { get; }

        Type HandlerType { get; }

        string InvokeMethod { get; }
    }
}
