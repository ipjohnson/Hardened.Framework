namespace Hardened.Requests.Abstract.Responses;

/// <summary>
/// The request was not one this service can act on - 400.
/// </summary>
/// <remarks>
/// <para>
/// The gap this closes was measured rather than assumed: nine applications built against this
/// framework each invented their own 400 shape, because there was no built-in one and 400 is the
/// most-answered client error there is.
/// </para>
/// <para>
/// <b>Not what a validation failure returns.</b> A failed constraint answers 400 with
/// <c>RequestValidationError</c>, which carries the field-level list a caller needs to fix the
/// payload. This is for the 400s that are not field-level - a parameter combination that cannot be
/// satisfied, a state the request does not fit - where "which field" is the wrong question and a
/// list of none would be noise.
/// </para>
/// </remarks>
[HttpStatus(400)]
public sealed record BadRequest(string? Detail = null) : IHttpStatusResponse, IDeclaresStatus {

    /// <summary>
    /// The BadRequest with a generic message, for a handler with nothing more to say than the status.
    /// Shared, so returning it allocates nothing.
    /// </summary>
    public static readonly BadRequest Default = new("The request could not be understood.");

    public string Type => ProblemTypes.BadRequest;

    public string Title => "Bad Request";

    public static int StatusCode => 400;

    public int Status => StatusCode;
}
