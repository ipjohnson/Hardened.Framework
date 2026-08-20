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
public sealed record PreconditionFailed(string? Detail = null) : IHttpStatusResponse {

    public string Type => ProblemTypes.PreconditionFailed;

    public string Title => "Precondition Failed";

    public int Status => 412;
}
