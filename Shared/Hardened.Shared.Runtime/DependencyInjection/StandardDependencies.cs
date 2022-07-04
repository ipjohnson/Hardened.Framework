using Hardened.Shared.Runtime.Collections;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Hardened.Shared.Runtime.DependencyInjection
{
    public static class StandardDependencies
    {
        public static void Register(IServiceCollection serviceCollection)
        {
            serviceCollection.TryAddSingleton<IStringBuilderPool, StringBuilderPool>();
            serviceCollection.TryAddSingleton<IMemoryStreamPool, MemoryStreamPool>();
        }
    }
}
