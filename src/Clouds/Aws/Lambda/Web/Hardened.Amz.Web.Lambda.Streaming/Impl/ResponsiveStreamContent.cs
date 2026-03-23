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
        var totalRead = 0;
        var readCount = 0;

        Console.Error.WriteLine("[StreamDiag] ResponsiveStreamContent: SerializeToStreamAsync starting");

        while (!cancellationToken.IsCancellationRequested) {
            var read = await _stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
            readCount++;

            if (read > 0) {
                totalRead += read;
                await stream.WriteAsync(buffer, 0, read, cancellationToken);

                if (timestamp.GetElapsedMilliseconds() > _flushDelayMs) {
                    await stream.FlushAsync(cancellationToken);
                    timestamp = MachineTimestamp.Now;
                }
            }
            else {
                Console.Error.WriteLine($"[StreamDiag] ResponsiveStreamContent: EOF after {readCount} reads, {totalRead} total bytes");
                break;
            }
        }

        if (!cancellationToken.IsCancellationRequested) {
            Console.Error.WriteLine($"[StreamDiag] ResponsiveStreamContent: final flush, {totalRead} bytes total");
            await stream.FlushAsync(cancellationToken);
        }
        else {
            Console.Error.WriteLine($"[StreamDiag] ResponsiveStreamContent: cancelled, {totalRead} bytes sent before cancel");
        }
    }

    protected override bool TryComputeLength(out long length) {
        length = -1;
        return false;
    }
}
