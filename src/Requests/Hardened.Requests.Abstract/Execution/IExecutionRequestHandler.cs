namespace Hardened.Requests.Abstract.Execution
{
    public interface IExecutionRequestHandler
    {
        IExecutionRequestHandlerInfo HandlerInfo { get; }

        IExecutionChain GetExecutionChain(IExecutionContext context);
    }
}
