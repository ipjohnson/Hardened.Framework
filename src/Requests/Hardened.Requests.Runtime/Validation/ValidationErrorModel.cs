namespace Hardened.Requests.Runtime.Validation;

public class ValidationErrorModel {
    public string Type { get; set; } = "";

    public string Message { get; set; } = "";

    public List<ValidationFieldError> Errors { get; set; } = new();
}

public class ValidationFieldError {
    public string Field { get; set; } = "";

    public string Code { get; set; } = "";

    public string Message { get; set; } = "";
}
