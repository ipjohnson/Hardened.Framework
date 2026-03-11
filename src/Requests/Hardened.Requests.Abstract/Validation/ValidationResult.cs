namespace Hardened.Requests.Abstract.Validation;

public class ValidationResult {
    public static readonly ValidationResult Success = new();

    private List<ValidationError>? _errors;

    public bool IsValid => _errors == null || _errors.Count == 0;

    public IReadOnlyList<ValidationError> Errors => _errors ?? (IReadOnlyList<ValidationError>)Array.Empty<ValidationError>();

    public void AddError(string field, string code, string message) {
        _errors ??= new List<ValidationError>();
        _errors.Add(new ValidationError(field, code, message));
    }

    public void Merge(ValidationResult other) {
        if (other.IsValid) return;

        _errors ??= new List<ValidationError>();
        _errors.AddRange(other.Errors);
    }
}
