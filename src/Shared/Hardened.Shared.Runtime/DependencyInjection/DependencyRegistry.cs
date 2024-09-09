using Hardened.Shared.Runtime.Application;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Shared.Runtime.DependencyInjection;

/// <summary>
/// Static class intended to hold static registrations for a given type
/// </summary>
/// <typeparam name="T"></typeparam>
public class DependencyRegistry<T> where T : class {
    private record RegistrationRef(DependencyRegistrationFunc Function) {
        public WeakReference<IServiceCollection?> LastServiceCollection { get; } = new(null);
    };
    
    private static readonly List<RegistrationRef> _registrations = new();

    private static readonly WeakReference<IServiceCollection?> _lastServiceCollection = new (null);

    /// <summary>
    /// Should register module, returns true if service collection wasn't load recently
    /// </summary>
    /// <param name="serviceCollection"></param>
    /// <returns></returns>
    public static bool ShouldRegisterModule(
        IServiceCollection serviceCollection) {
        if (_lastServiceCollection.TryGetTarget(out var collection) &&
            ReferenceEquals(serviceCollection, collection)) {
            return false;
        }
        
        _lastServiceCollection.SetTarget(serviceCollection);

        return true;
    }
    

    /// <summary>
    /// Register dependency registration func
    /// </summary>
    /// <param name="func"></param>
    /// <returns></returns>
    public static int Register(DependencyRegistrationFunc func) {
        _registrations.Add(new RegistrationRef(func));

        return 1;
    }

    /// <summary>
    /// Apply all known registrations to a service collection
    /// </summary>
    /// <param name="environment"></param>
    /// <param name="serviceCollection"></param>
    /// <param name="entryPoint"></param>
    public static void ApplyRegistration(IHardenedEnvironment environment, IServiceCollection serviceCollection, T entryPoint) {
        foreach (var registration in _registrations) {
            if (registration.LastServiceCollection.TryGetTarget(out var collection) && ReferenceEquals(collection, entryPoint)){
                continue;
            }
            
            registration.LastServiceCollection.SetTarget(serviceCollection);
            registration.Function(environment, serviceCollection, entryPoint);
        }
    }

    /// <summary>
    /// Represents registration logic
    /// </summary>
    public delegate void DependencyRegistrationFunc(IHardenedEnvironment environment, IServiceCollection serviceCollection,
        T entryPoint);

    /// <summary>
    /// Clear all known registration, this should only be used for testing purposes.
    /// </summary>
    public static void Clear() {
        _registrations.Clear();
    }
}