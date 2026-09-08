using Azure.Messaging.EventHubs;
using Hardened.Azure.Functions.Runtime.Adapters;
using Hardened.Azure.Functions.Runtime.Execution;
using Hardened.Requests.Abstract.Execution;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Primitives;

namespace Hardened.Azure.Functions.EventHubs;

/// <summary>
/// Event Hubs, delivered as an ordered batch off one partition.
/// </summary>
/// <remarks>
/// <para>
/// Payload-shaped: the handler sees an event and a route. A throw is rethrown rather than
/// answered, because there is no caller on the other end: a failed invocation is what the host
/// logs and counts, and what a retry policy on the function app retries.
/// </para>
/// <para>
/// <b>A failure does not replay the batch, and this is where the host differs from Kinesis.</b>
/// The Functions host advances the partition's checkpoint when the invocation completes, with or
/// without an exception, so a thrown batch is not delivered again unless the function app
/// declares a retry policy - and then it is delivered again only until that policy is spent.
/// Kinesis on Lambda retries a failed batch until it succeeds or expires. A <c>[Stream]</c>
/// handler that has to see every event therefore needs a retry policy on Azure and a dead-letter
/// path of its own; the documentation for the Event Hubs trigger says the same
/// (functions-reliable-event-processing). This is a documented divergence, not something the
/// adapter can close.
/// </para>
/// <para>
/// <b>The shim binds <c>EventData[]</c></b>, for the reason the Service Bus adapter gives for its
/// message type: the extension's converter has already decoded the host's AMQP bytes into the
/// SDK's own event, which carries the sequence number, the offset, the partition key and the
/// properties beside the body, and the extension depends on the SDK anyway.
/// </para>
/// <para>
/// Routes as <c>STREAM /orders</c>, with the hub's name from the shim: the host binds a function
/// to one hub, so the function's identity is the route.
/// </para>
/// </remarks>
public sealed class EventHubsAdapter : ITriggerAdapter {
    /// <summary>Whether the shim was generated for this family, which is a type check.</summary>
    public bool Handles(FunctionsTrigger trigger) => trigger.Data is EventData[];

    public IExecutionRequest CreateRequest(FunctionsTrigger trigger, FunctionContext context) {
        var events = trigger.Data as EventData[]
                     ?? throw new InvalidOperationException(
                         $"The Event Hubs adapter was handed {trigger.Data.GetType().Name} for " +
                         $"{trigger.Scheme} {trigger.Path}. Its shims bind EventData[], so this shim " +
                         "was generated for another family.");

        return new EventHubsRequest(
            trigger.Scheme,
            trigger.Path,
            Stream.Null,
            new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase),
            events);
    }

    public IExecutionResponse CreateResponse(Stream output) => new FunctionsPayloadResponse(output);

    /// <summary>
    /// Rethrown. A failed invocation is what the host reports and what a retry policy retries;
    /// answering would record a batch nothing handled as a success.
    /// </summary>
    public HostFailurePolicy FailurePolicy => HostFailurePolicy.Rethrow;

    /// <summary>Nothing. The host checkpoints when the invocation completes and reads no response.</summary>
    public ValueTask<object?> WriteResponse(IExecutionContext context, FunctionContext functionContext) =>
        new((object?)null);
}
