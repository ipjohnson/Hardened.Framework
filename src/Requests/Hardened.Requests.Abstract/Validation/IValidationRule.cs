namespace Hardened.Requests.Abstract.Validation;

public interface IValidationRule {
    void Validate(string parameterName, object? value, ValidationResult result);
}
