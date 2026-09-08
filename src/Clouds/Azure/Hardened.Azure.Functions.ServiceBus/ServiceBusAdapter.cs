using Azure.Messaging.ServiceBus;
using Hardened.Azure.Functions.Runtime.Adapters;
using Hardened.Azure.Functions.Runtime.Execution;
using Hardened.Requests.Abstract.Execution;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Primitives;

namespace Hardened.Azure.Functions.ServiceBus;

/// <summary>
/// Service Bus, delivered as a batch against one queue.
/// </summary>
/// <remarks>
/// <para>
/// Payload-shaped: the handler sees a message and a route, no path template and no connection. A
/// throw is rethrown rather than answered, because there is no caller waiting on a connection -
/// failing the invocation is what makes the extension abandon the batch so the queue redelivers.
/// </para>
/// <para>
/// <b>The shim's parameter is <c>ServiceBusReceivedMessage[]</c>, and this is why.</b> The worker
/// can hand a batched Service Bus function <c>string[]</c>, <c>byte[][]</c> or the SDK's own
/// message type. The first two are bodies alone; every fact a handler could want beyond the body -
/// the message id, the delivery count, the application properties - is then only in the trigger
/// metadata, as parallel arrays serialized to JSON strings the adapter would have to parse and
/// index back together. <c>ServiceBusReceivedMessage</c> carries all of it on the message the
/// extension's converter already decoded from the host's AMQP bytes, and it costs no dependency
/// this package does not already have: the worker extension that owns the attribute depends on
/// Azure.Messaging.ServiceBus. It is also the type the Phase 2 settlement actions take.
/// </para>
/// <para>
/// Routes as <c>QUEUE /orders</c>, with the queue's name from the shim rather than from the
/// message. On Lambda the event says which queue delivered; on Azure the function <em>is</em> the
/// binding to one queue, so the shim generated for <c>[Queue("orders")]</c> is invoked for nothing
/// else and its route is the honest one.
/// </para>
/// </remarks>
public sealed class ServiceBusAdapter : ITriggerAdapter {
    /// <summary>Whether the shim was generated for this family, which is a type check.</summary>
    public bool Handles(FunctionsTrigger trigger) => trigger.Data is ServiceBusReceivedMessage[];

    public IExecutionRequest CreateRequest(FunctionsTrigger trigger, FunctionContext context) {
        var messages = trigger.Data as ServiceBusReceivedMessage[]
                       ?? throw new InvalidOperationException(
                           $"The Service Bus adapter was handed {trigger.Data.GetType().Name} for " +
                           $"{trigger.Scheme} {trigger.Path}. Its shims bind ServiceBusReceivedMessage[], " +
                           "so this shim was generated for another family.");

        return new ServiceBusRequest(
            trigger.Scheme,
            trigger.Path,
            // Empty rather than the batch: the batch has no one body, and every fork built by
            // ServiceBusRequest.ForItem carries its own.
            Stream.Null,
            new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase),
            messages);
    }

    public IExecutionResponse CreateResponse(Stream output) => new FunctionsPayloadResponse(output);

    /// <summary>
    /// Rethrown. Failing the invocation is what abandons the batch - answering would tell the host
    /// every message was handled and the extension would complete them.
    /// </summary>
    public HostFailurePolicy FailurePolicy => HostFailurePolicy.Rethrow;

    /// <summary>
    /// Nothing, for now. With <c>ReportsItemFailures</c> off a failed message has already failed
    /// the invocation before this is reached; settling the batch message by message through
    /// <c>ServiceBusMessageActions</c> is Phase 2's work and lands here.
    /// </summary>
    public ValueTask WriteResponse(IExecutionContext context, FunctionContext functionContext) =>
        ValueTask.CompletedTask;
}
