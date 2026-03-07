using System.IO.Pipelines;
using Hardened.Requests.Abstract.Execution;

namespace Hardened.Amz.Function.Lambda.Streaming.Impl;

public class ResponseStream : Stream {
    private readonly PipeWriter _pipeWriter;
    private readonly Action _beginResponse;
    private bool _responseStarted;
    private long _writtenBytes;

    public ResponseStream(
        PipeWriter pipeWriter,
        Action beginResponse) {
        _pipeWriter = pipeWriter;
        _beginResponse = beginResponse;
    }

    public bool HasResponseStarted => _responseStarted;

    public override async ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default) {
        EnsureResponseStarted();

        await _pipeWriter.WriteAsync(buffer, cancellationToken);
        await _pipeWriter.FlushAsync(cancellationToken);
        _writtenBytes += buffer.Length;
    }

    public override void Write(byte[] buffer, int offset, int count) {
        var span = _pipeWriter.GetSpan(count);
        buffer.AsSpan(offset, count).CopyTo(span);
        _pipeWriter.Advance(count);

        _writtenBytes += count;
    }

    public override async Task FlushAsync(CancellationToken cancellationToken) {
        EnsureResponseStarted();

        await _pipeWriter.FlushAsync(cancellationToken);
    }

    public override void Flush() {
        // No-op for synchronous flush
    }

    public override void WriteByte(byte value) {
        var span = _pipeWriter.GetSpan(1);
        span[0] = value;
        _pipeWriter.Advance(1);
        _writtenBytes += 1;
    }

    private void EnsureResponseStarted() {
        if (!_responseStarted) {
            _responseStarted = true;
            _beginResponse();
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

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => _writtenBytes;

    public override long Position {
        get => _writtenBytes;
        set => throw new NotSupportedException("Seeking in this stream is not supported.");
    }
}
