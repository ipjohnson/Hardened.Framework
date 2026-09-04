using Hardened.Requests.Abstract.Execution;

namespace Hardened.Web.Runtime.Handlers;

/// <summary>
/// The response half of routing HEAD to the GET handler.
///
/// <para>
/// The routing table sends HEAD to the GET leaf, so the handler, its filters and its serializer
/// all run exactly as they would for the GET. That is the point: RFC 9110 requires a HEAD
/// response to carry the header fields it would have carried for a GET, and the only way to be
/// sure of that is to produce them the same way. What differs is that the bytes are counted and
/// dropped instead of written.
/// </para>
///
/// <para>
/// Content-Length is set from the count, so a health checker or CDN gets the size without the
/// payload. Discarding on the way out rather than skipping serialization is what makes that
/// number real rather than a guess — a compressed or templated response has no length anyone can
/// compute up front.
/// </para>
/// </summary>
internal static class HeadRequest {
    private const string Method = "HEAD";

    private const string ContentLengthHeader = "Content-Length";

    public static bool IsHead(IExecutionContext context) =>
        string.Equals(context.Request.Method, Method, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Runs <paramref name="chain"/> with the response body swapped for a counter, then reports
    /// what the body would have been.
    /// </summary>
    public static async Task ExecuteWithoutBody(IExecutionChain chain, IExecutionContext context) {
        var discard = new DiscardingStream();
        var responseBody = context.Response.Body;

        context.Response.Body = discard;

        try {
            await chain.Next();
        }
        finally {
            context.Response.Body = responseBody;

            // Nothing reached the real stream, so the response cannot have started - but a filter
            // may have started it deliberately, and rewriting headers after that throws on Kestrel.
            //
            // A 304 reports no length either. The count is of bytes the conditional filter
            // discarded, not of the body a 200 would have carried, and RFC 9110 §8.6 lets a 304
            // carry Content-Length only when it is that one.
            if (!context.Response.ResponseStarted && context.Response.Status != 304) {
                context.Response.Headers[ContentLengthHeader] =
                    discard.BytesWritten.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
        }
    }

    /// <summary>
    /// Write-only, counts, keeps nothing. Every write path the framework has ends at
    /// <c>Response.Body</c> — the serializers, the raw output helper, the gzip wrapper and the
    /// newline the async-enumerable filter writes between items — so swapping this in covers all
    /// of them without any of them knowing.
    /// </summary>
    private sealed class DiscardingStream : Stream {
        public long BytesWritten { get; private set; }

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => BytesWritten;

        public override long Position {
            get => BytesWritten;
            set => throw new NotSupportedException();
        }

        public override void Flush() { }

        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public override void Write(byte[] buffer, int offset, int count) => BytesWritten += count;

        public override void WriteByte(byte value) => BytesWritten++;

        public override Task WriteAsync(
            byte[] buffer, int offset, int count, CancellationToken cancellationToken) {
            BytesWritten += count;

            return Task.CompletedTask;
        }

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) {
            BytesWritten += buffer.Length;

            return default;
        }

        public override void Write(ReadOnlySpan<byte> buffer) => BytesWritten += buffer.Length;

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
