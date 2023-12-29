using Hardened.Shared.Runtime.Application;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Shared.Testing.Impl;

public class TestApplication : IApplicationRoot {
    private readonly ServiceProvider _rootServiceProvider;

    public TestApplication(IApplicationModule testModule, string logNs, IEnvironment environment,
        Action<IEnvironment, IServiceCollection>? overrideDependencies) {
        _rootServiceProvider = CreateServiceProvider(testModule, environment, overrideDependencies);
        ApplicationLogic.StartWithWait(Provider, null, 15);
    }

    private ServiceProvider CreateServiceProvider(IApplicationModule applicationModule, IEnvironment environment,
        Action<IEnvironment, IServiceCollection>? overrideDependencies) {
        var serviceCollection = new ServiceCollection();

        serviceCollection.AddLogging();
        serviceCollection.AddSingleton(environment);

        applicationModule.ConfigureModule(environment, serviceCollection);

        overrideDependencies?.Invoke(environment, serviceCollection);

        return serviceCollection.BuildServiceProvider();
    }

    public IServiceProvider Provider => _rootServiceProvider;

    public async ValueTask DisposeAsync() {
        await _rootServiceProvider.DisposeAsync();
    }
}