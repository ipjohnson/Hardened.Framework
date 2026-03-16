namespace Hardened.Requests.Abstract.Execution;

public interface IFunctionHandlerProvider {
    IExecutionRequestHandler? GetFunctionHandler(string functionName, IServiceProvider serviceProvider);
}
