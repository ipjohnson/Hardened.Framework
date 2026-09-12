namespace Hardened.Requests.Abstract.Serializer;

/// <summary>
/// What representation a failed request's body goes out as.
/// </summary>
public enum ErrorBodyFormat {

    /// <summary>
    /// The one the request negotiated, like any other body. The default.
    /// </summary>
    /// <remarks>
    /// An operation declaring <c>application/x-msgpack</c> answers its refusals as MessagePack,
    /// which is what its client asked for and what the published document says. Anything else
    /// would have a response's representation depend on whether it succeeded.
    /// </remarks>
    Negotiated = 0,

    /// <summary>
    /// JSON, whatever the request negotiated and whatever the operation declares.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For a service whose success bodies are a binary format and whose error bodies have to stay
    /// readable - by a log, by a human, or by a client library that cannot read a binary one.
    /// Refit is the case that forces the question: its <c>ApiException</c> carries the response
    /// content as a <c>string</c>, so a binary error body is decoded and re-encoded before any
    /// deserializer is asked for it and arrives as replacement characters. A Refit client of a
    /// MessagePack service can read every success body and no error body, and there is nothing
    /// the service can do about it except send text.
    /// </para>
    /// <para>
    /// All or nothing, and per service rather than per operation, for the reason
    /// <see cref="ContentNegotiationMode"/> is: a policy that has to be repeated is one that ends
    /// up applied unevenly, and one operation quietly answering a different representation than
    /// its neighbours is worse than either answer applied consistently.
    /// </para>
    /// <para>
    /// The published document says so - an error response declares <c>application/json</c> and
    /// nothing else - so a generated client reads what it is actually sent. Asked for with
    /// <c>[JsonErrorBodies]</c> on the entry point, or <c>x-hardened-error-bodies: json</c> at a
    /// description's root.
    /// </para>
    /// </remarks>
    Json = 1
}
