using System.IO.Pipelines;

namespace Hardened.Amz.Shared.Lambda.Runtime.Streaming;

/// <summary>
/// The body of a response in stream mode. The pipeline writes into a pipe; the first byte opens
/// the Lambda response stream and starts a pump that copies the pipe into it.
/// </summary>
/// <remarks>
/// <para>
/// Opening late is the point. Creating the stream sends the prelude, so status and headers have to
/// be final by then, and the pipeline has finished deciding them by the time it writes the first
/// byte. A refusal serialized by the pipeline opens the stream with the refusal's status; a
/// response that throws before writing never opens one, and the error goes back through the
/// bootstrap's error endpoint as a complete failure.
/// </para>
/// <para>
/// A pipe rather than the Lambda stream directly because serializers write synchronously and
/// <c>LambdaResponseStream.Write</c> blocks on its async path. Synchronous writes land in the pipe
/// and reach the socket at the next <see cref="FlushAsync"/> or at <see cref="CompleteAsync"/>;
/// asynchronous writes reach it as they happen. Each read the pump takes becomes one write on the
/// Lambda stream, which the runtime flushes per write, so a per-item flush is a chunk per item and
/// there is no timer between them.
/// </para>
/// </remarks>
public sealed class ResponseStream : Stream {
    private readonly Pipe _pipe = new();
    private readonly Func<Stream> _open;
    private Task? _pump;
    private long _written;

    /// <param name="open">
    /// Opens the Lambda response stream. Called once, on the first write or flush, and never when
    /// nothing is written.
    /// </param>
    public ResponseStream(Func<Stream> open) {
        _open = open;
    }

    /// <summary>
    /// Whether the Lambda response stream has been opened, which is the moment the prelude is
    /// committed.
    /// </summary>
    public bool HasResponseStarted => _pump != null;

    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    /// <summary>The bytes written so far.</summary>
    public override long Length => _written;

    public override long Position {
        get => _written;
        set => throw new NotSupportedException("Seeking in this stream is not supported.");
    }

    public override void Write(byte[] buffer, int offset, int count) {
        Write(buffer.AsSpan(offset, count));
    }

    public override void Write(ReadOnlySpan<byte> buffer) {
        EnsureStarted();

        var span = _pipe.Writer.GetSpan(buffer.Length);
        buffer.CopyTo(span);
        _pipe.Writer.Advance(buffer.Length);

        _written += buffer.Length;
    }

    public override void WriteByte(byte value) {
        EnsureStarted();

        var span = _pipe.Writer.GetSpan(1);
        span[0] = value;
        _pipe.Writer.Advance(1);

        _written += 1;
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) {
        return WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) {
        EnsureStarted();

        await _pipe.Writer.WriteAsync(buffer, cancellationToken);

        _written += buffer.Length;
    }

    public override async Task FlushAsync(CancellationToken cancellationToken) {
        EnsureStarted();

        await _pipe.Writer.FlushAsync(cancellationToken);
    }

    /// <summary>
    /// Nothing. A synchronous flush cannot wait on the pump without blocking the thread it runs
    /// on; the bytes go at the next asynchronous flush or at completion.
    /// </summary>
    public override void Flush() { }

    /// <summary>
    /// Closes the pipe and waits until every byte written has been handed to the Lambda stream.
    /// The host calls it once after the pipeline returns, and before it returns to the bootstrap,
    /// so the runtime's terminator cannot race a write still in flight. A stream that never
    /// started has nothing to wait for.
    /// </summary>
    public async Task CompleteAsync() {
        if (_pump == null) {
            return;
        }

        await _pipe.Writer.CompleteAsync();
        await _pump;
    }

    private void EnsureStarted() {
        if (_pump != null) {
            return;
        }

        var target = _open();

        _pump = PumpAsync(target);
    }

    private async Task PumpAsync(Stream target) {
        var reader = _pipe.Reader;

        try {
            while (true) {
                var result = await reader.ReadAsync();
                var buffer = result.Buffer;

                if (!buffer.IsEmpty) {
                    foreach (var segment in buffer) {
                        await target.WriteAsync(segment);
                    }

                    await target.FlushAsync();
                }

                reader.AdvanceTo(buffer.End);

                if (result.IsCompleted) {
                    break;
                }
            }
        }
        finally {
            await reader.CompleteAsync();
        }
    }

    public override int Read(byte[] buffer, int offset, int count) {
        throw new NotSupportedException("Reading from this stream is not supported.");
    }

    public override long Seek(long offset, SeekOrigin origin) {
        throw new NotSupportedException("Seeking in this stream is not supported.");
    }

    public override void SetLength(long value) {
        throw new NotSupportedException("SetLength is not supported for this stream.");
    }
}
