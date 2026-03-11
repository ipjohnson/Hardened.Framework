using Hardened.Requests.Abstract.Validation;

namespace Hardened.Requests.Runtime.Validation.Rules;

public class EnumRule : IValidationRule {
    private readonly HashSet<string> _allowedValues;

    public EnumRule(IEnumerable<string> allowedValues) {
        _allowedValues = new HashSet<string>(allowedValues, StringComparer.OrdinalIgnoreCase);
    }

    public void Validate(string parameterName, object? value, ValidationResult result) {
        if (value == null) return;

        var stringValue = value.ToString();
        if (stringValue != null && !_allowedValues.Contains(stringValue)) {
            var allowed = string.Join(", ", _allowedValues);
            result.AddError(parameterName, "enum",
                $"{parameterName} must be one of: {allowed}.");
        }
    }
}
