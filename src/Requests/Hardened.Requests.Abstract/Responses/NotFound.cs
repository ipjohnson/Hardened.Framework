namespace Hardened.Requests.Abstract.Responses;

/// <summary>
/// The resource named by the request does not exist - 404.
/// </summary>
/// <remarks>
/// <para>
/// <b>Sealed, like every built-in response type, and that is load-bearing rather than tidy.</b> A
/// user-defined <c>MyNotFound : NotFound</c> in the same response set as <c>NotFound</c> gives the
/// generated match no unambiguous order - either arm accepts the value, so which one runs depends
/// on the order they happen to be emitted in. Someone who needs more context on a 404 declares
/// their own type carrying <c>[HttpStatus(404)]</c>; two unrelated 404 types in one set are fine,
/// because neither is assignable to the other.
/// </para>
/// <para>
/// <see cref="Resource"/> is required rather than optional because a 404 that does not say what was
/// not found is indistinguishable from a routing mistake, and the two have completely different
/// fixes.
/// </para>
/// </remarks>
[HttpStatus(404)]
public sealed record NotFound(string Resource, string? Detail = null)
    : IHttpStatusResponse, IDeclaresStatus {

    /// <summary>
    /// The NotFound with a generic message, for a handler with nothing more to say than the status.
    /// Shared, so returning it allocates nothing.
    /// </summary>
    public static readonly NotFound Default = new("resource", "The requested resource does not exist.");

    public string Type => ProblemTypes.NotFound;

    public string Title => "Not Found";

    public static int StatusCode => 404;

    public int Status => StatusCode;
}
