using System.Text;
using Hardened.Amz.Shared.Lambda.Runtime.Streaming;
using Xunit;

namespace Hardened.Amz.Shared.Lambda.Runtime.Tests.Streaming;

/// <summary>
/// The body of a response in stream mode: a pipe the pipeline writes into, opened onto the Lambda
/// response stream at the first byte and pumped until completion.
/// </summary>
public class ResponseStreamTests {

    /// <summary>
    /// A memory stream that says when it has been written to, so a test waits for the pump rather
    /// than sleeping, and that can be told to refuse every write.
    /// </summary>
    private sealed class Target : MemoryStream {
        private readonly TaskCompletionSource _firstWrite = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task FirstWrite => _firstWrite.Task;

        public Exception? FailWith { get; init; }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) {
            if (FailWith != null) {
                throw FailWith;
            }

            var result = base.WriteAsync(buffer, cancellationToken);
            _firstWrite.TrySetResult();

            return result;
        }

        public string Text => Encoding.UTF8.GetString(ToArray());
    }

    private sealed class Opener {
        public Target Target { get; init; } = new();

        public int Opened { get; private set; }

        public Stream Open() {
            Opened++;

            return Target;
        }
    }

    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    [Fact]
    public void TheStreamIsWriteOnly() {
        var stream = new ResponseStream(new Opener().Open);

        Assert.True(stream.CanWrite);
        Assert.False(stream.CanRead);
        Assert.False(stream.CanSeek);
    }

    [Fact]
    public void ANewStreamHasWrittenNothingAndOpenedNothing() {
        var opener = new Opener();
        var stream = new ResponseStream(opener.Open);

        Assert.Equal(0, stream.Length);
        Assert.Equal(0, stream.Position);
        Assert.False(stream.HasResponseStarted);
        Assert.Equal(0, opener.Opened);
    }

    /// <summary>
    /// The Lambda stream opens at the first byte and not before. Opening earlier would commit the
    /// prelude before the pipeline had finished deciding the status and headers.
    /// </summary>
    [Fact]
    public async Task TheFirstWriteOpensTheLambdaStream() {
        var opener = new Opener();
        var stream = new ResponseStream(opener.Open);

        await stream.WriteAsync(Bytes("x"), TestContext.Current.CancellationToken);

        Assert.True(stream.HasResponseStarted);
        Assert.Equal(1, opener.Opened);
    }

    /// <summary>
    /// The bootstrap allows one stream per invocation and throws on a second, so however many
    /// writes and flushes follow, the opener runs once.
    /// </summary>
    [Fact]
    public async Task TheLambdaStreamOpensOnce() {
        var opener = new Opener();
        var stream = new ResponseStream(opener.Open);

        await stream.WriteAsync(Bytes("a"), TestContext.Current.CancellationToken);
        stream.Write(Bytes("b"), 0, 1);
        stream.WriteByte((byte)'c');
        await stream.FlushAsync(TestContext.Current.CancellationToken);
        await stream.CompleteAsync();

        Assert.Equal(1, opener.Opened);
        Assert.Equal("abc", opener.Target.Text);
    }

    /// <summary>
    /// An asynchronous write is on its way before the writer continues: the pump takes it as soon
    /// as the pipe hands it over, with no timer in between. The hand-rolled host coalesced on a
    /// 100 ms clock.
    /// </summary>
    [Fact]
    public async Task AnAsyncWriteReachesTheLambdaStreamWithoutWaitingForCompletion() {
        var opener = new Opener();
        var stream = new ResponseStream(opener.Open);

        await stream.WriteAsync(Bytes("first item"), TestContext.Current.CancellationToken);
        await opener.Target.FirstWrite.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal("first item", opener.Target.Text);
    }

    /// <summary>
    /// Synchronous writes sit in the pipe until something flushes. Completion is that something for
    /// a serializer that never does.
    /// </summary>
    [Fact]
    public async Task SynchronousWritesReachTheLambdaStreamAtCompletion() {
        var opener = new Opener();
        var stream = new ResponseStream(opener.Open);

        stream.Write(Bytes("hello"), 0, 5);
        stream.WriteByte((byte)'!');

        Assert.Equal(6, stream.Length);
        Assert.True(stream.HasResponseStarted);

        await stream.CompleteAsync();

        Assert.Equal("hello!", opener.Target.Text);
    }

    [Fact]
    public async Task AnAsynchronousFlushSendsWhatSynchronousWritesLeftInThePipe() {
        var opener = new Opener();
        var stream = new ResponseStream(opener.Open);

        stream.Write(Bytes("sync"), 0, 4);
        await stream.FlushAsync(TestContext.Current.CancellationToken);
        await opener.Target.FirstWrite.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal("sync", opener.Target.Text);
    }

    /// <summary>
    /// A flush with nothing written opens the stream: it is how a handler sends the status and
    /// headers ahead of a slow body.
    /// </summary>
    [Fact]
    public async Task AFlushWithNothingWrittenOpensTheLambdaStream() {
        var opener = new Opener();
        var stream = new ResponseStream(opener.Open);

        await stream.FlushAsync(TestContext.Current.CancellationToken);

        Assert.True(stream.HasResponseStarted);
        Assert.Equal(1, opener.Opened);
    }

    /// <summary>
    /// The synchronous flush cannot wait on the pump without blocking the thread the pump needs,
    /// so it does nothing - and in particular does not open the stream.
    /// </summary>
    [Fact]
    public void TheSynchronousFlushDoesNothing() {
        var opener = new Opener();
        var stream = new ResponseStream(opener.Open);

        stream.Flush();

        Assert.False(stream.HasResponseStarted);
        Assert.Equal(0, opener.Opened);
    }

    /// <summary>
    /// A response that never wrote has no stream to close; completing it must not open one, or a
    /// buffered function would stream an empty body on every invocation.
    /// </summary>
    [Fact]
    public async Task CompletingAStreamThatNeverStartedOpensNothing() {
        var opener = new Opener();
        var stream = new ResponseStream(opener.Open);

        await stream.CompleteAsync();

        Assert.Equal(0, opener.Opened);
        Assert.False(stream.HasResponseStarted);
    }

    /// <summary>
    /// Completion returns only when every byte is on the Lambda stream, whatever the size. The
    /// bootstrap writes the terminator the moment the handler returns, and a write still in the
    /// pipe at that point would corrupt the chunked body.
    /// </summary>
    [Fact]
    public async Task CompletionWaitsForEveryByte() {
        var opener = new Opener();
        var stream = new ResponseStream(opener.Open);
        var large = new byte[1_000_000];
        Random.Shared.NextBytes(large);

        await stream.WriteAsync(large, TestContext.Current.CancellationToken);
        stream.Write(large, 0, large.Length);
        await stream.CompleteAsync();

        Assert.Equal(2_000_000, opener.Target.Length);
        Assert.Equal(large, opener.Target.ToArray().Take(1_000_000));
        Assert.Equal(large, opener.Target.ToArray().Skip(1_000_000));
    }

    [Fact]
    public async Task SuccessiveWritesAccumulateInTheLength() {
        var stream = new ResponseStream(new Opener().Open);

        await stream.WriteAsync(new byte[10], TestContext.Current.CancellationToken);
        await stream.WriteAsync(new byte[20].AsMemory(), TestContext.Current.CancellationToken);
        stream.Write(new byte[5], 0, 5);

        Assert.Equal(35, stream.Length);
        Assert.Equal(35, stream.Position);
    }

    /// <summary>
    /// The runtime refusing a write - a connection reset, a stream already failed - is the
    /// invocation's failure, surfaced where the host waits for the bytes.
    /// </summary>
    [Fact]
    public async Task AWriteTheLambdaStreamRefusesSurfacesFromCompletion() {
        var opener = new Opener { Target = new Target { FailWith = new IOException("connection reset") } };
        var stream = new ResponseStream(opener.Open);

        await stream.WriteAsync(Bytes("x"), TestContext.Current.CancellationToken);

        var failure = await Assert.ThrowsAsync<IOException>(stream.CompleteAsync);

        Assert.Equal("connection reset", failure.Message);
    }

    /// <summary>
    /// Position is how many bytes have gone out, not a cursor. Bytes already sent cannot be unsent.
    /// </summary>
    [Fact]
    public void ThePositionCannotBeMoved() {
        var stream = new ResponseStream(new Opener().Open);

        Assert.Throws<NotSupportedException>(() => stream.Position = 10);
        Assert.Throws<NotSupportedException>(() => stream.Seek(0, SeekOrigin.Begin));
        Assert.Throws<NotSupportedException>(() => stream.SetLength(100));
    }

    [Fact]
    public void ReadingIsNotSupported() {
        Assert.Throws<NotSupportedException>(() => new ResponseStream(new Opener().Open).Read(new byte[1], 0, 1));
    }
}
