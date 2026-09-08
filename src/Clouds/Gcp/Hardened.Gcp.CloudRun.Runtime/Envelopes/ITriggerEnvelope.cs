using Hardened.Gcp.CloudRun.Runtime.Execution;
using Hardened.Requests.Abstract.Execution;

namespace Hardened.Gcp.CloudRun.Runtime.Envelopes;

/// <summary>
/// Turns the HTTP request an event source delivers into the trigger-shaped request a handler
/// routes on.
/// </summary>
/// <remarks>
/// <para>
/// The seam the Cloud Run line rests on, and the counterpart of the Lambda line's payload
/// adapter: a host differs from every other host in how its delivery becomes a request, and on
/// Cloud Run every delivery is an HTTP request. A Pub/Sub push is a JSON body with a
/// <c>message</c> and a <c>subscription</c>; an Eventarc event is a body under <c>ce-</c> headers;
/// a Cloud Scheduler run is a POST to a path the job was given. Each is one implementation,
/// registered by its adapter package, and <see cref="TriggerFrontDoor"/> asks them in turn.
/// </para>
/// <para>
/// <b>Recognising and unwrapping are two reads, and the first must be cheap.</b> A web route on
/// the same service pays <see cref="Recognises"/> on every request, so it looks at the method and
/// the headers and nothing else. Only a request some envelope recognised has its body buffered,
/// and only then is <see cref="Unwrap"/> asked - which may still decline, because a JSON POST is
/// not a push until its body says so.
/// </para>
/// </remarks>
public interface ITriggerEnvelope {
    /// <summary>
    /// Whether this envelope might be what <paramref name="request"/> is, from its method and
    /// headers alone. False costs nothing further.
    /// </summary>
    bool Recognises(IExecutionRequest request);

    /// <summary>
    /// The trigger request for <paramref name="request"/>, or null when the body says it is not
    /// this envelope after all.
    /// </summary>
    /// <remarks>
    /// The payload is the whole body, buffered, with the parsed document available for a JSON
    /// envelope. The request returned owns its own bytes: the payload is disposed as soon as this
    /// returns.
    /// </remarks>
    CloudRunTriggerRequest? Unwrap(IExecutionRequest request, TriggerPayload payload);
}
