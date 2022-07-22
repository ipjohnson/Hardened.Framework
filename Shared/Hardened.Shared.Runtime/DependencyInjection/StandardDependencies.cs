using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Runtime.Collections;
using Hardened.Shared.Runtime.Configuration;
using Hardened.Shared.Runtime.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Hardened.Shared.Runtime.DependencyInjection
{
    public static class StandardDependencies
    {
        public static void Register(IEnvironment environment, IServiceCollection serviceCollection)
        {
            serviceCollection.TryAddSingleton<IStringBuilderPool, StringBuilderPool>();
            serviceCollection.TryAddSingleton<IMemoryStreamPool, MemoryStreamPool>();
            serviceCollection.TryAddSingleton<IConfigurationManager, ConfigurationManager>();
            serviceCollection.TryAddSingleton<IMetricLoggerProvider, NullMetricLoggerProvider>();

            //serviceCollection.AddTransient<IConfigurationValueProvider>(provider =>
            //    provider.GetRequiredService<IConfigurationManager>().GetConfiguration<IPersonServiceConfiguration>());
        }
    }
}
