using Hardened.Requests.Abstract.Execution;

namespace Hardened.Requests.Abstract.Serializer;

public interface INullValueResponseHandler {
    Task Handle(IExecutionContext context);
}