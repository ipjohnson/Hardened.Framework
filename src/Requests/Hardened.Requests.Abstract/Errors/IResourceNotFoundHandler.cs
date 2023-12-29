using Hardened.Requests.Abstract.Execution;

namespace Hardened.Requests.Abstract.Errors;

public interface IResourceNotFoundHandler {
    Task Handle(IExecutionChain chain);
}