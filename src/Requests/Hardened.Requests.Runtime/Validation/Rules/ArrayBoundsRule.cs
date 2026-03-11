using System.Collections;
using Hardened.Requests.Abstract.Validation;

namespace Hardened.Requests.Runtime.Validation.Rules;

public class ArrayBoundsRule : IValidationRule {
    private readonly int? _minItems;
    private readonly int? _maxItems;

    public ArrayBoundsRule(int? minItems, int? maxItems) {
        _minItems = minItems;
        _maxItems = maxItems;
    }

    public void Validate(string parameterName, object? value, ValidationResult result) {
        if (value == null) return;

        int count;
        if (value is ICollection collection) {
            count = collection.Count;
        } else if (value is IEnumerable enumerable) {
            count = 0;
            foreach (var _ in enumerable) count++;
        } else {
            return;
        }

        if (_minItems.HasValue && count < _minItems.Value) {
            result.AddError(parameterName, "array_bounds",
                $"{parameterName} must have at least {_minItems.Value} items.");
        }

        if (_maxItems.HasValue && count > _maxItems.Value) {
            result.AddError(parameterName, "array_bounds",
                $"{parameterName} must have at most {_maxItems.Value} items.");
        }
    }
}
