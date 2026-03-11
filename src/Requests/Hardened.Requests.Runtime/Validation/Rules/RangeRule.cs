using Hardened.Requests.Abstract.Validation;

namespace Hardened.Requests.Runtime.Validation.Rules;

public class RangeRule : IValidationRule {
    private readonly decimal? _minimum;
    private readonly decimal? _maximum;
    private readonly bool _exclusiveMinimum;
    private readonly bool _exclusiveMaximum;

    public RangeRule(decimal? minimum, decimal? maximum,
        bool exclusiveMinimum = false, bool exclusiveMaximum = false) {
        _minimum = minimum;
        _maximum = maximum;
        _exclusiveMinimum = exclusiveMinimum;
        _exclusiveMaximum = exclusiveMaximum;
    }

    public void Validate(string parameterName, object? value, ValidationResult result) {
        if (value == null) return;

        decimal numericValue;
        try {
            numericValue = Convert.ToDecimal(value);
        } catch {
            return;
        }

        if (_minimum.HasValue) {
            if (_exclusiveMinimum ? numericValue <= _minimum.Value : numericValue < _minimum.Value) {
                var op = _exclusiveMinimum ? "greater than" : "at least";
                result.AddError(parameterName, "range",
                    $"{parameterName} must be {op} {_minimum.Value}.");
            }
        }

        if (_maximum.HasValue) {
            if (_exclusiveMaximum ? numericValue >= _maximum.Value : numericValue > _maximum.Value) {
                var op = _exclusiveMaximum ? "less than" : "at most";
                result.AddError(parameterName, "range",
                    $"{parameterName} must be {op} {_maximum.Value}.");
            }
        }
    }
}
