namespace Hardened.Requests.Abstract.Responses;

/// <summary>
/// A 412 carrying a body the caller supplies.
/// </summary>
/// <remarks>
/// <para>
/// The generic form of <see cref="PreconditionFailed"/>, for a caller who already has a payload type and does
/// not want a Hardened-shaped problem document. Both are the same problem kind and carry the same
/// <c>type</c> URI - that identifies what went wrong, not what shape the body is.
/// </para>
/// <para>
/// <b>The body is <see cref="Body"/>, not this record.</b> It implements
/// <see cref="ICarriesResponseBody"/>, so the generated dispatch sends the payload rather than a
/// wrapper with the payload nested inside it.
/// </para>
/// <para>
/// This is what a specification-first build binds a declared status with a body to, so a declared
/// error and a hand-written one are one type rather than two names for it - and two statuses
/// sharing one payload schema are two distinct case types rather than the CS0457 that
/// <c>PreconditionFailed, PreconditionFailed</c> would be.
/// </para>
/// </remarks>
[HttpStatus(412)]
public sealed record PreconditionFailed<T>(T Body)
    : IHttpStatusResponse, ICarriesResponseBody, IResponseExpectation<PreconditionFailed<T>> {

    public string Type => ProblemTypes.PreconditionFailed;

    public string Title => "Precondition Failed";

    public static int StatusCode => 412;

    public int Status => StatusCode;

    object? ICarriesResponseBody.Body => Body;

    public static PreconditionFailed<T> FromResponse(
        object? body, IReadOnlyDictionary<string, string> headers) =>
        new(ResponseExpectation.Body<T>(body));
}
