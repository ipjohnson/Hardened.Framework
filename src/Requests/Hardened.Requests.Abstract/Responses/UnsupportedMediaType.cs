namespace Hardened.Requests.Abstract.Responses;

/// <summary>
/// The body arrived in a media type this service cannot read - 415.
/// </summary>
/// <remarks>
/// <para>
/// The request half of content negotiation, and the mirror of <see cref="NotAcceptable"/>. This is
/// about the <c>Content-Type</c> the caller sent; that one is about the <c>Accept</c> they will
/// take. Answering either with the other tells the caller to change the wrong header.
/// </para>
/// <para>
/// Distinct from a 400 on purpose. A body this service cannot parse at all is a media-type
/// mismatch and the fix is a header; a body it parsed and refused is a validation failure and the
/// fix is in the payload. Collapsing the two into 400 loses which of the two the caller has to do.
/// </para>
/// </remarks>
[HttpStatus(415)]
public sealed record UnsupportedMediaType(string? Detail = null)
    : IHttpStatusResponse, IDeclaresStatus {

    /// <summary>
    /// The UnsupportedMediaType with a generic message, for a handler with nothing more to say than the status.
    /// Shared, so returning it allocates nothing.
    /// </summary>
    public static readonly UnsupportedMediaType Default = new("The request's media type is not supported.");

    public string Type => ProblemTypes.UnsupportedMediaType;

    public string Title => "Unsupported Media Type";

    public static int StatusCode => 415;

    public int Status => StatusCode;
}
