using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Shared.Runtime.Application;

public interface IApplicationModule {
    void ConfigureModule(IEnvironment environment, IServiceCollection serviceCollection);
}