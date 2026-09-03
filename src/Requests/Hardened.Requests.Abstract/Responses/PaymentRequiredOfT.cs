namespace Hardened.Requests.Abstract.Responses;

/// <summary>
/// A 402 carrying a body the caller supplies.
/// </summary>
/// <remarks>
/// <para>
/// The generic form of <see cref="PaymentRequired"/>, for a caller who already has a payload type and does
/// not want a Hardened-shaped problem document. Both are the same problem kind and carry the same
/// <c>type</c> URI - that identifies what went wrong, not what shape the body is.
/// </para>
/// <para>
/// <b>The body is <see cref="Body"/>, not this record.</b> It implements
/// <see cref="ICarriesResponseBody"/>, so the generated dispatch sends the payload rather than a
/// wrapper with the payload nested inside it.
/// </para>
/// <para>
/// This is what a specification-first build binds a declared 402 with a body to, so a declared
/// error and a hand-written one are one type rather than two names for it - and two statuses
/// sharing one payload schema are two distinct case types rather than the CS0457 that
/// <c>PaymentRequired, PaymentRequired</c> would be.
/// </para>
/// </remarks>
[HttpStatus(402)]
public sealed record PaymentRequired<T>(T Body)
    : IHttpStatusResponse, ICarriesResponseBody {

    public string Type => ProblemTypes.PaymentRequired;

    public string Title => "Payment Required";

    public int Status => 402;

    object? ICarriesResponseBody.Body => Body;
}
