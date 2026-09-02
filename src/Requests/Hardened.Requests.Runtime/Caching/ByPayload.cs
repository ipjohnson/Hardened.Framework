using System.Security.Cryptography;
using Hardened.Requests.Abstract.Caching;
using Hardened.Requests.Abstract.Execution;

namespace Hardened.Requests.Runtime.Caching;

/// <summary>
/// Keys the response on the whole request body.
///
/// <code>
/// [HardenedFunction]
/// [CacheResponse&lt;ByPayload&gt;(Duration = 300)]
/// public Quote Price(QuoteRequest request) =&gt; _pricing.Quote(request);
/// </code>
///
/// <para>
/// The strategy for a function handler, and the one that is correct in a way a URL key is not: the
/// payload is the whole input rather than part of it. A directly invoked Lambda is authorized by
/// IAM at the boundary and <c>ILambdaContext</c> carries no caller principal at all, so a function
/// cannot vary its answer by caller even if it wanted to - and the moment a caller does pass a
/// tenant or a user id, it is in the payload.
/// </para>
/// </summary>
/// <remarks>
/// <para>
/// <b>It buffers the body, which is the one part of caching that is not free.</b> The filter runs
/// ahead of the bind, so hashing the body consumes the stream the bind is about to read; the buffer
/// is put back in its place, rewound, before the chain continues. A GET has no body and pays
/// nothing.
/// </para>
/// <para>
/// SHA-256 rather than a cheaper hash, and not for secrecy: the key is a lookup, but two different
/// payloads colliding means one caller is served another's answer, so the cost of a collision is
/// the same as the cost of a leak. It is also what the rest of this framework hashes with, and
/// <c>MD5.Create()</c> throws outright on a FIPS-enforcing host.
/// </para>
/// </remarks>
public sealed class ByPayload : ICacheKeyProvider {

    private static readonly ByPayload _instance = new();

    private ByPayload() { }

    /// <summary>
    /// Takes no values, and says so.
    /// </summary>
    /// <remarks>
    /// <c>params string[]</c> cannot express "no arguments", so
    /// <c>[CacheResponse&lt;ByPayload&gt;("culture")]</c> compiles clean. Refusing it here turns
    /// that into a failure naming the handler as its filter chain is built, instead of an argument
    /// silently doing nothing.
    /// </remarks>
    public static ICacheKeyProvider Create(string[] values) =>
        values.Length == 0
            ? _instance
            : throw new ArgumentException(
                "ByPayload keys on the request body and takes no values, but was given " +
                string.Join(", ", values) + ".",
                nameof(values));

    public async ValueTask<string?> Key(IExecutionContext context) {
        var body = context.Request.Body;

        if (body == Stream.Null) {
            return string.Empty;
        }

        var buffer = new MemoryStream();

        await body.CopyToAsync(buffer, context.CancellationToken);

        var payload = buffer.GetBuffer();
        var hash = SHA256.HashData(payload.AsSpan(0, (int)buffer.Length));

        // Rewound and put back, because the bind at FilterOrder.Serialization reads this next and
        // the stream it was going to read has just been drained.
        buffer.Position = 0;

        context.Request.Body = buffer;

        return Convert.ToBase64String(hash);
    }
}
