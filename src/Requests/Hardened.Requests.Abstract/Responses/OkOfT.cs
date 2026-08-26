using Microsoft.Extensions.Primitives;

namespace Hardened.Requests.Abstract.Responses;

/// <summary>
/// A 200 carrying a body and headers the handler chooses.
/// </summary>
/// <remarks>
/// <para>
/// The partner to <see cref="Created{T}"/>, and the one the study found missing on every route that
/// needed it. A handler returning <c>T</c> answers 200 and sets no headers, which is right almost
/// always - but an <c>ETag</c> on a read, a <c>Cache-Control</c> on a computed document, a
/// <c>RateLimit-Remaining</c> on a metered one, are all 200s whose headers are part of the answer.
/// Without this, each of those meant hand-writing a filter to put a header on a response the handler
/// had already produced, re-deriving from the payload what the handler knew when it returned.
/// </para>
/// <para>
/// <b>Headers are supplied rather than fixed.</b> <see cref="Created{T}"/> can hard-code
/// <c>Location</c> because a 201 has exactly one header that matters. A 200 does not, so this takes
/// what to set - which also means it does not need a variant per header.
/// </para>
/// <para>
/// <b>The body is <see cref="Value"/>, not this record</b>, through
/// <see cref="ICarriesResponseBody"/> - the same reason <see cref="Created{T}"/> does it.
/// Serializing the wrapper would put the caller's resource under a <c>value</c> member and the
/// headers in the body as well as on the response.
/// </para>
/// <para>
/// No non-generic <c>Ok</c>. A 200 with no body is 204, which <see cref="NoContent"/> already is,
/// and offering both would make "no body" a choice between two spellings that mean different things
/// on the wire.
/// </para>
/// </remarks>
[HttpStatus(200)]
public sealed record Ok<T>(T Value, IReadOnlyDictionary<string, string>? Headers = null)
    : IHttpStatusResponse, IProvidesResponseHeaders, ICarriesResponseBody {

    /// <summary>
    /// A 200 carrying one header, which is the common case - an <c>ETag</c>.
    /// </summary>
    public Ok(T value, string headerName, string headerValue)
        : this(value, new Dictionary<string, string> { [headerName] = headerValue }) { }

    public int Status => 200;

    object? ICarriesResponseBody.Body => Value;

    public void ApplyHeaders(IDictionary<string, StringValues> headers) {
        if (Headers == null) {
            return;
        }

        foreach (var header in Headers) {
            headers[header.Key] = header.Value;
        }
    }
}
