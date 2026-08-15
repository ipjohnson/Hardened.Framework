using Hardened.Amz.Web.Lambda.Streaming.Impl;
using Xunit;

namespace Hardened.Amz.Web.Lambda.Streaming.Tests.Impl;

public class ResponsiveStreamContentTests {
    [Fact]
    public void TryComputeLength_ReturnsFalse() {
        var source = new MemoryStream(new byte[10]);
        var content = new ResponsiveStreamContent(source);

        // TryComputeLength is protected, test via Headers.ContentLength
        Assert.Null(content.Headers.ContentLength);
    }

    [Fact]
    public async Task SerializeToStreamAsync_CopiesAllData() {
        var sourceData = new byte[100];
        Random.Shared.NextBytes(sourceData);
        var source = new MemoryStream(sourceData);
        var destination = new MemoryStream();

        var content = new ResponsiveStreamContent(source);
        await content.CopyToAsync(destination, TestContext.Current.CancellationToken);

        Assert.Equal(sourceData, destination.ToArray());
    }

    [Fact]
    public async Task SerializeToStreamAsync_HandlesEmptyStream() {
        var source = new MemoryStream();
        var destination = new MemoryStream();

        var content = new ResponsiveStreamContent(source);
        await content.CopyToAsync(destination, TestContext.Current.CancellationToken);

        Assert.Empty(destination.ToArray());
    }

    [Fact]
    public async Task SerializeToStreamAsync_HandlesLargeData() {
        var sourceData = new byte[50_000]; // Larger than 4096 buffer
        Random.Shared.NextBytes(sourceData);
        var source = new MemoryStream(sourceData);
        var destination = new MemoryStream();

        var content = new ResponsiveStreamContent(source);
        await content.CopyToAsync(destination, TestContext.Current.CancellationToken);

        Assert.Equal(sourceData, destination.ToArray());
    }

    [Fact]
    public async Task SerializeToStreamAsync_ExitsCleanly_WhenAlreadyCancelled() {
        var sourceData = new byte[100];
        Random.Shared.NextBytes(sourceData);
        var source = new MemoryStream(sourceData);
        var destination = new MemoryStream();
        var cts = new CancellationTokenSource();

        var content = new ResponsiveStreamContent(source);

        // Cancel before CopyToAsync — the while loop should exit immediately
        cts.Cancel();

        await content.CopyToAsync(destination, cts.Token);

        // No data should have been copied since the token was already cancelled
        Assert.Equal(0, destination.Length);
    }

    [Fact]
    public async Task SerializeToStreamAsync_StopsCopying_WhenCancelled() {
        var cts = new CancellationTokenSource();
        var source = new CancellingStream(cts);
        var destination = new MemoryStream();

        var content = new ResponsiveStreamContent(source);

        // The stream returns data on the first read, then on the second read
        // cancels the token and returns 0. The while loop exits.
        await content.CopyToAsync(destination, cts.Token);

        // Only the first chunk should have been written
        Assert.Equal(10, destination.Length);
    }

    /// <summary>
    /// A stream that returns data on the first read, then cancels the token
    /// and returns 0 on the second read (simulating cancellation between iterations).
    /// </summary>
    private class CancellingStream : Stream {
        private readonly CancellationTokenSource _cts;
        private int _readCount;

        public CancellingStream(CancellationTokenSource cts) {
            _cts = cts;
        }

        public override Task<int> ReadAsync(
            byte[] buffer, int offset, int count, CancellationToken cancellationToken) {
            _readCount++;
            if (_readCount == 1) {
                var bytesToWrite = Math.Min(count, 10);
                Array.Fill(buffer, (byte)0x42, offset, bytesToWrite);
                return Task.FromResult(bytesToWrite);
            }

            // Second read: cancel and return EOF
            _cts.Cancel();
            return Task.FromResult(0);
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();
        public override void SetLength(long value) =>
            throw new NotSupportedException();
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }
    }
}
