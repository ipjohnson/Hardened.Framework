namespace Hardened.Requests.Abstract.Responses;

/// <summary>
/// The service does not support what was asked at all - 501.
/// </summary>
/// <remarks>
/// <para>
/// The honest answer from a stub. A generated service interface is implemented before every
/// operation is written, and the alternatives are a <c>NotImplementedException</c> - which reaches
/// the caller as a 500, a server fault the request did not cause - or a fabricated success.
/// </para>
/// <para>
/// About the service rather than the resource: 501 says this service will never answer this,
/// where a 405 says this resource will not answer this method and names the ones that will.
/// </para>
/// </remarks>
[HttpStatus(501)]
public sealed record NotImplemented(string? Detail = null) : IHttpStatusResponse, IDeclaresStatus {

    /// <summary>
    /// The NotImplemented with a generic message, for a handler with nothing more to say than the status.
    /// Shared, so returning it allocates nothing.
    /// </summary>
    public static readonly NotImplemented Default = new("This operation is not implemented.");

    public string Type => ProblemTypes.NotImplemented;

    public string Title => "Not Implemented";

    public static int StatusCode => 501;

    public int Status => StatusCode;
}
