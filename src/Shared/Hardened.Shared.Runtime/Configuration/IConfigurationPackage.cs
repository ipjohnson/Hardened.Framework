using Hardened.Shared.Runtime.Application;

namespace Hardened.Shared.Runtime.Configuration;

public interface IConfigurationPackage
{
    IEnumerable<IConfigurationValueProvider> ConfigurationValueProviders(IEnvironment env);

    IEnumerable<IConfigurationValueAmender> ConfigurationValueAmenders(IEnvironment env);
}