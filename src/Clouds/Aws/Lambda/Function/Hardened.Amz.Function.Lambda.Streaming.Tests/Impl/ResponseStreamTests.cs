using System.IO.Pipelines;
using System.Text;
using Hardened.Amz.Function.Lambda.Streaming.Impl;
using Xunit;

namespace Hardened.Amz.Function.Lambda.Streaming.Tests.Impl;

/// <summary>
/// The stream a streaming function's handler writes into.
///
/// <para>
/// It is a one-way adapter onto a <see cref="PipeWriter"/> with one job beyond copying bytes:
/// telling the engine when the response has begun, so the engine can open the POST back to the
/// Lambda runtime API. Until that call happens no bytes leave the function, however many have been
/// written.
/// </para>
///
/// <para>
/// Mirrors <c>Hardened.Amz.Web.Lambda.Streaming.Tests</c>, which covers the same shape for the web
/// runtime.
/// </para>
/// </summary>
public class ResponseStreamTests {
    private readonly Pipe _pipe = new();
    private int _beginCount;

    private ResponseStream CreateStream() =>
        new(_pipe.Writer, () => _beginCount++);

    [Fact]
    public void TheStreamIsWriteOnly() {
        var stream = CreateStream();

        Assert.True(stream.CanWrite);
        Assert.False(stream.CanRead);
        Assert.False(stream.CanSeek);
    }

    [Fact]
    public void ANewStreamHasWrittenNothing() {
        var stream = CreateStream();

        Assert.Equal(0, stream.Length);
        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public async Task WritingAdvancesTheLength() {
        var stream = CreateStream();

        await stream.WriteAsync("hello"u8.ToArray().AsMemory(), TestContext.Current.CancellationToken);

        Assert.Equal(5, stream.Length);
        Assert.Equal(5, stream.Position);
    }

    [Fact]
    public async Task SuccessiveWritesAccumulate() {
        var stream = CreateStream();

        await stream.WriteAsync(new byte[10].AsMemory(), TestContext.Current.CancellationToken);
        await stream.WriteAsync(new byte[20].AsMemory(), TestContext.Current.CancellationToken);
        await stream.WriteAsync(new byte[5].AsMemory(), TestContext.Current.CancellationToken);

        Assert.Equal(35, stream.Length);
    }

    /// <summary>
    /// The first async write is what opens the response. Nothing reaches the caller before it, so a
    /// missing call here is a function that computes an answer and never sends it.
    /// </summary>
    [Fact]
    public async Task TheFirstAsyncWriteBeginsTheResponse() {
        var stream = CreateStream();

        Assert.False(stream.HasResponseStarted);

        await stream.WriteAsync("x"u8.ToArray().AsMemory(), TestContext.Current.CancellationToken);

        Assert.True(stream.HasResponseStarted);
        Assert.Equal(1, _beginCount);
    }

    /// <summary>
    /// The engine starts one POST per invocation. Beginning twice would mean two responses for one
    /// request, and the runtime API rejects the second.
    /// </summary>
    [Fact]
    public async Task TheResponseBeginsOnlyOnce() {
        var stream = CreateStream();

        await stream.WriteAsync(new byte[5].AsMemory(), TestContext.Current.CancellationToken);
        await stream.WriteAsync(new byte[5].AsMemory(), TestContext.Current.CancellationToken);
        await stream.FlushAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, _beginCount);
    }

    /// <summary>
    /// A flush with nothing written begins the response, which is how a handler sends headers ahead
    /// of a slow body.
    /// </summary>
    [Fact]
    public async Task FlushingBeginsTheResponse() {
        var stream = CreateStream();

        await stream.FlushAsync(TestContext.Current.CancellationToken);

        Assert.True(stream.HasResponseStarted);
        Assert.Equal(1, _beginCount);
    }

    [Fact]
    public void TheSynchronousFlushDoesNothing() {
        var stream = CreateStream();

        stream.Flush();

        Assert.False(stream.HasResponseStarted);
        Assert.Equal(0, _beginCount);
    }

    [Fact]
    public void SynchronousWritingAdvancesTheLength() {
        var stream = CreateStream();
        var data = "hello sync"u8.ToArray();

        stream.Write(data, 0, data.Length);

        Assert.Equal(data.Length, stream.Length);
    }

    [Fact]
    public void WritingASingleByteAdvancesTheLength() {
        var stream = CreateStream();

        stream.WriteByte(0x42);

        Assert.Equal(1, stream.Length);
    }

    /// <summary>
    /// Unlike its web counterpart, the synchronous <c>Write</c> and <c>WriteByte</c> here do not
    /// begin the response — they fill the pipe and leave it to something else to open the POST.
    ///
    /// <para>
    /// It is not a leak in practice: <c>FunctionInvokeEngine</c> flushes the stream once the
    /// middleware chain returns, and the flush begins it. The consequence is latency, not loss —
    /// a handler that only ever writes synchronously sends nothing until it has finished, which is
    /// the opposite of what a streaming response is for. Recorded here because the two copies of
    /// this class diverge on it, not because the divergence is intended.
    /// </para>
    /// </summary>
    [Fact]
    public void SynchronousWritingLeavesTheResponseToBeBegunByTheFlush() {
        var stream = CreateStream();

        stream.Write("hello"u8.ToArray(), 0, 5);
        stream.WriteByte(0x42);

        Assert.False(stream.HasResponseStarted);
        Assert.Equal(0, _beginCount);
    }

    [Fact]
    public async Task WrittenBytesAreReadableFromThePipe() {
        var stream = CreateStream();

        await stream.WriteAsync("pipe-data"u8.ToArray().AsMemory(), TestContext.Current.CancellationToken);
        await _pipe.Writer.CompleteAsync();

        var result = await _pipe.Reader.ReadAsync(TestContext.Current.CancellationToken);

        Assert.Equal("pipe-data", Encoding.UTF8.GetString(result.Buffer));

        _pipe.Reader.AdvanceTo(result.Buffer.End);
        await _pipe.Reader.CompleteAsync();
    }

    [Fact]
    public void ReadingIsNotSupported() {
        Assert.Throws<NotSupportedException>(() => CreateStream().Read(new byte[1], 0, 1));
    }

    [Fact]
    public void SeekingIsNotSupported() {
        Assert.Throws<NotSupportedException>(() => CreateStream().Seek(0, SeekOrigin.Begin));
    }

    [Fact]
    public void SettingTheLengthIsNotSupported() {
        Assert.Throws<NotSupportedException>(() => CreateStream().SetLength(100));
    }

    /// <summary>
    /// Position is how many bytes have gone out, not a cursor. Bytes already sent cannot be
    /// unsent, so moving it has no meaning.
    /// </summary>
    [Fact]
    public void SettingThePositionIsNotSupported() {
        Assert.Throws<NotSupportedException>(() => CreateStream().Position = 10);
    }
}
