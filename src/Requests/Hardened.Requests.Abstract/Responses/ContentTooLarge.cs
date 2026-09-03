namespace Hardened.Requests.Abstract.Responses;

/// <summary>
/// The body the caller sent is larger than this service will read - 413.
/// </summary>
/// <remarks>
/// <para>
/// RFC 9110's current name for the status HTTP/1.1 called <c>Payload Too Large</c>. The registered
/// name is the one a client library and a gateway both use, and it is what the generated
/// specification-first types are named after, so the two halves agree without a translation table.
/// </para>
/// <para>
/// <see cref="Detail"/> is where the limit goes. A 413 that does not say how large is too large
/// leaves the caller to find the boundary by bisection, which is a sequence of requests the service
/// then has to refuse.
/// </para>
/// </remarks>
[HttpStatus(413)]
public sealed record ContentTooLarge(string? Detail = null) : IHttpStatusResponse {

    public string Type => ProblemTypes.ContentTooLarge;

    public string Title => "Content Too Large";

    public int Status => 413;
}
