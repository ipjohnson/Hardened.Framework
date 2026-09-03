namespace Hardened.Requests.Abstract.Responses;

/// <summary>
/// An upstream this service depends on did not answer in time - 504.
/// </summary>
/// <remarks>
/// <para>
/// The mirror of <see cref="RequestTimeout"/>, one hop further out: that one is a request that
/// never finished arriving, this is a dependency that never finished answering. The caller's
/// request was complete and correct in both cases, which is why neither is a 4xx.
/// </para>
/// <para>
/// Distinct from <see cref="ServiceUnavailable"/>, which carries a <c>Retry-After</c> because a
/// service shedding load knows its own window. A timeout out at a dependency knows nothing about
/// when that dependency will recover, so there is no honest number to send.
/// </para>
/// </remarks>
[HttpStatus(504)]
public sealed record GatewayTimeout(string? Detail = null) : IHttpStatusResponse {

    public string Type => ProblemTypes.GatewayTimeout;

    public string Title => "Gateway Timeout";

    public int Status => 504;
}
