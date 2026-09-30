using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.Responses;
using Microsoft.Extensions.Primitives;

namespace Hardened.Web.Runtime.Responses;

/// <summary>
/// The work was taken and has not been done yet - 202, and where to watch it if there is anywhere.
/// </summary>
/// <remarks>
/// <para>
/// Bodyless: a caller who wants status polls <see cref="Location"/>. A 202 that describes the
/// accepted work - an identifier, a state - is <see cref="Accepted{T}"/>, which a test also reads
/// back with <c>Returns&lt;Accepted&lt;T&gt;&gt;()</c>. A body that describes a thing which has not
/// happened is easily mistaken for a result, so the representation should say what it is.
/// </para>
/// <para>
/// <see cref="Location"/> is optional here where a 201's is not. A creation always produced
/// something with an address; an acceptance may have produced nothing addressable yet, and a
/// fabricated polling URL that answers 404 for the first few seconds is worse than no header.
/// </para>
/// </remarks>
[HttpStatus(202)]
public sealed record Accepted(string? Location = null)
    : IHttpStatusResponse,
        IProvidesResponseHeaders,
        IResponseExpectation<Accepted>
{
    public static int StatusCode => 202;

    public int Status => StatusCode;

    public bool HasBody => false;

    public void ApplyHeaders(IDictionary<string, StringValues> headers)
    {
        if (!string.IsNullOrEmpty(Location))
        {
            headers[KnownHeaders.Location] = Location!;
        }
    }

    /// <summary>
    /// Every header the response carried, where <see cref="FromResponse"/> built this from a call.
    /// Empty where a handler built it, and never sent.
    /// </summary>
    public IReadOnlyDictionary<string, string> Headers => ResponseExpectation.HeadersOf(this);

    public static Accepted FromResponse(
        object? body,
        IReadOnlyDictionary<string, string> headers
    ) =>
        ResponseExpectation.Received<Accepted>(
            new(ResponseExpectation.OptionalHeader(headers, KnownHeaders.Location)),
            headers
        );
}
