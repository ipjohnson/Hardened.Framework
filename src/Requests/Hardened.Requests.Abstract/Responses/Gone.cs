namespace Hardened.Requests.Abstract.Responses;

/// <summary>
/// The resource existed and has been removed on purpose - 410.
/// </summary>
/// <remarks>
/// Worth distinguishing from <see cref="NotFound"/> rather than folding into it: a 404 invites a
/// client to retry later or to suspect its own URL, and a 410 tells it to stop asking and to forget
/// the reference. Caches and crawlers act on the difference.
/// </remarks>
[HttpStatus(410)]
public sealed record Gone(string? Detail = null) : IHttpStatusResponse, IDeclaresStatus {

    public string Type => ProblemTypes.Gone;

    public string Title => "Gone";

    public static int StatusCode => 410;

    public int Status => StatusCode;
}
