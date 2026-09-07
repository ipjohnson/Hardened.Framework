using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Hardened.Functions.Testing;

/// <summary>
/// Makes an application's generated trigger façades resolvable in a test.
/// </summary>
/// <remarks>
/// Applied through the test harness rather than by the application, so the registration never
/// reaches a published function - which is what keeps the façades trimmable. Names no cloud, so a
/// test written against them does not change when the host does.
/// </remarks>
public static class TriggerTestingServiceCollectionExtensions {
    public static IServiceCollection AddTriggerTesting(this IServiceCollection services) {
        services.AddSingleton<TriggerInvoker>();

        // TryAdd, so a provider's testing attribute can register the envelope delivery instead and
        // this becomes a no-op whichever order the two attributes run in. A provider replaces it by
        // removing first, the way WebTestingAttribute replaces the not-found handler.
        services.TryAddSingleton<ITriggerDelivery, PipelineDelivery>();

        return services;
    }
}
