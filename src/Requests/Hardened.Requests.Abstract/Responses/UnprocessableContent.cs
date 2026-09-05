namespace Hardened.Requests.Abstract.Responses;

/// <summary>
/// The body was understood and cannot be acted on - 422.
/// </summary>
/// <remarks>
/// <para>
/// RFC 9110's current name for what RFC 4918 called <c>Unprocessable Entity</c>. The status a
/// contract declares for validation refusal, and the one <c>ValidationErrorStatus</c> already
/// routes a failed constraint to when an operation declares it - so a declared 422 had a wired
/// status and no type to name.
/// </para>
/// <para>
/// <b>Not what <c>RequestValidationError</c> replaces.</b> That carries the field-level list a
/// caller needs to fix a payload, and a handler with one to send returns
/// <c>UnprocessableContent&lt;RequestValidationError&gt;</c>. This bare form is for the refusals
/// that are about the request as a whole - a state the body describes that the resource cannot be
/// moved to - where "which field" has no answer.
/// </para>
/// </remarks>
[HttpStatus(422)]
public sealed record UnprocessableContent(string? Detail = null)
    : IHttpStatusResponse, IDeclaresStatus {

    public string Type => ProblemTypes.UnprocessableContent;

    public string Title => "Unprocessable Content";

    public static int StatusCode => 422;

    public int Status => StatusCode;
}
