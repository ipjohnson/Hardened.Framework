namespace Hardened.Requests.Abstract.Responses;

/// <summary>
/// A 400 carrying a body the caller supplies.
/// </summary>
/// <remarks>
/// <para>
/// The generic form of <see cref="BadRequest"/>, for a caller who already has a payload type and does
/// not want a Hardened-shaped problem document. Both are the same problem kind and carry the same
/// <c>type</c> URI - that identifies what went wrong, not what shape the body is.
/// </para>
/// <para>
/// <b>The body is <see cref="Body"/>, not this record.</b> It implements
/// <see cref="ICarriesResponseBody"/>, so the generated dispatch sends the payload rather than a
/// wrapper with the payload nested inside it.
/// </para>
/// </remarks>
[HttpStatus(400)]
public sealed record BadRequest<T>(T Body)
    : IHttpStatusResponse, ICarriesResponseBody {

    public string Type => ProblemTypes.BadRequest;

    public string Title => "Bad Request";

    public int Status => 400;

    object? ICarriesResponseBody.Body => Body;
}
