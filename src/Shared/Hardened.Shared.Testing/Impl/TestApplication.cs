using DependencyModules.Runtime.Interfaces;
using Hardened.Shared.Runtime.Application;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Shared.Testing.Impl;

public class TestApplication : IApplicationRoot {
    private readonly ServiceProvider _rootServiceProvider;

    public TestApplication(IApplicationModule testModule, string logNs, IHardenedEnvironment environment,
        Action<IHardenedEnvironment, IServiceCollection>? overrideDependencies) {
        _rootServiceProvider = CreateServiceProvider(testModule, environment, overrideDependencies);
        ApplicationLogic.StartWithWait(Provider, null, 15);
    }

    public TestApplication(IDependencyModule testModule, string logNs, IHardenedEnvironment environment,
        Action<IHardenedEnvironment, IServiceCollection>? overrideDependencies) {
        _rootServiceProvider = CreateServiceProvider(testModule, environment, overrideDependencies);
        ApplicationLogic.StartWithWait(Provider, null, 15);
    }

    private ServiceProvider CreateServiceProvider(IApplicationModule applicationModule, IHardenedEnvironment environment,
        Action<IHardenedEnvironment, IServiceCollection>? overrideDependencies) {
        var serviceCollection = new ServiceCollection();

        serviceCollection.AddLogging();

        // Both interfaces, because the module system reads IModuleEnvironment while it decides
        // what to register. AddSingleton would register the parameter's static type alone, and
        // [IfEnvironment] under test would answer against ASPNETCORE_ENVIRONMENT - Production -
        // rather than the environment this application was handed.
        serviceCollection.AddHardenedEnvironment(environment);

        applicationModule.ConfigureModule(environment, serviceCollection);

        overrideDependencies?.Invoke(environment, serviceCollection);

        return serviceCollection.BuildServiceProvider();
    }

    private ServiceProvider CreateServiceProvider(IDependencyModule dependencyModule, IHardenedEnvironment environment,
        Action<IHardenedEnvironment, IServiceCollection>? overrideDependencies) {
        var serviceCollection = new ServiceCollection();

        serviceCollection.AddLogging();

        // Both interfaces, because the module system reads IModuleEnvironment while it decides
        // what to register. AddSingleton would register the parameter's static type alone, and
        // [IfEnvironment] under test would answer against ASPNETCORE_ENVIRONMENT - Production -
        // rather than the environment this application was handed.
        serviceCollection.AddHardenedEnvironment(environment);

        dependencyModule.PopulateServiceCollection(serviceCollection);

        overrideDependencies?.Invoke(environment, serviceCollection);

        return serviceCollection.BuildServiceProvider();
    }

    public IServiceProvider Provider => _rootServiceProvider;

    public async ValueTask DisposeAsync() {
        await _rootServiceProvider.DisposeAsync();
    }
}