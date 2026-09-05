using Hardened.Requests.Abstract.Headers;
using Microsoft.Extensions.Primitives;

namespace Hardened.Requests.Abstract.Responses;

/// <summary>
/// The representation the caller already holds is still current - 304.
/// </summary>
/// <remarks>
/// <para>
/// Bodyless because RFC 9110 says so rather than because this type chose it, which is why there is
/// no generic form beside it. <c>UnionResponseSelector.HasBody</c> answers false for 304 from the
/// status alone, so a <c>NotModified&lt;T&gt;</c> would be a wrapper whose payload the dispatch
/// discards - a type that compiles and cannot do the one thing its signature promises.
/// </para>
/// <para>
/// <see cref="ETag"/> is optional. A conditional request that was answered from
/// <c>If-None-Match</c> already carries the validator the client sent, and repeating it is the
/// ordinary thing to do; one answered from <c>If-Modified-Since</c> may have no entity tag to
/// repeat, and inventing one would tell the client it can revalidate against a value the store
/// does not know.
/// </para>
/// </remarks>
[HttpStatus(304)]
public sealed record NotModified(string? ETag = null)
    : IHttpStatusResponse, IProvidesResponseHeaders, IResponseExpectation<NotModified> {

    public static int StatusCode => 304;

    public int Status => StatusCode;

    public bool HasBody => false;

    public void ApplyHeaders(IDictionary<string, StringValues> headers) {
        if (!string.IsNullOrEmpty(ETag)) {
            headers[KnownHeaders.ETag] = ETag!;
        }
    }

    public static NotModified FromResponse(
        object? body, IReadOnlyDictionary<string, string> headers) =>
        new(ResponseExpectation.OptionalHeader(headers, KnownHeaders.ETag));
}
