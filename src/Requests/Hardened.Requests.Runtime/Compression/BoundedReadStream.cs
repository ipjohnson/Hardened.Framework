namespace Hardened.Requests.Runtime.Compression;

/// <summary>
/// A read-only view over a decoder that refuses to hand out more than a fixed number of bytes.
/// </summary>
/// <remarks>
/// The check is on bytes read rather than on the encoded length, because the encoded length is the
/// one number a hostile client controls: a gzip member of a few hundred bytes decodes to gigabytes.
/// The limit is inclusive, so a body of exactly the cap is read in full.
/// </remarks>
internal sealed class BoundedReadStream : Stream {
    private readonly Stream _inner;
    private readonly long _limit;
    private long _read;

    public BoundedReadStream(Stream inner, long limit) {
        _inner = inner;
        _limit = limit;
    }

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position {
        get => _read;
        set => throw new NotSupportedException();
    }

    public override void Flush() { }

    public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public override int Read(byte[] buffer, int offset, int count) =>
        Count(_inner.Read(buffer, offset, count));

    /// <summary>
    /// The array overload as well as the memory one, because the base class runs the array
    /// overload on the thread pool over the synchronous <c>Read</c> rather than forwarding it.
    /// <c>ReadByte</c> and the span overload go through <c>Read</c> already and are left to it.
    /// </summary>
    public override async Task<int> ReadAsync(
        byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        Count(await _inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken));

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        Count(await _inner.ReadAsync(buffer, cancellationToken));

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    private int Count(int read) {
        _read += read;

        if (_read > _limit) {
            throw new DecompressedBodyTooLargeException(_limit);
        }

        return read;
    }
}
