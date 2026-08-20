using Hardened.Requests.Abstract.Headers;
using Microsoft.Extensions.Primitives;

namespace Hardened.Requests.Abstract.Responses;

/// <summary>
/// The work was taken and has not been done yet - 202, and where to watch it if there is anywhere.
/// </summary>
/// <remarks>
/// <para>
/// Bodyless, which is a choice rather than a rule: 202 permits a representation of the accepted
/// work, but a body that describes a thing which has not happened is the shape most likely to be
/// mistaken for a result. A caller who wants status polls <see cref="Location"/>; a caller who wants
/// to return a real progress representation declares their own 202 type, which is exactly what
/// <c>[HttpStatus(202)]</c> on a record of their own is for.
/// </para>
/// <para>
/// <see cref="Location"/> is optional here where a 201's is not. A creation always produced
/// something with an address; an acceptance may have produced nothing addressable yet, and a
/// fabricated polling URL that answers 404 for the first few seconds is worse than no header.
/// </para>
/// </remarks>
[HttpStatus(202)]
public sealed record Accepted(string? Location = null)
    : IHttpStatusResponse, IProvidesResponseHeaders {

    public int Status => 202;

    public bool HasBody => false;

    public void ApplyHeaders(IDictionary<string, StringValues> headers) {
        if (!string.IsNullOrEmpty(Location)) {
            headers[KnownHeaders.Location] = Location!;
        }
    }
}
