namespace Hardened.Requests.Abstract.Responses;

/// <summary>
/// A 428 carrying a body the caller supplies.
/// </summary>
/// <remarks>
/// The generic form of <see cref="PreconditionRequired"/>, for a caller who already has a payload
/// type and does not want a Hardened-shaped problem document. Both are the same problem kind and
/// carry the same <c>type</c> URI. The body is <see cref="Body"/> rather than this record, through
/// <see cref="ICarriesResponseBody"/>.
/// </remarks>
[HttpStatus(428)]
public sealed record PreconditionRequired<T>(T Body)
    : IHttpStatusResponse, ICarriesResponseBody {

    public string Type => ProblemTypes.PreconditionRequired;

    public string Title => "Precondition Required";

    public int Status => 428;

    object? ICarriesResponseBody.Body => Body;
}
