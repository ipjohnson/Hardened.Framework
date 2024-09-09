using Hardened.Shared.Runtime.Application;

namespace Hardened.Shared.Runtime.Configuration;

public interface IConfigurationPackage {
    IEnumerable<IConfigurationValueProvider> ConfigurationValueProviders(IHardenedEnvironment env);

    IEnumerable<IConfigurationValueAmender> ConfigurationValueAmenders(IHardenedEnvironment env);
}