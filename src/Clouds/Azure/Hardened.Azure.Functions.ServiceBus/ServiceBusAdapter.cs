using Azure.Messaging.ServiceBus;
using Hardened.Azure.Functions.Runtime.Adapters;
using Hardened.Azure.Functions.Runtime.Execution;
using Hardened.Requests.Abstract.Execution;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Primitives;

namespace Hardened.Azure.Functions.ServiceBus;

/// <summary>
/// Service Bus, delivered as a batch against one queue or one subscription.
/// </summary>
/// <remarks>
/// <para>
/// Payload-shaped: the handler sees a message and a route, no path template and no connection. A
/// throw is rethrown rather than answered, because there is no caller waiting on a connection -
/// failing the invocation is what makes the extension abandon the batch so the queue redelivers.
/// </para>
/// <para>
/// <b>The shim binds <c>ServiceBusReceivedMessage[]</c>, and this is why.</b> The worker can hand
/// a batched Service Bus function <c>string[]</c>, <c>byte[][]</c> or the SDK's own message type.
/// The first two are bodies alone; every fact a handler could want beyond the body - the message
/// id, the delivery count, the application properties - is then only in the trigger metadata, as
/// parallel arrays serialized to JSON strings the adapter would have to parse and index back
/// together. <c>ServiceBusReceivedMessage</c> carries all of it on the message the extension's
/// converter already decoded from the host's AMQP bytes, and it costs no dependency this package
/// does not already have: the worker extension that owns the attribute depends on
/// Azure.Messaging.ServiceBus. It is also what the settlement actions take.
/// </para>
/// <para>
/// Routes as <c>QUEUE /orders</c> or <c>TOPIC /order-events</c>, with the name from the shim
/// rather than from the message. On Lambda the event says which queue delivered; on Azure the
/// function <em>is</em> the binding to one entity, so the shim generated for
/// <c>[Queue("orders")]</c> is invoked for nothing else and its route is the honest one.
/// </para>
/// </remarks>
public sealed class ServiceBusAdapter : ITriggerAdapter {
    private readonly bool _reportsItemFailures;

    /// <param name="reportsItemFailures">
    /// Whether the module settles messages one by one. Off by default; see
    /// <see cref="ServiceBusModule.ReportsItemFailures"/>.
    /// </param>
    public ServiceBusAdapter(bool reportsItemFailures = false) {
        _reportsItemFailures = reportsItemFailures;
    }

    /// <summary>Whether the shim was generated for this family, which is a type check.</summary>
    public bool Handles(FunctionsTrigger trigger) =>
        trigger.Data is ServiceBusDelivery || trigger.Data is ServiceBusReceivedMessage[];

    public IExecutionRequest CreateRequest(FunctionsTrigger trigger, FunctionContext context) {
        var delivery = trigger.Data switch {
            ServiceBusDelivery bound => bound,
            ServiceBusReceivedMessage[] messages => new ServiceBusDelivery(messages),
            _ => throw new InvalidOperationException(
                $"The Service Bus adapter was handed {trigger.Data.GetType().Name} for " +
                $"{trigger.Scheme} {trigger.Path}. Its shims bind ServiceBusReceivedMessage[], " +
                "so this shim was generated for another family.")
        };

        return new ServiceBusRequest(
            trigger.Scheme,
            trigger.Path,
            // Empty rather than the batch: the batch has no one body, and every fork built by
            // ServiceBusRequest.ForItem carries its own.
            Stream.Null,
            new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase),
            delivery.Messages,
            _reportsItemFailures,
            delivery.Actions);
    }

    public IExecutionResponse CreateResponse(Stream output) => new FunctionsPayloadResponse(output);

    /// <summary>
    /// Rethrown. Failing the invocation is what abandons the batch - answering would tell the host
    /// every message was handled and the extension would complete them.
    /// </summary>
    public HostFailurePolicy FailurePolicy => HostFailurePolicy.Rethrow;

    /// <summary>
    /// Settles the batch message by message, where the module asked for it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Reached only on a successful chain. With <c>ReportsItemFailures</c> off a failed message has
    /// already failed the invocation, the host settles the batch, and there is nothing to do. On,
    /// the generated function turned the host's auto-completion off, so every message is settled
    /// here: completed where its fork succeeded, abandoned where the filter recorded a failure, so
    /// the queue delivers the refused message alone.
    /// </para>
    /// <para>
    /// Nothing settles a batch whose chain never finished, because this is not reached; those
    /// locks expire and the messages come back together, which is what the host would have done.
    /// </para>
    /// </remarks>
    public async ValueTask<object?> WriteResponse(IExecutionContext context, FunctionContext functionContext) {
        if (!_reportsItemFailures || context.Request is not ServiceBusRequest batch || batch.Actions == null) {
            return null;
        }

        var failed = new HashSet<int>(batch.FailedItems);

        for (var index = 0; index < batch.Count; index++) {
            var message = batch.Messages[index];

            if (failed.Contains(index)) {
                await batch.Actions.AbandonMessageAsync(message, cancellationToken: functionContext.CancellationToken);
            }
            else {
                await batch.Actions.CompleteMessageAsync(message, functionContext.CancellationToken);
            }
        }

        return null;
    }
}
