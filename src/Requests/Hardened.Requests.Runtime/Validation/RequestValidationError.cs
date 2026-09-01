namespace Hardened.Requests.Runtime.Validation;

/// <summary>
/// How a request that failed validation is answered.
/// </summary>
/// <remarks>
/// <para>
/// One of the framework's two error envelopes, and the convention between them is this:
/// <c>type</c> and <c>message</c> appear on both, and the detail rides in the one member the
/// failure's shape calls for. A validation failure is inherently per field, so its detail is
/// <see cref="Errors"/> - a list naming each field, a code and a sentence. Everything else is a
/// single fact about the whole request, so <c>ErrorModel</c> carries its detail as one
/// <c>details</c> string. Two shapes, one rule; a reader switches on <c>type</c> and knows which
/// members follow.
/// </para>
/// <para>
/// Every path to a validation 400 produces this shape: the generated filters, a binder refusal,
/// a body the deserializer could not read, and a handler throwing <c>ValidationException</c>
/// itself. Which layer caught the failure is the framework's business and not the caller's.
/// </para>
/// </remarks>
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
