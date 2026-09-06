using Amazon.Lambda.Core.ResponseStreaming;
using Hardened.Amz.Shared.Lambda.Runtime.Streaming;

namespace Hardened.Amz.Web.Lambda.Runtime.Tests.Infrastructure;

/// <summary>
/// The stream-opening seam, recording the prelude each stream opened with and keeping every byte
/// written to it.
/// </summary>
internal sealed class CapturingResponseStreamFactory : IResponseStreamFactory {
    public List<HttpResponseStreamPrelude> Preludes { get; } = [];

    public int PlainStreams { get; private set; }

    public SignallingStream Target { get; } = new();

    /// <summary>The prelude of the one stream a test expects to have been opened.</summary>
    public HttpResponseStreamPrelude Prelude => Assert.Single(Preludes);

    public Stream CreateStream() {
        PlainStreams++;

        return Target;
    }

    public Stream CreateHttpStream(HttpResponseStreamPrelude prelude) {
        Preludes.Add(prelude);

        return Target;
    }
}

/// <summary>
/// A memory stream that says when it has been written to, so a test can wait for the pump rather
/// than sleep, and that can be told to fail every write.
/// </summary>
internal sealed class SignallingStream : MemoryStream {
    private readonly TaskCompletionSource _firstWrite = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completes on the first write that reached this stream.</summary>
    public Task FirstWrite => _firstWrite.Task;

    public Exception? FailWith { get; set; }

    public override void Write(ReadOnlySpan<byte> buffer) {
        Throw();
        base.Write(buffer);
        _firstWrite.TrySetResult();
    }

    public override void Write(byte[] buffer, int offset, int count) {
        Throw();
        base.Write(buffer, offset, count);
        _firstWrite.TrySetResult();
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) {
        Throw();
        var result = base.WriteAsync(buffer, cancellationToken);
        _firstWrite.TrySetResult();

        return result;
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) {
        Throw();
        var result = base.WriteAsync(buffer, offset, count, cancellationToken);
        _firstWrite.TrySetResult();

        return result;
    }

    private void Throw() {
        if (FailWith != null) {
            throw FailWith;
        }
    }
}
