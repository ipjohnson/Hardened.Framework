using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Microsoft.Extensions.Primitives;

namespace Hardened.Web.Runtime.Conditional;

/// <summary>
/// Answers 304 to a GET or HEAD whose caller already holds the representation the handler would
/// have sent.
/// </summary>
/// <remarks>
/// <para>
/// Installed on every GET handler by <c>HardenedWebModule</c>, at
/// <see cref="Abstract.RequestFilter.FilterOrder.Conditional"/>: outside the response cache, so a
/// hit is revalidated as well as a miss, and outside compression, so a 304 carries no coding for a
/// body it does not have. A request carrying neither <c>If-None-Match</c> nor
/// <c>If-Modified-Since</c> costs two header lookups and is otherwise untouched.
/// </para>
/// <para>
/// <b>The decision is made on the first write, which is the first moment it can be.</b> The
/// validator is on the response by then - a handler sets <c>ETag</c> before it returns, the cache
/// sets one as it captures, a hit replays one - and nothing has reached the transport, so the
/// status and the headers can still change. A 304 then discards everything written after it: the
/// serializer's bytes on a miss, the stored bytes on a hit, and the encoder's trailer, because the
/// compressing body sits inside this one. A response that writes nothing is decided as the chain
/// returns.
/// </para>
/// <para>
/// A response with no validator is never a 304, and nothing here computes one. The response cache
/// tags what it stores, and a handler that knows its resource's version writes <c>ETag</c> or
/// <c>Last-Modified</c> itself. The rule for what matches is <see cref="ConditionalGet"/>.
/// </para>
/// <para>
/// <c>If-Match</c> and <c>If-Unmodified-Since</c> are not evaluated. They guard a write against a
/// lost update, which needs the current validator before the handler runs, and nothing in the
/// pipeline knows it then.
/// </para>
/// </remarks>
public sealed class ConditionalRequestFilter : IExecutionFilter {

    public async Task Execute(IExecutionChain chain) {
        var context = chain.Context;
        var request = context.Request;
        var response = context.Response;

        var ifNoneMatch = Read(request.Headers, KnownHeaders.IfNoneMatch);
        var ifModifiedSince = Read(request.Headers, KnownHeaders.IfModifiedSince);

        // In order of how often each is the reason: an unconditional request, a method the
        // conditionals are not defined for, a response something ahead of this stage already
        // started and so cannot be turned into a 304, and a second registration on one handler.
        if ((ifNoneMatch.Count == 0 && ifModifiedSince.Count == 0) ||
            !IsGetOrHead(request.Method) ||
            response.ResponseStarted ||
            response.Body is ConditionalResponseStream) {
            await chain.Next();

            return;
        }

        var transport = response.Body;
        var body = new ConditionalResponseStream(response, transport, ifNoneMatch, ifModifiedSince);

        response.Body = body;

        try {
            await chain.Next();
        }
        finally {
            response.Body = transport;
        }

        // Only when the chain completed. A chain that threw is answered by whatever catches it,
        // and a 304 decided underneath a failure would be a status the host then overwrites.
        body.Complete();
    }

    /// <summary>
    /// Whether <paramref name="method"/> is one the conditionals answer a 304 for. RFC 9110 §13.1.2
    /// defines them for GET and HEAD; on any other method they mean a 412, which is not evaluated
    /// here.
    /// </summary>
    internal static bool IsGetOrHead(string method) =>
        string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A header value, looked up the way HTTP defines header names, because API Gateway delivers
    /// them lowercased and a forked request carries whatever dictionary it was handed.
    /// </summary>
    private static StringValues Read(IDictionary<string, StringValues> headers, string name) {
        if (headers.TryGetValue(name, out var value)) {
            return value;
        }

        foreach (var header in headers) {
            if (string.Equals(header.Key, name, StringComparison.OrdinalIgnoreCase)) {
                return header.Value;
            }
        }

        return StringValues.Empty;
    }
}
