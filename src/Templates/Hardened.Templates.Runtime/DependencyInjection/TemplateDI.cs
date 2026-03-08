using Hardened.Shared.Runtime.Application;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Templates.Runtime.DependencyInjection;

public static class TemplateDI {
    private static readonly WeakReference<IServiceCollection> _lastServiceCollection = new(default!);

    public static void Register(IHardenedEnvironment environment, IServiceCollection serviceCollection) {
        if (!_lastServiceCollection.TryGetTarget(out var lastServiceCollection) ||
            !ReferenceEquals(lastServiceCollection, serviceCollection)) {
            _lastServiceCollection.SetTarget(serviceCollection);
            new HardenedTemplateModule().ConfigureServices(serviceCollection);
        }
    }
}