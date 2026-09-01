namespace Hardened.Requests.Abstract.Errors;

/// <summary>
/// How a failed request is answered when the contract declared no body for it.
/// </summary>
/// <remarks>
/// One of the framework's two error envelopes. <c>type</c> and <c>message</c> appear on both,
/// and the detail rides in the one member the failure's shape calls for: here a single
/// <see cref="Details"/> string, because everything this envelope answers - a refused
/// authorization, a 406, an anonymous 500 - is one fact about the whole request. A validation
/// failure is per field, so <c>RequestValidationError</c> carries a field list instead. A reader
/// switches on <c>type</c> and knows which members follow.
/// </remarks>
public class ErrorModel {
    public string Type { get; set; } = "";

    public string Message { get; set; } = "";

    public string Details { get; set; } = "";
}