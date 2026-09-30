using Hardened.Requests.Abstract.Responses;

namespace Hardened.Web.Runtime.Responses;

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
    : IHttpStatusResponse,
        ICarriesResponseBody,
        IResponseExpectation<PreconditionRequired<T>>
{
    public string Type => ProblemTypes.PreconditionRequired;

    public string Title => "Precondition Required";

    public static int StatusCode => 428;

    public int Status => StatusCode;

    object? ICarriesResponseBody.Body => Body;

    /// <summary>
    /// Every header the response carried, where <see cref="FromResponse"/> built this from a call.
    /// Empty where a handler built it, and never sent.
    /// </summary>
    public IReadOnlyDictionary<string, string> Headers => ResponseExpectation.HeadersOf(this);

    public static PreconditionRequired<T> FromResponse(
        object? body,
        IReadOnlyDictionary<string, string> headers
    ) =>
        ResponseExpectation.Received<PreconditionRequired<T>>(
            new(ResponseExpectation.Body<T>(body)),
            headers
        );
}
