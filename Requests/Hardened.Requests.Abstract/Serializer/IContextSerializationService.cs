using Hardened.Requests.Abstract.Execution;

namespace Hardened.Requests.Abstract.Serializer
{
    public interface IContextSerializationService
    {
        ValueTask<T?> DeserializeRequestBody<T>(IExecutionContext context);

        Task SerializeResponse(IExecutionContext context);
    }
}
