using Hardened.Requests.Runtime.Errors;

namespace Hardened.Requests.Runtime.Validation;

/// <summary>
/// Thrown when a request's parameters do not satisfy their constraints.
/// </summary>
/// <remarks>
/// <para>
/// Deriving from <see cref="BadRequestException"/> is what makes this a 400: client errors are
/// identified by type. The response itself is written by
/// <see cref="Errors.ExceptionToModelConverter"/> rather than by the filter, so there is one place
/// that decides what a validation failure looks like on the wire.
/// </para>
/// <para>
/// The result is ValidationModules'. Hardened does not re-declare the error model - the whole point
/// of the dependency is that structural validation, whatever produced it, reports failures in one
/// shape.
/// </para>
/// </remarks>
public class ValidationException : BadRequestException {
    public ValidationException(ValidationModules.ValidationResult validationResult)
        : base("One or more validation errors occurred.") {
        ValidationResult = validationResult;
    }

    /// <summary>
    /// For a failure that had an underlying cause - a value that would not convert to the type it
    /// was declared as.
    /// </summary>
    /// <remarks>
    /// The response body is the field errors either way; the caller is told <c>limit</c> is not a
    /// valid <c>Int32</c> and has no use for a stack trace. The inner exception is for the log,
    /// where "abc is not in a correct format" is the difference between a diagnosable failure and a
    /// bare assertion that something was wrong.
    /// </remarks>
    public ValidationException(ValidationModules.ValidationResult validationResult, Exception inner)
        : base("One or more validation errors occurred.", inner) {
        ValidationResult = validationResult;
    }

    public ValidationModules.ValidationResult ValidationResult { get; }
}
