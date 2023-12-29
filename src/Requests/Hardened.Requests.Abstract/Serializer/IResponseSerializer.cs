using Hardened.Requests.Abstract.Execution;

namespace Hardened.Requests.Abstract.Serializer;

public interface IResponseSerializer {
    bool IsDefaultSerializer { get; }

    bool CanProcessContext(IExecutionContext context);

    Task SerializeResponse(IExecutionContext context);
}