using System.Net;
using Hardened.Shared.Runtime.Diagnostics;

namespace Hardened.Amz.Function.Lambda.Streaming.Impl;

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
        long totalBytesWritten = 0;

        while (!cancellationToken.IsCancellationRequested) {
            var read = await _stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);

            if (read > 0) {
                await stream.WriteAsync(buffer, 0, read, cancellationToken);
                totalBytesWritten += read;

                if (timestamp.GetElapsedMilliseconds() > _flushDelayMs) {
                    await stream.FlushAsync(cancellationToken);
                    timestamp = MachineTimestamp.Now;
                }
            }
            else {
                break;
            }
        }

        if (!cancellationToken.IsCancellationRequested) {
            await stream.FlushAsync(cancellationToken);
        }
    }

    protected override bool TryComputeLength(out long length) {
        length = -1;
        return false;
    }
}
