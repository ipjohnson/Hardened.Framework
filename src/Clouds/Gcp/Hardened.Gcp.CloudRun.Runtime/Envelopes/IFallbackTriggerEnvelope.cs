namespace Hardened.Gcp.CloudRun.Runtime.Envelopes;

/// <summary>
/// An envelope asked only once every ordinary one has declined.
/// </summary>
/// <remarks>
/// <para>
/// Two envelopes answer whatever nothing else claimed. A plain Pub/Sub push is any JSON POST with
/// a <c>message</c> and a <c>subscription</c>, and a Cloud Storage notification delivered through
/// Pub/Sub is exactly that with two attributes more; a plain Eventarc event is any CloudEvent, and
/// a Firestore or Storage event is exactly that with a particular type. Asked first, the generic
/// envelope would claim the specific one's delivery and route it to the wrong handler.
/// </para>
/// <para>
/// Whether an envelope is generic is a property of the envelope, so it says so in the type system
/// rather than in the order modules happen to be applied - which a package cannot control across
/// package boundaries. The same arrangement <c>IFallbackRequestHandlerProvider</c> gives the
/// routing table for static content.
/// </para>
/// </remarks>
public interface IFallbackTriggerEnvelope : ITriggerEnvelope;
