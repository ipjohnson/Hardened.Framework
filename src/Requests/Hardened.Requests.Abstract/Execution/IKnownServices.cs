using Hardened.Requests.Abstract.Serializer;

namespace Hardened.Requests.Abstract.Execution
{
    public interface IKnownServices
    {
        IContextSerializationService ContextSerializationService { get; }

        IStringConverterService StringConverterService { get; }
    }
}
