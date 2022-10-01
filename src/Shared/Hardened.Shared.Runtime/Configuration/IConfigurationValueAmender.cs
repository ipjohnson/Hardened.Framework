using Hardened.Shared.Runtime.Application;

namespace Hardened.Shared.Runtime.Configuration;

public interface IConfigurationValueAmender
{
    object ApplyConfiguration(IEnvironment environment, object configurationValue);
}