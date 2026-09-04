using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Microsoft.Extensions.Primitives;

namespace Hardened.Web.Runtime.Conditional;

/// <summary>
/// Answers 304 to a GET or HEAD whose caller already holds the representation the handler would
/// have sent, and tags a response so that a caller can.
/// </summary>
/// <remarks>
/// <para>
/// Installed by <see cref="ConditionalGetAttribute"/> on an operation or a class, or on every GET
/// handler by <c>[Enable&lt;ConditionalGet&gt;]</c>, at
/// <see cref="Abstract.RequestFilter.FilterOrder.Conditional"/>: outside the response cache, so a
/// hit is revalidated as well as a miss, and outside compression, so a 304 carries no coding for a
/// body it does not have. Nothing installs it otherwise. A service whose responses are small and
/// change on every read gets nothing from a 304, and under this arrangement pays nothing for it.
/// </para>
/// <para>
/// <b>What it costs is decided on the first write.</b> A response that already carries an
/// <c>ETag</c> by then - a handler wrote one, the cache tagged the entry it captured, a hit
/// replayed one - is decided there and then: a 304, or the bytes straight through to the
/// transport. A response carrying none is held back and tagged over the bytes as sent once they
/// are all there, which is a buffer and a hash per response. That is the cost of declaring this
/// on a handler that writes no validator of its own, and the reason it is opt-in.
/// </para>
/// <para>
/// The tag covers the bytes as sent. Through the compression filter, which sits inside this one,
/// a gzip client and an identity client are handed different tags for one resource, as they are
/// for a compressed static file, and each is revalidated against its own.
/// </para>
/// <para>
/// <b>Not for a handler that streams.</b> Holding a response back means buffering it, so a
/// handler returning <c>IAsyncEnumerable&lt;T&gt;</c> under this answers no sooner than it has
/// finished - the same caution the response cache carries.
/// </para>
/// <para>
/// <c>If-Match</c> and <c>If-Unmodified-Since</c> are not evaluated. They guard a write against a
/// lost update, which needs the current validator before the handler runs, and nothing in the
/// pipeline knows it then. The rule for what matches is <see cref="Precondition"/>.
/// </para>
/// </remarks>
public sealed class ConditionalGetFilter : IExecutionFilter {

    public async Task Execute(IExecutionChain chain) {
        var context = chain.Context;
        var request = context.Request;
        var response = context.Response;

        // A method the conditionals are not defined for, a response something ahead of this stage
        // already started and so can neither be tagged nor turned into a 304, and a second
        // registration on one handler.
        if (!IsGetOrHead(request.Method) ||
            response.ResponseStarted ||
            response.Body is ConditionalResponseStream) {
            await chain.Next();

            return;
        }

        var transport = response.Body;
        var body = new ConditionalResponseStream(
            response,
            transport,
            Read(request.Headers, KnownHeaders.IfNoneMatch),
            Read(request.Headers, KnownHeaders.IfModifiedSince));

        response.Body = body;

        var completed = false;

        try {
            await chain.Next();

            completed = true;
        }
        finally {
            response.Body = transport;

            // Whatever was held back is written whether the chain completed or threw: the error
            // path serialized into the same buffer. Only the tag and the 304 wait for a chain that
            // completed, because a status decided underneath a failure is one the host overwrites.
            await body.CompleteAsync(completed, context.CancellationToken);
        }
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
