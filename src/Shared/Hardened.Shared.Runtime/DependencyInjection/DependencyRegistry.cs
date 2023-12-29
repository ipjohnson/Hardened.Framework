using Hardened.Shared.Runtime.Application;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Shared.Runtime.DependencyInjection;

public class DependencyRegistry<T> {
    private static readonly List<DependencyRegistrationFunc> _registrations = new();

    public static int Register(DependencyRegistrationFunc func) {
        _registrations.Add(func);

        return 1;
    }

    public static void ApplyRegistration(IEnvironment environment, IServiceCollection serviceCollection, T entryPoint) {
        foreach (var registrationFunc in _registrations) {
            registrationFunc(environment, serviceCollection, entryPoint);
        }
    }

    public delegate void DependencyRegistrationFunc(IEnvironment environment, IServiceCollection serviceCollection,
        T entryPoint);
}