namespace Hardened.Requests.Testing.Conformance;

/// <summary>
/// What a client ends up with, after a transport has finished writing a response.
/// </summary>
/// <remarks>
/// <para>
/// The counterpart to <c>ConformanceRequestSpec</c>, and the half the request suite has no
/// equivalent of. A request is asserted on the way in, by reading the <c>IExecutionRequest</c> an
/// adapter produced. A response cannot be checked that way: every value on
/// <c>IExecutionResponse</c> can be perfectly correct and still never reach anybody, which is
/// exactly what happened twice — a status that was set and then discarded by the ASP.NET host
/// because no bytes had flushed, and a cookie appended to a collection nothing serialised.
/// </para>
/// <para>
/// So the adapter drives its own completion path and reports what came out. The mechanism differs —
/// an <c>HttpResponse</c>, a response feature, a proxy response object with a <c>cookies</c> array
/// beside its headers — and the meaning does not, which is the whole claim under test.
/// </para>
/// </remarks>
/// <param name="StatusCode">
/// The status the client sees. Not nullable: by the time a response has been written, something has
/// decided, and a transport that leaves <c>IExecutionResponse.Status</c> null reports the default it
/// sent rather than passing the null along.
/// </param>
/// <param name="SetCookies">
/// Fully formed cookie strings — <c>name=value; Path=/; HttpOnly</c> — however this transport
/// carries them. On an HTTP host they are the <c>Set-Cookie</c> header values; on API Gateway they
/// are the proxy response's <c>cookies</c> array. Normalising here is what lets one assertion cover
/// both without knowing which is which.
/// </param>
public record ObservedResponse(
    int StatusCode,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Headers,
    IReadOnlyList<string> SetCookies,
    byte[] Body) {

    /// <summary>The first value of a header, or null. Names are matched as they are over the wire.</summary>
    public string? Header(string name) {
        foreach (var pair in Headers) {
            if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase)) {
                return pair.Value.Count > 0 ? pair.Value[0] : null;
            }
        }

        return null;
    }

    public string BodyAsText() => System.Text.Encoding.UTF8.GetString(Body);
}
