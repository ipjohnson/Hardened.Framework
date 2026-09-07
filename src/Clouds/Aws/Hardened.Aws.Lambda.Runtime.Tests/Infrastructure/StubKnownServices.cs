using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Forms;
using Hardened.Requests.Abstract.Serializer;

namespace Hardened.Aws.Lambda.Runtime.Tests.Infrastructure;

/// <summary>
/// The known services, present but unusable.
/// </summary>
/// <remarks>
/// The invocation loop resolves this and puts it on the context without reading it - binding and
/// serialization are filters, and none of them run in these tests. Every member throws so a change
/// that starts reading one says so here rather than working against a fabricated serializer.
/// </remarks>
public sealed class StubKnownServices : IKnownServices {
    public IContextSerializationService ContextSerializationService =>
        throw new NotSupportedException();

    public IStringConverterService StringConverterService => throw new NotSupportedException();

    public IFormReader FormReader => throw new NotSupportedException();
}
