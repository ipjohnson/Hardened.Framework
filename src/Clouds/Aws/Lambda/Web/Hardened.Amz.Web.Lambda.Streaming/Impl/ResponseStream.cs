using System.IO.Pipelines;
using Hardened.Requests.Abstract.Execution;

namespace Hardened.Amz.Web.Lambda.Streaming.Impl;

public class ResponseStream : Stream {
    private readonly PipeWriter _pipeWriter;
    private readonly Action<IExecutionResponse, PipeWriter> _writePrelude;
    private readonly Action _beginResponse;
    private IExecutionResponse? _executionResponse;
    private bool _preludeWritten;
    private bool _responseStarted;
    private long _writtenBytes;

    public ResponseStream(
        PipeWriter pipeWriter,
        Action<IExecutionResponse, PipeWriter> writePrelude,
        Action beginResponse) {
        _pipeWriter = pipeWriter;
        _writePrelude = writePrelude;
        _beginResponse = beginResponse;
    }

    public void SetExecutionResponse(IExecutionResponse response) {
        _executionResponse = response;
    }

    public bool HasResponseStarted => _responseStarted;

    public override async ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default) {
        EnsurePreludeWritten();
        EnsureResponseStarted();

        await _pipeWriter.WriteAsync(buffer, cancellationToken);
        await _pipeWriter.FlushAsync(cancellationToken);
        _writtenBytes += buffer.Length;
    }

    public override void Write(byte[] buffer, int offset, int count) {
        EnsurePreludeWritten();
        EnsureResponseStarted();

        var span = _pipeWriter.GetSpan(count);
        buffer.AsSpan(offset, count).CopyTo(span);
        _pipeWriter.Advance(count);

        _writtenBytes += count;
    }

    public override async Task FlushAsync(CancellationToken cancellationToken) {
        EnsurePreludeWritten();
        EnsureResponseStarted();

        await _pipeWriter.FlushAsync(cancellationToken);
    }

    public override void Flush() {
        // No-op for synchronous flush
    }

    public override void WriteByte(byte value) {
        EnsurePreludeWritten();
        EnsureResponseStarted();

        var span = _pipeWriter.GetSpan(1);
        span[0] = value;
        _pipeWriter.Advance(1);
        _writtenBytes += 1;
    }

    private void EnsurePreludeWritten() {
        if (!_preludeWritten) {
            _preludeWritten = true;

            if (_executionResponse == null) {
                throw new InvalidOperationException(
                    "ExecutionResponse must be set before writing to the stream.");
            }

            Console.Error.WriteLine($"[ResponseStream] Writing prelude - Status: {_executionResponse.Status}");
            _writePrelude(_executionResponse, _pipeWriter);
            Console.Error.WriteLine("[ResponseStream] Prelude written");
        }
    }

    private void EnsureResponseStarted() {
        if (!_responseStarted) {
            _responseStarted = true;
            Console.Error.WriteLine("[ResponseStream] Starting response (beginning SendResponse task)");
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
