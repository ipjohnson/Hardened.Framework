using System.Text;
using Hardened.Aws.Lambda.Runtime.Streaming;
using Xunit;

namespace Hardened.Aws.Lambda.Runtime.Tests.Streaming;

/// <summary>
/// The body of a response in mixed mode: its first write or asynchronous flush sends the body to
/// the Lambda response stream or keeps it in memory, and nothing after that changes where it goes.
/// </summary>
public class MixedResponseStreamTests
{
    private sealed class Opener
    {
        public MemoryStream Target { get; } = new();

        public int Opened { get; private set; }

        public string Text => Encoding.UTF8.GetString(Target.ToArray());

        public Stream Open()
        {
            Opened++;

            return Target;
        }
    }

    /// <summary>Whether the response streams, and how many times the body asked.</summary>
    private sealed class Question(bool answer)
    {
        public bool Answer { get; set; } = answer;

        public int Asked { get; private set; }

        public bool Ask()
        {
            Asked++;

            return Answer;
        }
    }

    private static (MixedResponseStream Body, Opener Opener, Question Question) Build(bool streams)
    {
        var opener = new Opener();
        var question = new Question(streams);

        return (
            new MixedResponseStream(new ResponseStream(opener.Open), question.Ask),
            opener,
            question
        );
    }

    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    [Fact]
    public void TheBodyIsWriteOnly()
    {
        var (body, _, _) = Build(streams: true);

        Assert.True(body.CanWrite);
        Assert.False(body.CanRead);
        Assert.False(body.CanSeek);
    }

    [Fact]
    public void ANewBodyHasDecidedNothing()
    {
        var (body, opener, question) = Build(streams: true);

        Assert.Equal(0, question.Asked);
        Assert.Equal(0, opener.Opened);
        Assert.False(body.Streamed);
        Assert.Equal(0, body.Length);
        Assert.Equal(0, body.Collected.Length);
    }

    [Fact]
    public async Task AStreamOpensTheLambdaStreamAtItsFirstWrite()
    {
        var (body, opener, _) = Build(streams: true);

        await body.WriteAsync(Bytes("data: 1\n\n"), TestContext.Current.CancellationToken);
        await body.CompleteAsync();

        Assert.True(body.Streamed);
        Assert.Equal(1, opener.Opened);
        Assert.Equal("data: 1\n\n", opener.Text);
        Assert.Equal(0, body.Collected.Length);
    }

    [Fact]
    public async Task AnyOtherAnswerIsCollectedAndOpensNothing()
    {
        var (body, opener, _) = Build(streams: false);

        await body.WriteAsync(Bytes("{\"id\":1"), TestContext.Current.CancellationToken);
        body.Write(Bytes("}"), 0, 1);
        body.WriteByte((byte)'\n');
        await body.FlushAsync(TestContext.Current.CancellationToken);
        await body.CompleteAsync();

        Assert.False(body.Streamed);
        Assert.Equal(0, opener.Opened);
        Assert.Equal("{\"id\":1}\n", Encoding.UTF8.GetString(body.Collected.ToArray()));
    }

    /// <summary>
    /// Asked at the first write rather than when the body is built, because the content type the
    /// answer depends on is set after the response exists. Asked once, because the bytes already
    /// written cannot follow a different answer.
    /// </summary>
    [Fact]
    public async Task TheQuestionIsAskedOnceAtTheFirstWrite()
    {
        var (body, opener, question) = Build(streams: false);

        question.Answer = true;

        await body.WriteAsync(Bytes("data: 1\n\n"), TestContext.Current.CancellationToken);

        question.Answer = false;

        await body.WriteAsync(Bytes("data: 2\n\n"), TestContext.Current.CancellationToken);
        await body.CompleteAsync();

        Assert.Equal(1, question.Asked);
        Assert.True(body.Streamed);
        Assert.Equal("data: 1\n\ndata: 2\n\n", opener.Text);
    }

    /// <summary>
    /// A flush with nothing written decides as a write does, so a stream can send its headers ahead
    /// of its first item.
    /// </summary>
    [Fact]
    public async Task AnAsynchronousFlushDecides()
    {
        var (body, opener, question) = Build(streams: true);

        await body.FlushAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, question.Asked);
        Assert.True(body.Streamed);
        Assert.Equal(1, opener.Opened);
    }

    /// <summary>
    /// The synchronous flush opens nothing on a <see cref="ResponseStream"/>, so it decides nothing
    /// here either.
    /// </summary>
    [Fact]
    public void TheSynchronousFlushDecidesNothing()
    {
        var (body, opener, question) = Build(streams: true);

        body.Flush();

        Assert.Equal(0, question.Asked);
        Assert.Equal(0, opener.Opened);
        Assert.False(body.Streamed);
    }

    /// <summary>
    /// A response that wrote nothing has no stream to finish, and completing it must not open one.
    /// </summary>
    [Fact]
    public async Task CompletingABodyThatWroteNothingOpensNothing()
    {
        var (body, opener, question) = Build(streams: true);

        await body.CompleteAsync();

        Assert.Equal(0, question.Asked);
        Assert.Equal(0, opener.Opened);
    }

    /// <summary>
    /// The count the hosts read <c>ResponseStarted</c> from, on either path.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TheLengthAndPositionCountWhatWasWritten(bool streams)
    {
        var (body, _, _) = Build(streams);

        await body.WriteAsync(new byte[10], TestContext.Current.CancellationToken);
        body.Write(new byte[5], 0, 5);

        Assert.Equal(15, body.Length);
        Assert.Equal(15, body.Position);

        await body.CompleteAsync();
    }

    [Fact]
    public void ThePositionCannotBeMoved()
    {
        var (body, _, _) = Build(streams: false);

        Assert.Throws<NotSupportedException>(() => body.Position = 10);
        Assert.Throws<NotSupportedException>(() => body.Seek(0, SeekOrigin.Begin));
        Assert.Throws<NotSupportedException>(() => body.SetLength(100));
    }

    [Fact]
    public void ReadingIsNotSupported()
    {
        var (body, _, _) = Build(streams: false);

        Assert.Throws<NotSupportedException>(() => body.Read(new byte[1], 0, 1));
    }
}
