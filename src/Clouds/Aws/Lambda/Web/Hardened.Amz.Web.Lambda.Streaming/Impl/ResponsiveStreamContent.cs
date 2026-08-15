using System.Net;
using Hardened.Shared.Runtime.Diagnostics;

namespace Hardened.Amz.Web.Lambda.Streaming.Impl;

public class ResponsiveStreamContent : HttpContent {
    private readonly Stream _stream;
    private readonly int _flushDelayMs;

    public ResponsiveStreamContent(Stream content, int flushDelayMs = 100) {
        _stream = content;
        _flushDelayMs = flushDelayMs;
    }

    protected override async Task SerializeToStreamAsync(
        Stream stream, TransportContext? context) {
        await SerializeToStreamAsync(stream, context, CancellationToken.None);
    }

    protected override async Task SerializeToStreamAsync(
        Stream stream, TransportContext? context, CancellationToken cancellationToken) {
        var buffer = new byte[4096];
        var timestamp = MachineTimestamp.Now;

        while (!cancellationToken.IsCancellationRequested) {
            var read = await _stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);

            if (read == 0) {
                break;
            }

            await stream.WriteAsync(buffer, 0, read, cancellationToken);

            if (timestamp.GetElapsedMilliseconds() > _flushDelayMs) {
                await stream.FlushAsync(cancellationToken);
                timestamp = MachineTimestamp.Now;
            }
        }

        // Nothing to flush once cancelled: the request is gone and the write would throw.
        if (!cancellationToken.IsCancellationRequested) {
            await stream.FlushAsync(cancellationToken);
        }
    }

    protected override bool TryComputeLength(out long length) {
        length = -1;
        return false;
    }
}
