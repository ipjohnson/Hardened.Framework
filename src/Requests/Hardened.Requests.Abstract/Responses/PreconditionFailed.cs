namespace Hardened.Requests.Abstract.Responses;

/// <summary>
/// A conditional request whose condition did not hold - 412.
/// </summary>
/// <remarks>
/// The answer to an <c>If-Match</c> or <c>If-Unmodified-Since</c> that no longer describes the
/// resource, which is how a client performs a lost-update-safe write. Answering 409 instead would
/// be defensible prose and the wrong status: a client that sent a validator is waiting to be told
/// its validator is stale, and that is the one thing 412 means.
/// </remarks>
[HttpStatus(412)]
public sealed record PreconditionFailed(string? Detail = null)
    : IHttpStatusResponse, IDeclaresStatus {

    /// <summary>
    /// The PreconditionFailed with a generic message, for a handler with nothing more to say than the status.
    /// Shared, so returning it allocates nothing.
    /// </summary>
    public static readonly PreconditionFailed Default = new("A precondition on the request did not hold.");

    public string Type => ProblemTypes.PreconditionFailed;

    public string Title => "Precondition Failed";

    public static int StatusCode => 412;

    public int Status => StatusCode;
}
