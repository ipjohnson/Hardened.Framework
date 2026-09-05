namespace Hardened.Requests.Abstract.Responses;

/// <summary>
/// The request cannot be applied to the current state of the resource - 409.
/// </summary>
/// <remarks>
/// The state, not the request: a 409 says the request was well-formed and would have succeeded
/// against a different version of the world, which is what separates it from a 400. Retrying it
/// unchanged is reasonable once the caller has re-read the resource, and is not otherwise.
/// </remarks>
[HttpStatus(409)]
public sealed record Conflict(string? Detail = null) : IHttpStatusResponse, IDeclaresStatus {

    /// <summary>
    /// The Conflict with a generic message, for a handler with nothing more to say than the status.
    /// Shared, so returning it allocates nothing.
    /// </summary>
    public static readonly Conflict Default = new("The request conflicts with the current state.");

    public string Type => ProblemTypes.Conflict;

    public string Title => "Conflict";

    public static int StatusCode => 409;

    public int Status => StatusCode;
}
