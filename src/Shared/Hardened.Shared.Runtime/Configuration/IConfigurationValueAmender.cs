using Hardened.Shared.Runtime.Application;

namespace Hardened.Shared.Runtime.Configuration;

public interface IConfigurationValueAmender {
    object ApplyConfiguration(IHardenedEnvironment environment, object configurationValue);
}