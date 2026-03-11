using System.Text.RegularExpressions;
using Hardened.Requests.Abstract.Validation;

namespace Hardened.Requests.Runtime.Validation.Rules;

public class PatternRule : IValidationRule {
    private readonly Regex _regex;
    private readonly string _pattern;

    public PatternRule(string pattern) {
        _pattern = pattern;
        _regex = new Regex(pattern, RegexOptions.Compiled);
    }

    public void Validate(string parameterName, object? value, ValidationResult result) {
        if (value is not string s) return;

        if (!_regex.IsMatch(s)) {
            result.AddError(parameterName, "pattern",
                $"{parameterName} must match pattern '{_pattern}'.");
        }
    }
}
