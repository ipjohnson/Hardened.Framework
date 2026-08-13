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

    public ValidationModules.ValidationResult ValidationResult { get; }
}
