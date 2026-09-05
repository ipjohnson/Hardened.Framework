namespace Hardened.Requests.Abstract.Responses;

/// <summary>
/// A conditional request that arrived without its condition - 428.
/// </summary>
/// <remarks>
/// <para>
/// The other half of the conditional-request pair. <see cref="PreconditionFailed"/> answers a
/// validator that is stale; this answers one that was never sent, which is the case that actually
/// loses an update: two clients read, both write unconditionally, and the second silently overwrites
/// the first. RFC 6585 defines 428 for exactly this, and its stated purpose is to let a server
/// require the conditional request rather than hope for one.
/// </para>
/// <para>
/// Shipping only the 412 half made every optimistic-concurrency route in the study answer one of its
/// two cases with an invented status.
/// </para>
/// </remarks>
[HttpStatus(428)]
public sealed record PreconditionRequired(string? Detail = null)
    : IHttpStatusResponse, IDeclaresStatus {

    public string Type => ProblemTypes.PreconditionRequired;

    public string Title => "Precondition Required";

    public static int StatusCode => 428;

    public int Status => StatusCode;
}
