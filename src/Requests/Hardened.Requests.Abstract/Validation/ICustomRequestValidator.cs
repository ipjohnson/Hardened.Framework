namespace Hardened.Requests.Abstract.Validation;

public interface ICustomRequestValidator<in TRequest> where TRequest : class {
    Task<ValidationResult> ValidateAsync(TRequest request);
}
