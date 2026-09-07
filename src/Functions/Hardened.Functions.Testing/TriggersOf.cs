namespace Hardened.Functions.Testing;

/// <summary>
/// The queues an application declares, as something a test can send to.
/// </summary>
/// <remarks>
/// <para>
/// <typeparamref name="TQueues"/> is the façade generated as a nested type on the entry point, so a
/// test writes <c>IQueuesOf&lt;OrdersApp.Queues&gt;</c> and nothing new has to be named. It cannot
/// be the application type itself: C# will not look up a nested type through a type parameter, so
/// <c>TApplication.Queues</c> does not compile.
/// </para>
/// <para>
/// Reaching the façade through an interface rather than injecting it directly is what keeps it out
/// of the application's own service collection. A registration is exactly what a trimmer cannot
/// remove, so registering the façade app-side would leave test scaffolding in every published
/// function; this way nothing in the application references it and it trims out entirely.
/// </para>
/// <para>
/// The façade itself references none of this. It takes a
/// <c>Func&lt;object, string, string, Task&gt;</c> - the messages, the scheme and the path - because
/// those types are available everywhere, so a project with a queue handler needs no reference to
/// this package to compile what the generator emitted. Only a test project does.
/// </para>
/// <para>
/// The verb differs per trigger because the sources do: you send to a queue, publish to a topic and
/// a timer fires. A test then reads as the thing it simulates.
/// </para>
/// </remarks>
public interface IQueuesOf<out TQueues> {
    /// <summary>One method per queue the application handles.</summary>
    TQueues SendTo { get; }
}

/// <summary>The topics an application subscribes to.</summary>
public interface ITopicsOf<out TTopics> {
    TTopics PublishTo { get; }
}

/// <summary>
/// The schedules an application runs on.
/// </summary>
/// <remarks>
/// A schedule carries no payload, so these methods take no message - which is why the timer façade
/// is not just the queue façade with a different name.
/// </remarks>
public interface ITimersOf<out TTimers> {
    TTimers Fire { get; }
}
