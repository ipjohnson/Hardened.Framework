using System.IO.Pipelines;
using System.Text;
using Hardened.Amz.Web.Lambda.Streaming.Impl;
using Hardened.Requests.Abstract.Execution;
using NSubstitute;
using Xunit;

namespace Hardened.Amz.Web.Lambda.Streaming.Tests.Impl;

public class ResponseStreamTests {
    private readonly Pipe _pipe = new();
    private readonly IExecutionResponse _response;
    private bool _preludeWritten;
    private bool _responseStarted;

    public ResponseStreamTests() {
        _response = Substitute.For<IExecutionResponse>();
    }

    private ResponseStream CreateStream() {
        var stream = new ResponseStream(
            _pipe.Writer,
            (response, writer) => { _preludeWritten = true; },
            () => { _responseStarted = true; });
        stream.SetExecutionResponse(_response);
        return stream;
    }

    [Fact]
    public void StreamCapabilities_AreCorrect() {
        var stream = CreateStream();

        Assert.True(stream.CanWrite);
        Assert.False(stream.CanRead);
        Assert.False(stream.CanSeek);
    }

    [Fact]
    public void Length_IsZero_BeforeAnyWrites() {
        var stream = CreateStream();

        Assert.Equal(0, stream.Length);
        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public async Task WriteAsync_WritesDataToPipe() {
        var stream = CreateStream();
        var data = Encoding.UTF8.GetBytes("hello");

        await stream.WriteAsync(data.AsMemory());

        Assert.Equal(5, stream.Length);
        Assert.Equal(5, stream.Position);
    }

    [Fact]
    public async Task WriteAsync_TriggersPreludeAndResponse() {
        var stream = CreateStream();
        var data = Encoding.UTF8.GetBytes("test");

        Assert.False(_preludeWritten);
        Assert.False(_responseStarted);
        Assert.False(stream.HasResponseStarted);

        await stream.WriteAsync(data.AsMemory());

        Assert.True(_preludeWritten);
        Assert.True(_responseStarted);
        Assert.True(stream.HasResponseStarted);
    }

    [Fact]
    public void Write_Sync_WritesDataToPipe() {
        var stream = CreateStream();
        var data = Encoding.UTF8.GetBytes("hello sync");

        stream.Write(data, 0, data.Length);

        Assert.Equal(data.Length, stream.Length);
        Assert.True(_preludeWritten);
        Assert.True(_responseStarted);
    }

    [Fact]
    public void WriteByte_WritesSingleByte() {
        var stream = CreateStream();

        stream.WriteByte(0x42);

        Assert.Equal(1, stream.Length);
        Assert.True(_preludeWritten);
    }

    [Fact]
    public async Task FlushAsync_TriggersPreludeAndResponse() {
        var stream = CreateStream();

        Assert.False(_preludeWritten);
        Assert.False(_responseStarted);

        await stream.FlushAsync();

        Assert.True(_preludeWritten);
        Assert.True(_responseStarted);
    }

    [Fact]
    public void Flush_Sync_IsNoOp() {
        var stream = CreateStream();

        stream.Flush();

        Assert.False(_preludeWritten);
        Assert.False(_responseStarted);
    }

    [Fact]
    public async Task WriteAsync_MultipleWrites_AccumulatesLength() {
        var stream = CreateStream();

        await stream.WriteAsync(new byte[10].AsMemory());
        await stream.WriteAsync(new byte[20].AsMemory());
        await stream.WriteAsync(new byte[5].AsMemory());

        Assert.Equal(35, stream.Length);
        Assert.Equal(35, stream.Position);
    }

    [Fact]
    public async Task PreludeWritten_OnlyOnce() {
        var preludeCount = 0;
        var stream = new ResponseStream(
            _pipe.Writer,
            (response, writer) => { preludeCount++; },
            () => { });
        stream.SetExecutionResponse(_response);

        await stream.WriteAsync(new byte[5].AsMemory());
        await stream.WriteAsync(new byte[5].AsMemory());
        await stream.FlushAsync();

        Assert.Equal(1, preludeCount);
    }

    [Fact]
    public async Task ResponseStarted_OnlyOnce() {
        var startCount = 0;
        var stream = new ResponseStream(
            _pipe.Writer,
            (response, writer) => { },
            () => { startCount++; });
        stream.SetExecutionResponse(_response);

        await stream.WriteAsync(new byte[5].AsMemory());
        await stream.WriteAsync(new byte[5].AsMemory());
        await stream.FlushAsync();

        Assert.Equal(1, startCount);
    }

    [Fact]
    public void Write_ThrowsInvalidOperation_WhenResponseNotSet() {
        var stream = new ResponseStream(
            _pipe.Writer,
            (response, writer) => { },
            () => { });

        // No SetExecutionResponse call

        Assert.Throws<InvalidOperationException>(() =>
            stream.Write(new byte[1], 0, 1));
    }

    [Fact]
    public void Read_ThrowsNotSupported() {
        var stream = CreateStream();

        Assert.Throws<NotSupportedException>(() =>
            stream.Read(new byte[1], 0, 1));
    }

    [Fact]
    public void Seek_ThrowsNotSupported() {
        var stream = CreateStream();

        Assert.Throws<NotSupportedException>(() =>
            stream.Seek(0, SeekOrigin.Begin));
    }

    [Fact]
    public void SetLength_ThrowsNotSupported() {
        var stream = CreateStream();

        Assert.Throws<NotSupportedException>(() =>
            stream.SetLength(100));
    }

    [Fact]
    public void SetPosition_ThrowsNotSupported() {
        var stream = CreateStream();

        Assert.Throws<NotSupportedException>(() =>
            stream.Position = 10);
    }

    [Fact]
    public async Task WriteAsync_DataReadableFromPipeReader() {
        var stream = CreateStream();
        var data = Encoding.UTF8.GetBytes("pipe-data");

        await stream.WriteAsync(data.AsMemory());
        await _pipe.Writer.CompleteAsync();

        var result = await _pipe.Reader.ReadAsync();
        var output = Encoding.UTF8.GetString(result.Buffer);

        // Output should contain "pipe-data" (possibly with prelude data before it)
        Assert.Contains("pipe-data", output);

        _pipe.Reader.AdvanceTo(result.Buffer.End);
        await _pipe.Reader.CompleteAsync();
    }
}
