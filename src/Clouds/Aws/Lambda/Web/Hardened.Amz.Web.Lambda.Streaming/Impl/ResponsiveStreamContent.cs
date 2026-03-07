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
        Console.Error.WriteLine("[ResponsiveStreamContent] SerializeToStreamAsync started");

        var buffer = new byte[4096];
        var timestamp = MachineTimestamp.Now;
        long totalBytesWritten = 0;
        int iterations = 0;

        while (!cancellationToken.IsCancellationRequested) {
            var read = await _stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
            iterations++;

            if (read > 0) {
                await stream.WriteAsync(buffer, 0, read, cancellationToken);
                totalBytesWritten += read;

                if (timestamp.GetElapsedMilliseconds() > _flushDelayMs) {
                    await stream.FlushAsync(cancellationToken);
                    timestamp = MachineTimestamp.Now;
                    Console.Error.WriteLine($"[ResponsiveStreamContent] Periodic flush at {totalBytesWritten} bytes");
                }
            }
            else {
                Console.Error.WriteLine($"[ResponsiveStreamContent] Read returned 0, breaking. Total: {totalBytesWritten} bytes in {iterations} iterations");
                break;
            }
        }

        if (!cancellationToken.IsCancellationRequested) {
            Console.Error.WriteLine($"[ResponsiveStreamContent] Final flush - {totalBytesWritten} bytes total");
            await stream.FlushAsync(cancellationToken);
            Console.Error.WriteLine("[ResponsiveStreamContent] Final flush completed");
        }
        else {
            Console.Error.WriteLine("[ResponsiveStreamContent] Cancelled, skipping final flush");
        }
    }

    protected override bool TryComputeLength(out long length) {
        length = -1;
        return false;
    }
}
