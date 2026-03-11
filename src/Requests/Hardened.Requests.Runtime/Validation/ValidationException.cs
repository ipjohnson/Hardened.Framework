using Hardened.Requests.Abstract.Validation;
using Hardened.Requests.Runtime.Errors;

namespace Hardened.Requests.Runtime.Validation;

public class ValidationException : BadRequestException {
    public ValidationException(ValidationResult validationResult)
        : base("One or more validation errors occurred.") {
        ValidationResult = validationResult;
    }

    public ValidationResult ValidationResult { get; }
}
