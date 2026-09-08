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
/// through on Cloud Run.
/// </para>
/// <para>
/// <b>The shim binds the event as a string</b>: the JSON of one CloudEvent, which is what the host
/// sends a function subscribed in the CloudEvents schema. The extension can bind it to the Event
/// Grid SDK's own type; this reads the same JSON with the one parser the lines share, and the body
/// the handler binds is the event's data as the producer wrote it.
/// </para>
/// </remarks>
public sealed class EventGridAdapter : ITriggerAdapter {
    /// <summary>The event's id, as a header.</summary>
    public const string IdHeader = "ce-id";

    /// <summary>The event's source.</summary>
    public const string SourceHeader = "ce-source";

    /// <summary>The event's type.</summary>
    public const string TypeHeader = "ce-type";

    /// <summary>The event's subject, when the producer set one.</summary>
    public const string SubjectHeader = "ce-subject";

    /// <summary>When the producer said the event happened, as written.</summary>
    public const string TimeHeader = "ce-time";

    /// <summary>
    /// Whether the shim was generated for this family. A string on its own says nothing - three
    /// families bind one - so the scheme the shim carries is part of the check.
    /// </summary>
    public bool Handles(FunctionsTrigger trigger) =>
        trigger.Scheme == CloudEventRoutes.EventScheme && trigger.Data is string;

    public IExecutionRequest CreateRequest(FunctionsTrigger trigger, FunctionContext context) {
        var cloudEvent = CloudEventReader.ReadStructured(Encoding.UTF8.GetBytes((string)trigger.Data));

        var headers = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);

        Set(headers, IdHeader, cloudEvent.Id);
        Set(headers, SourceHeader, cloudEvent.Source);
        Set(headers, TypeHeader, cloudEvent.Type);
        Set(headers, SubjectHeader, cloudEvent.Subject);
        Set(headers, TimeHeader, cloudEvent.Time);
        Set(headers, "Content-Type", cloudEvent.DataContentType ?? "application/json");

        return new FunctionsPayloadRequest(
            CloudEventRoutes.EventScheme,
            CloudEventRoutes.Event(cloudEvent),
            cloudEvent.Data.IsEmpty
                ? Stream.Null
                : new MemoryStream(cloudEvent.Data.ToArray(), writable: false),
            headers);
    }

    private static void Set(IDictionary<string, StringValues> headers, string name, string? value) {
        if (!string.IsNullOrEmpty(value)) {
            headers[name] = value;
        }
    }

    public IExecutionResponse CreateResponse(Stream output) => new FunctionsPayloadResponse(output);

    /// <summary>Rethrown. Failing the invocation is what makes Event Grid retry the delivery.</summary>
    public HostFailurePolicy FailurePolicy => HostFailurePolicy.Rethrow;

    /// <summary>Nothing. Event Grid reads no response from a function.</summary>
    public ValueTask<object?> WriteResponse(IExecutionContext context, FunctionContext functionContext) =>
        new((object?)null);
}
