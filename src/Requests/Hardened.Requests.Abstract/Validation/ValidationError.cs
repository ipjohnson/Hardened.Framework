namespace Hardened.Requests.Abstract.Validation;

public record ValidationError(string Field, string Code, string Message);
