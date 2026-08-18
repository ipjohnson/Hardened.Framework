using Hardened.Requests.Abstract.Forms;
using Hardened.Requests.Abstract.Serializer;

namespace Hardened.Requests.Abstract.Execution;

public interface IKnownServices {
    IContextSerializationService ContextSerializationService { get; }

    IStringConverterService StringConverterService { get; }

    /// <summary>
    /// Reads <c>application/x-www-form-urlencoded</c> bodies.
    /// </summary>
    /// <remarks>
    /// Here rather than on <c>IExecutionRequest</c> deliberately - see <c>FormReader</c>. This
    /// interface has one implementation, so a host gets form binding without implementing anything.
    /// </remarks>
    IFormReader FormReader { get; }
}