using System.Text;
using Hardened.Azure.Functions.Runtime.Adapters;
using Hardened.Azure.Functions.Runtime.Execution;
using Hardened.CloudEvents;
using Hardened.Requests.Abstract.Execution;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Primitives;

namespace Hardened.Azure.Functions.EventGrid;

/// <summary>
/// Event Grid, one CloudEvent per invocation, routed by its source and type.
/// </summary>
/// <remarks>
/// <para>
/// Payload-shaped and never batched. A throw is rethrown, because failing the invocation is what
/// makes Event Grid retry the delivery and, in the end, dead-letter it.
/// </para>
/// <para>
/// <b>One function for every <c>[Event]</c> handler</b>, unlike the entity-bound families: an
/// Event Grid subscription delivers everything that matches it to one function, and which handler
/// runs is a question about the event. So the shim carries no route and the adapter builds one -
/// <c>EVENT /{source}/{type}</c>, the shape the EventBridge adapter routes under - from the
/// CloudEvent, through <c>Hardened.CloudEvents</c>, which is the same reader Eventarc's events go
/// through on Cloud Run. The event's attributes become the headers <see cref="CloudEventHeaders"/>
/// names, so a handler reads the event id the same way on either.
/// </para>
/// <para>
/// <b>The shim binds the event as a string</b>: the JSON of one CloudEvent, which is what the host
/// sends a function subscribed in the CloudEvents schema. The extension can bind it to the Event
/// Grid SDK's own type; this reads the same JSON with the one parser the lines share, and the body
/// the handler binds is the event's data as the producer wrote it.
/// </para>
/// </remarks>
public sealed class EventGridAdapter : ITriggerAdapter {
    /// <summary>
    /// Whether the shim was generated for this family. A string on its own says nothing - three
    /// families bind one - so the scheme the shim carries is part of the check.
    /// </summary>
    public bool Handles(FunctionsTrigger trigger) =>
        trigger.Scheme == CloudEventRoutes.EventScheme && trigger.Data is string;

    public IExecutionRequest CreateRequest(FunctionsTrigger trigger, FunctionContext context) {
        var cloudEvent = CloudEventReader.ReadStructured(Encoding.UTF8.GetBytes((string)trigger.Data));

        var headers = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);

        CloudEventHeaders.Write(headers, cloudEvent);

        // The data's own type, when the producer said; JSON otherwise, which is what a CloudEvent
        // carrying data with no type declared is defined to hold.
        headers["Content-Type"] = string.IsNullOrEmpty(cloudEvent.DataContentType)
            ? "application/json"
            : cloudEvent.DataContentType;

        return new FunctionsPayloadRequest(
            CloudEventRoutes.EventScheme,
            CloudEventRoutes.Event(cloudEvent),
            cloudEvent.Data.IsEmpty
                ? Stream.Null
                : new MemoryStream(cloudEvent.Data.ToArray(), writable: false),
            headers);
    }

    public IExecutionResponse CreateResponse(Stream output) => new FunctionsPayloadResponse(output);

    /// <summary>Rethrown. Failing the invocation is what makes Event Grid retry the delivery.</summary>
    public HostFailurePolicy FailurePolicy => HostFailurePolicy.Rethrow;

    /// <summary>Nothing. Event Grid reads no response from a function.</summary>
    public ValueTask<object?> WriteResponse(IExecutionContext context, FunctionContext functionContext) =>
        new((object?)null);
}
