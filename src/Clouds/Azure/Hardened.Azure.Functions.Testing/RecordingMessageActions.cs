using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;

namespace Hardened.Azure.Functions.Testing;

/// <summary>
/// A <see cref="ServiceBusMessageActions"/> that records what the adapter settled, by message id.
/// </summary>
/// <remarks>
/// <para>
/// The worker binds the real one from its settlement channel to the host, which a test has no
/// access to. The class is subclassable for exactly this purpose - a protected constructor and
/// virtual settlement methods - so this records each call and does nothing else, and a test
/// asserts which messages were completed and which abandoned.
/// </para>
/// <para>
/// Hand it to the delivery inside a <c>ServiceBusDelivery</c>, or register it as
/// <see cref="ServiceBusMessageActions"/> in the container and <c>FunctionsTriggerDelivery</c>
/// picks it up for every Service Bus batch it builds.
/// </para>
/// </remarks>
public sealed class RecordingMessageActions : ServiceBusMessageActions {
    private readonly List<string> _completed = new();
    private readonly List<string> _abandoned = new();
    private readonly List<string> _deadLettered = new();
    private readonly List<string> _deferred = new();

    /// <summary>The ids of the messages completed, in order.</summary>
    public IReadOnlyList<string> Completed => _completed;

    /// <summary>The ids of the messages abandoned, in order.</summary>
    public IReadOnlyList<string> Abandoned => _abandoned;

    public IReadOnlyList<string> DeadLettered => _deadLettered;

    public IReadOnlyList<string> Deferred => _deferred;

    public override Task CompleteMessageAsync(
        ServiceBusReceivedMessage message, CancellationToken cancellationToken = default) {
        _completed.Add(message.MessageId);

        return Task.CompletedTask;
    }

    public override Task AbandonMessageAsync(
        ServiceBusReceivedMessage message,
        IDictionary<string, object>? propertiesToModify = null,
        CancellationToken cancellationToken = default) {
        _abandoned.Add(message.MessageId);

        return Task.CompletedTask;
    }

    public override Task DeadLetterMessageAsync(
        ServiceBusReceivedMessage message,
        Dictionary<string, object>? propertiesToModify = null,
        string? deadLetterReason = null,
        string? deadLetterErrorDescription = null,
        CancellationToken cancellationToken = default) {
        _deadLettered.Add(message.MessageId);

        return Task.CompletedTask;
    }

    public override Task DeferMessageAsync(
        ServiceBusReceivedMessage message,
        IDictionary<string, object>? propertiesToModify = null,
        CancellationToken cancellationToken = default) {
        _deferred.Add(message.MessageId);

        return Task.CompletedTask;
    }
}
