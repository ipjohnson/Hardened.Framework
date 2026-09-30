using Hardened.Requests.Abstract.Errors;
using Hardened.Requests.Abstract.Responses;

namespace Hardened.Requests.Runtime.Validation;

/// <summary>
/// How a request that failed validation is answered: a problem details document whose
/// <c>errors</c> extension member names each field that failed.
/// </summary>
/// <remarks>
/// <para>
/// Every path to a validation failure produces this shape: the generated filters, a binder refusal,
/// a body the deserializer could not read, and a handler throwing <c>ValidationException</c>
/// itself. Which layer caught the failure is the framework's business and not the caller's.
/// </para>
/// <para>
/// Its <c>type</c> is its own rather than the status's, because a validation failure is answered
/// at 400 or at the 422 an operation declares, and a client matching on <c>type</c> should find
/// the field list either way.
/// </para>
/// </remarks>
public class RequestValidationError : IProblemDetails
{
    /// <summary>The problem type every validation failure is sent with.</summary>
    public const string ProblemType = ErrorModel.TypePrefix + "validation-failed";

    /// <summary>Each field that failed, with a code and a sentence.</summary>
    public List<RequestValidationFieldError> Errors { get; set; } = new();

    /// <summary>A sentence about the whole request.</summary>
    public string? Detail { get; set; } = "One or more validation errors occurred.";

    public string Type { get; set; } = ProblemType;

    public string Title { get; set; } = "Request Validation Failed";

    public int Status { get; set; } = 400;
}

public class RequestValidationFieldError
{
    public string Field { get; set; } = "";

    public string Code { get; set; } = "";

    public string Message { get; set; } = "";
}
