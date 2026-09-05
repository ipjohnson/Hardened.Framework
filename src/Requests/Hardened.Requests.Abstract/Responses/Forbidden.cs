namespace Hardened.Requests.Abstract.Responses;

/// <summary>
/// The caller is authenticated and still may not do this - 403.
/// </summary>
/// <remarks>
/// Distinct from <see cref="Unauthorized"/> and not interchangeable with it: a 401 says "I do not
/// know who you are, here is how to tell me", and a 403 says "I know, and the answer is no". A 403
/// carries no challenge, because there is no credential a client could present that would change
/// the outcome - sending one invites an authentication loop over a decision that has already been
/// made.
/// </remarks>
[HttpStatus(403)]
public sealed record Forbidden(string? Detail = null) : IHttpStatusResponse, IDeclaresStatus {

    /// <summary>
    /// The Forbidden with a generic message, for a handler with nothing more to say than the status.
    /// Shared, so returning it allocates nothing.
    /// </summary>
    public static readonly Forbidden Default = new("This request is not permitted.");

    public string Type => ProblemTypes.Forbidden;

    public string Title => "Forbidden";

    public static int StatusCode => 403;

    public int Status => StatusCode;
}
