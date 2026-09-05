using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.Serializer;

namespace Hardened.Requests.Runtime.Filters;

/// <summary>
/// Newline-delimited JSON: one document per line.
/// </summary>
/// <remarks>
/// The default framing, and what every <c>IAsyncEnumerable&lt;T&gt;</c> handler answered as before
/// there was a choice. <c>application/jsonl</c> is the same wire format under a different name and
/// OpenAPI 3.2 treats the two as equivalent; only this spelling is emitted, because it is the one
/// the pipeline has always committed to.
/// </remarks>
public class NdjsonFraming : IStreamFraming {
    /// <summary>The one instance, because it holds nothing.</summary>
    public static readonly NdjsonFraming Instance = new();

    /// <summary>
    /// The line terminator, written asynchronously for the reason <see cref="SseFraming"/>
    /// gives: a synchronous <c>WriteByte</c> is refused by Kestrel's response body.
    /// </summary>
    private static readonly byte[] Newline = "\n"u8.ToArray();

    public string ContentType => KnownContentType.NdJson;

    public async ValueTask WriteItem(
        IExecutionContext context, Func<IExecutionContext, Task> serialize) {
        await serialize(context);

        await context.Response.Body.WriteAsync(Newline, 0, Newline.Length, context.CancellationToken);
    }

    /// <summary>
    /// A newline, so the body is never zero bytes.
    /// </summary>
    /// <remarks>
    /// Lambda Function URLs do not close the body stream promptly for a zero-byte response, and a
    /// downstream reader waiting on one hangs. It costs a byte on a stream that produced nothing
    /// and it is what stops an empty result being indistinguishable from a hung one.
    /// </remarks>
    public async ValueTask WriteCompletion(IExecutionContext context) {
        await context.Response.Body.WriteAsync(Newline, 0, Newline.Length, context.CancellationToken);
    }

    /// <summary>
    /// Nothing. The format has no comment syntax, and a blank line is not an item every reader
    /// skips, so a quiet NDJSON stream cannot be kept alive from inside the body.
    /// </summary>
    public ValueTask<bool> WriteHeartbeat(IExecutionContext context) => new(false);
}
