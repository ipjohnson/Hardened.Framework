using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Hardened.Functions.Testing;

/// <summary>
/// Makes an application's generated trigger façades resolvable in a test.
/// </summary>
/// <remarks>
/// Applied through <c>overrideDependencies</c> when the test builds the application, so the
/// registration never reaches a published function - which is what keeps the façades trimmable.
/// Names no cloud, so a test written against it does not change when the host does.
/// </remarks>
public static class TriggerTestingServiceCollectionExtensions {
    public static IServiceCollection AddTriggerTesting(this IServiceCollection services) {
        services.AddSingleton<TriggerInvoker>();

        // TryAdd, so a provider's testing attribute can register the envelope delivery instead and
        // this becomes a no-op whichever order the two attributes run in. A provider replaces it by
        // removing first, the way WebTestingAttribute replaces the not-found handler.
        services.TryAddSingleton<ITriggerDelivery, PipelineDelivery>();

        // Open generics, so the testing package registers three things and never names a generated
        // type. The façade is constructed on resolve, from the type argument the test asked for.
        services.AddSingleton(typeof(IQueuesOf<>), typeof(QueuesOf<>));
        services.AddSingleton(typeof(ITopicsOf<>), typeof(TopicsOf<>));
        services.AddSingleton(typeof(ITimersOf<>), typeof(TimersOf<>));
        services.AddSingleton(typeof(IInvokeOf<>), typeof(InvokeOf<>));

        return services;
    }

    private sealed class QueuesOf<TQueues> : IQueuesOf<TQueues> {
        public QueuesOf(TriggerInvoker invoker) => SendTo = invoker.Facade<TQueues>();

        public TQueues SendTo { get; }
    }

    private sealed class TopicsOf<TTopics> : ITopicsOf<TTopics> {
        public TopicsOf(TriggerInvoker invoker) => PublishTo = invoker.Facade<TTopics>();

        public TTopics PublishTo { get; }
    }

    private sealed class TimersOf<TTimers> : ITimersOf<TTimers> {
        public TimersOf(TriggerInvoker invoker) => Fire = invoker.Facade<TTimers>();

        public TTimers Fire { get; }
    }

    private sealed class InvokeOf<TInvocations> : IInvokeOf<TInvocations> {
        public InvokeOf(TriggerInvoker invoker) => Call = invoker.Facade<TInvocations>();

        public TInvocations Call { get; }
    }
}
