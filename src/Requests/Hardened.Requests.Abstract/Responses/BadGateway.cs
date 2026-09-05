namespace Hardened.Requests.Abstract.Responses;

/// <summary>
/// An upstream this service depends on answered, and its answer was unusable - 502.
/// </summary>
/// <remarks>
/// <para>
/// The upstream failure that is not a timeout. It answered - a malformed body, a status the
/// contract between the two does not allow, a payload that does not deserialize - so there is
/// nothing to wait for and retrying immediately will get the same thing.
/// <see cref="GatewayTimeout"/> is the other half, where nothing came back at all.
/// </para>
/// <para>
/// Not folded into 500. A 502 tells the caller that this service is working and something behind
/// it is not, which is the difference between "try again later" and "report a bug here".
/// </para>
/// </remarks>
[HttpStatus(502)]
public sealed record BadGateway(string? Detail = null) : IHttpStatusResponse, IDeclaresStatus {

    public string Type => ProblemTypes.BadGateway;

    public string Title => "Bad Gateway";

    public static int StatusCode => 502;

    public int Status => StatusCode;
}
