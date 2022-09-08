using Hardened.Requests.Abstract.Execution;

namespace Hardened.Requests.Abstract.Serializer
{
    public interface IRequestDeserializer
    {
        bool IsDefaultSerializer { get; }

        bool CanProcessContext(IExecutionContext context);

        ValueTask<T?> DeserializeRequestBody<T>(IExecutionContext context);
    }
}
