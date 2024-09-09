using Hardened.Shared.Runtime.Application;

namespace Hardened.Shared.Runtime.Configuration;

public interface IConfigurationValueProvider {
    Type InterfaceType { get; }

    Type ImplementationType { get; }

    object ProvideValue(IHardenedEnvironment environment, Action<IHardenedEnvironment, object> amender);
}