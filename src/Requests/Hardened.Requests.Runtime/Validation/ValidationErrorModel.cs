namespace Hardened.Requests.Runtime.Validation;

public class RequestValidationError {
    public string Type { get; set; } = "";

    public string Message { get; set; } = "";

    public List<RequestValidationFieldError> Errors { get; set; } = new();
}

public class RequestValidationFieldError {
    public string Field { get; set; } = "";

    public string Code { get; set; } = "";

    public string Message { get; set; } = "";
}
