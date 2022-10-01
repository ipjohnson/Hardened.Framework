using Hardened.Requests.Abstract.Execution;

namespace Hardened.Requests.Abstract.Serializer;

public interface ISerializationLocatorService
{
    IRequestDeserializer FindRequestDeserializer(IExecutionContext context);

    IResponseSerializer FindResponseSerializer(IExecutionContext context);
}