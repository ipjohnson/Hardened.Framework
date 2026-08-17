using DependencyModules.Runtime.Attributes;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Forms;
using Hardened.Requests.Abstract.Serializer;

namespace Hardened.Requests.Runtime.Execution;

[SingletonService(Using = RegistrationType.Try)]
public class KnownServices : IKnownServices {
    public KnownServices(IContextSerializationService contextSerializationService,
        IStringConverterService stringConverterService,
        IFormReader formReader) {
        ContextSerializationService = contextSerializationService;
        StringConverterService = stringConverterService;
        FormReader = formReader;
    }

    public IContextSerializationService ContextSerializationService { get; }

    public IStringConverterService StringConverterService { get; }

    public IFormReader FormReader { get; }
}