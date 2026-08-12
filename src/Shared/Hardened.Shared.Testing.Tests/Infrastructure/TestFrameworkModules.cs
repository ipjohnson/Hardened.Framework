using DependencyModules.Runtime.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Shared.Testing.Tests.Infrastructure;

/// <summary>
/// A greeting, so a test can tell the application's own registration apart from a substitute
/// standing in for it.
/// </summary>
public interface IGreetingService {
    string Greet(string name);
}

public class RealGreetingService : IGreetingService {
    public string Greet(string name) => $"real hello {name}";
}

/// <summary>
/// Registered by nothing. A parameter of this type is what "unresolvable" means to the resolver:
/// no registration to find and no constructor to fall back on.
/// </summary>
public interface INeverRegisteredService {
    int Value { get; }
}

/// <summary>
/// Concrete, unregistered, and constructible from services the container does know — the shape
/// that lets a test name the class under test directly instead of registering it first.
/// </summary>
public class GreetingConsumer {
    public GreetingConsumer(IGreetingService greetingService) {
        GreetingService = greetingService;
    }

    public IGreetingService GreetingService { get; }

    public string GreetWorld() => GreetingService.Greet("world");
}

/// <summary>
/// The module named by <c>[assembly: HardenedTestEntryPoint]</c> in Bootstrap.cs, standing in for
/// an application's generated entry point.
/// </summary>
/// <remarks>
/// Hand written rather than generated. <c>DependencyRegistry.LoadModules</c> reaches a module's
/// registrations through <see cref="IServiceCollectionConfiguration"/> and
/// <c>InternalApplyServices</c>, never through <see cref="IDependencyModule.PopulateServiceCollection"/>,
/// so implementing only the latter would load a module that registers nothing and every
/// [Mock]-beats-the-application test would pass with nothing to beat.
/// </remarks>
public class AssemblyEntryPointModule : IDependencyModule, IServiceCollectionConfiguration {
    public void PopulateServiceCollection(IServiceCollection serviceCollection) {
        ConfigureServices(serviceCollection);
    }

    public void ConfigureServices(IServiceCollection services) {
        services.AddSingleton<IGreetingService, RealGreetingService>();
    }
}

/// <summary>
/// Stands in for an entry point declared on a test class, so a lookup can be seen to prefer it
/// over the assembly's.
/// </summary>
public class ClassEntryPointModule : IDependencyModule {
    public void PopulateServiceCollection(IServiceCollection serviceCollection) { }
}

/// <summary>
/// Stands in for an entry point declared on a single test method.
/// </summary>
public class MethodEntryPointModule : IDependencyModule {
    public void PopulateServiceCollection(IServiceCollection serviceCollection) { }
}
