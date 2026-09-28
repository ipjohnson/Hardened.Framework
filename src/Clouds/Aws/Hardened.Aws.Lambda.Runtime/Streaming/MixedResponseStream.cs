namespace Hardened.Aws.Lambda.Runtime.Streaming;

/// <summary>
/// The body of a response in mixed mode. Its first write or asynchronous flush decides where the
/// body goes: to a <see cref="ResponseStream"/>, which opens the Lambda response stream, or into
/// <see cref="Collected"/>, for an answer sent as one payload when the handler returns.
/// </summary>
/// <remarks>
/// <para>
/// Decided at the first byte for the reason <see cref="ResponseStream"/> opens there: the pipeline
/// has settled the status and headers by then. A streaming handler's content type is among them,
/// because the stream filter commits it before the first item, so the response itself can say
/// whether it streams.
/// </para>
/// <para>
/// A response that writes nothing is never decided, and goes back as one payload. That keeps a 204,
/// a 304 and the answer to a HEAD request without a body, where an opened stream has to send at
/// least one byte.
/// </para>
/// </remarks>
internal sealed class MixedResponseStream : Stream
{
    private readonly ResponseStream _stream;
    private readonly Func<bool> _streams;
    private Stream? _target;

    /// <param name="stream">Where the body goes when the response streams.</param>
    /// <param name="streams">
    /// Whether the response streams. Asked once, at the first write or asynchronous flush.
    /// </param>
    public MixedResponseStream(ResponseStream stream, Func<bool> streams)
    {
        _stream = stream;
        _streams = streams;
    }

    /// <summary>
    /// The body of an answer sent as one payload. Empty when the response streamed or wrote nothing.
    /// </summary>
    public MemoryStream Collected { get; } = new();

    /// <summary>Whether the body went to the Lambda response stream.</summary>
    public bool Streamed => _stream.HasResponseStarted;

    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    /// <summary>The bytes written so far, wherever they went.</summary>
    public override long Length => _target?.Length ?? 0;

    public override long Position
    {
        get => Length;
        set => throw new NotSupportedException("Seeking in this stream is not supported.");
    }

    public override void Write(byte[] buffer, int offset, int count) =>
        Target.Write(buffer, offset, count);

    public override void Write(ReadOnlySpan<byte> buffer) => Target.Write(buffer);

    public override void WriteByte(byte value) => Target.WriteByte(value);

    public override Task WriteAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken
    ) => Target.WriteAsync(buffer, offset, count, cancellationToken);

    public override ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default
    ) => Target.WriteAsync(buffer, cancellationToken);

    public override Task FlushAsync(CancellationToken cancellationToken) =>
        Target.FlushAsync(cancellationToken);

    /// <summary>
    /// Passed on once the body has somewhere to go, and decides nothing before that, as
    /// <see cref="ResponseStream.Flush"/> opens nothing.
    /// </summary>
    public override void Flush() => _target?.Flush();

    /// <summary>
    /// Waits until every byte written to the Lambda response stream has been handed to it. A
    /// response that did not stream has nothing to wait for.
    /// </summary>
    public Task CompleteAsync() => _stream.CompleteAsync();

    public override int Read(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException("Reading from this stream is not supported.");
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotSupportedException("Seeking in this stream is not supported.");
    }

    public override void SetLength(long value)
    {
        throw new NotSupportedException("SetLength is not supported for this stream.");
    }

    private Stream Target => _target ??= _streams() ? _stream : Collected;
}
