using Hardened.Requests.Abstract.Validation;

namespace Hardened.Requests.Runtime.Validation.Rules;

public class RequiredRule : IValidationRule {
    public static readonly RequiredRule Instance = new();

    public void Validate(string parameterName, object? value, ValidationResult result) {
        if (value == null || (value is string s && string.IsNullOrWhiteSpace(s))) {
            result.AddError(parameterName, "required", $"{parameterName} is required.");
        }
    }
}
