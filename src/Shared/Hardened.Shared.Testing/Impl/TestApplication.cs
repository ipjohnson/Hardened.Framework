using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Runtime.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Hardened.Shared.Testing.Impl;

public class TestApplication : IApplicationRoot
{
    private readonly ServiceProvider _rootServiceProvider;

    public TestApplication(IApplicationModule testModule, string logNs, IEnvironment environment, Action<IEnvironment, IServiceCollection>? overrideDependencies)
    {
        var loggerFactory = LoggerFactory.Create(builder => { });
        _rootServiceProvider = CreateServiceProvider(testModule, environment, overrideDependencies, loggerFactory);
        ApplicationLogic.StartWithWait(Provider, null, 15);
    }

    private ServiceProvider CreateServiceProvider(IApplicationModule applicationModule, IEnvironment environment,
        Action<IEnvironment, IServiceCollection>? overrideDependencies, ILoggerFactory loggerFactory)
    {
        var serviceCollection = new ServiceCollection();

        serviceCollection.TryAddTransient(typeof(ILogger<>), typeof(LoggerImpl<>));
        serviceCollection.AddSingleton(_ => loggerFactory);
        serviceCollection.AddSingleton(environment);

        applicationModule.ConfigureModule(environment, serviceCollection);

        overrideDependencies?.Invoke(environment, serviceCollection);

        return serviceCollection.BuildServiceProvider();
    }
        
    public IServiceProvider Provider => _rootServiceProvider;

    public async ValueTask DisposeAsync()
    {
        await _rootServiceProvider.DisposeAsync();
    }
}