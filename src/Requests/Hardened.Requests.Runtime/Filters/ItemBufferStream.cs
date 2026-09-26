using Hardened.Requests.Abstract.Execution;
using Hardened.Shared.Runtime.Collections;

namespace Hardened.Requests.Runtime.Filters;

/// <summary>
/// The body a streamed response is written through, so that each item reaches the transport as one
/// write.
/// </summary>
/// <remarks>
/// <para>
/// <b>One write per item, because the transport frames every write.</b> Kestrel sends each write to
/// a chunked response as a chunk of its own and flushes its output for it, and a framing writes an
/// item in parts: an event is <c>data: </c>, the payload and a blank line. Written straight through,
/// an 89-event response left as 267 chunks. Collected here and handed over by the flush that ends
/// the item, it leaves as 89.
/// </para>
/// <para>
/// <b>A pooled stream is reserved for one item, not for the response.</b> It is taken at the item's
/// first write and returned by the flush that hands the item over, so nothing is held while the
/// filter waits for the handler, and an event stream that is quiet for hours holds no buffer. It is
/// returned only once the transport's write has finished, because a compressing transport reads the
/// bytes across its awaits.
/// </para>
/// <para>
/// <b>Disposal puts the transport back and returns whatever is still held, once.</b> The reference
/// is cleared before the reservation is disposed. <see cref="ItemPool{T}"/> does not guard against
/// a second return, and a stream returned twice is handed to the next two callers at once. After
/// disposal every write throws, so a caller that kept this body cannot write into a stream the pool
/// has lent to another request.
/// </para>
/// <para>
/// <see cref="Position"/> is what the transport has taken plus what is held here, because the
/// Lambda, Azure Functions and testing hosts answer <c>ResponseStarted</c> from the body's
/// position. The held bytes stop counting as the handover begins: a compressing transport decides
/// at its first write whether to compress, and declines once the response reads as started.
/// </para>
/// </remarks>
internal sealed class ItemBufferStream : Stream
{
    private readonly IExecutionResponse _response;
    private readonly Stream _transport;
    private readonly IMemoryStreamPool _pool;
    private IPoolItemReservation<MemoryStream>? _reservation;
    private long _pending;
    private bool _disposed;

    private ItemBufferStream(IExecutionResponse response, IMemoryStreamPool pool)
    {
        _response = response;
        _transport = response.Body;
        _pool = pool;
    }

    /// <summary>
    /// Puts a buffer in place of the response's body. Disposing the buffer puts the body back.
    /// </summary>
    public static ItemBufferStream Install(IExecutionResponse response, IMemoryStreamPool pool)
    {
        var body = new ItemBufferStream(response, pool);

        response.Body = body;

        return body;
    }

    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => _transport.Position + _pending;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count)
    {
        Held().Write(buffer, offset, count);

        _pending += count;
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        Held().Write(buffer);

        _pending += buffer.Length;
    }

    public override Task WriteAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken
    )
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled(cancellationToken);
        }

        Write(buffer, offset, count);

        return Task.CompletedTask;
    }

    public override ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default
    )
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return ValueTask.FromCanceled(cancellationToken);
        }

        Write(buffer.Span);

        return default;
    }

    public override void Flush()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_reservation is { } reservation)
        {
            _pending = 0;

            try
            {
                var held = reservation.Item;

                _transport.Write(held.GetBuffer(), 0, (int)held.Length);
            }
            finally
            {
                Release();
            }
        }

        _transport.Flush();
    }

    public override async Task FlushAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_reservation is { } reservation)
        {
            _pending = 0;

            try
            {
                var held = reservation.Item;

                await _transport.WriteAsync(
                    held.GetBuffer().AsMemory(0, (int)held.Length),
                    cancellationToken
                );
            }
            finally
            {
                Release();
            }
        }

        await _transport.FlushAsync(cancellationToken);
    }

    protected override void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            _disposed = true;
            _response.Body = _transport;
        }

        Release();

        base.Dispose(disposing);
    }

    private MemoryStream Held()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return (_reservation ??= _pool.Get()).Item;
    }

    private void Release()
    {
        var reservation = _reservation;

        _reservation = null;
        _pending = 0;

        reservation?.Dispose();
    }
}
