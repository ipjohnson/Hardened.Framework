using Hardened.Requests.Abstract.Validation;

namespace Hardened.Requests.Runtime.Validation.Rules;

public class StringLengthRule : IValidationRule {
    private readonly int? _minLength;
    private readonly int? _maxLength;

    public StringLengthRule(int? minLength, int? maxLength) {
        _minLength = minLength;
        _maxLength = maxLength;
    }

    public void Validate(string parameterName, object? value, ValidationResult result) {
        if (value is not string s) return;

        if (_minLength.HasValue && s.Length < _minLength.Value) {
            result.AddError(parameterName, "string_length",
                $"{parameterName} must be at least {_minLength.Value} characters.");
        }

        if (_maxLength.HasValue && s.Length > _maxLength.Value) {
            result.AddError(parameterName, "string_length",
                $"{parameterName} must be at most {_maxLength.Value} characters.");
        }
    }
}
