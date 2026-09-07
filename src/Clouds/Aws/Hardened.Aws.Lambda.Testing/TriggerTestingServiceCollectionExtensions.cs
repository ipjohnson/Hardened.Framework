using Hardened.Functions.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Aws.Lambda.Testing;

/// <summary>
/// Makes an application's generated trigger façades resolvable in a test.
/// </summary>
/// <remarks>
/// Applied through <c>overrideDependencies</c> when the test builds the application, so the
/// registration never reaches a published function - which is what keeps the façades trimmable.
/// </remarks>
public static class TriggerTestingServiceCollectionExtensions {
    public static IServiceCollection AddLambdaTriggerTesting(this IServiceCollection services) {
        services.AddSingleton<LambdaTriggerInvoker>();

        // Open generics, so the testing package registers three things and never names a generated
        // type. The façade is constructed on resolve, from the type argument the test asked for.
        services.AddSingleton(typeof(IQueuesOf<>), typeof(QueuesOf<>));
        services.AddSingleton(typeof(ITopicsOf<>), typeof(TopicsOf<>));
        services.AddSingleton(typeof(ITimersOf<>), typeof(TimersOf<>));

        return services;
    }

    private sealed class QueuesOf<TQueues> : IQueuesOf<TQueues> {
        public QueuesOf(LambdaTriggerInvoker invoker) => SendTo = invoker.Facade<TQueues>();

        public TQueues SendTo { get; }
    }

    private sealed class TopicsOf<TTopics> : ITopicsOf<TTopics> {
        public TopicsOf(LambdaTriggerInvoker invoker) => PublishTo = invoker.Facade<TTopics>();

        public TTopics PublishTo { get; }
    }

    private sealed class TimersOf<TTimers> : ITimersOf<TTimers> {
        public TimersOf(LambdaTriggerInvoker invoker) => Fire = invoker.Facade<TTimers>();

        public TTimers Fire { get; }
    }
}
