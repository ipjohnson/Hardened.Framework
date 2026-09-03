namespace Hardened.Requests.Abstract.Responses;

/// <summary>
/// Nothing this service can produce matches what the caller will accept - 406.
/// </summary>
/// <remarks>
/// <para>
/// The response half of content negotiation, and the one the runtime already emits: an
/// <c>Accept</c> naming only media types no serializer registered for this endpoint can write.
/// <see cref="UnsupportedMediaType"/> is the request half, and the two are not interchangeable -
/// one is about what the caller sent and the other about what they will take.
/// </para>
/// <para>
/// <b>Bodyless, and there is no generic form beside it.</b> That is
/// <c>ContextSerializationService</c>'s own reasoning rather than a shape chosen here: anything
/// written would be the representation the client has just said it cannot read. A payload wrapper
/// for this status would be a body with no media type left to send it in.
/// </para>
/// </remarks>
[HttpStatus(406)]
public sealed record NotAcceptable : IHttpStatusResponse {

    public int Status => 406;

    public bool HasBody => false;
}
