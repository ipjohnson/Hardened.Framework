using Hardened.Requests.Abstract.Execution;

namespace Hardened.Requests.Abstract.Serializer;

public interface IExceptionResponseSerializer {
    Task Handle(IExecutionContext context, Exception exp);
}