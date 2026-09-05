using DependencyModules.Runtime.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Shared.Testing.NUnit.Tests;

public interface IGreetingService {
    string Greet(string name);
}

public class RealGreetingService : IGreetingService {
    public string Greet(string name) => $"real hello {name}";
}

/// <summary>
/// Hand written rather than generated, for the reason the xUnit test project gives on its own
/// module: the registry reaches a module's registrations through
/// <see cref="IServiceCollectionConfiguration"/>, so a module implementing only
/// <see cref="IDependencyModule.PopulateServiceCollection"/> would load and register nothing.
/// </summary>
public class GreetingModule : IDependencyModule, IServiceCollectionConfiguration {
    public void PopulateServiceCollection(IServiceCollection serviceCollection) => ConfigureServices(serviceCollection);

    public void ConfigureServices(IServiceCollection services) {
        services.AddSingleton<IGreetingService, RealGreetingService>();
    }
}
