using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Shared.Runtime.Application;

public interface IApplicationModule {
    void ConfigureModule(IHardenedEnvironment environment, IServiceCollection serviceCollection);
}