using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Runtime.Filters;
using Hardened.Requests.Runtime.Tests.Support;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Filters;

/// <summary>
/// The body a streamed response is written through. Each item reaches the transport as one write,
/// from a stream the pool lends for that item and gets back once the write has finished.
/// </summary>
public class ItemBufferStreamTests
{
    private static readonly byte[] Prefix = "data: "u8.ToArray();
    private static readonly byte[] Payload = "{\"id\":1}"u8.ToArray();
    private static readonly byte[] Terminator = "\n\n"u8.ToArray();

    private static IExecutionResponse Response(Stream transport)
    {
        var response = Pipeline.Context().Response;

        response.Body = transport;

        return response;
    }

    /// <summary>An event written in its three parts, the way <c>SseFraming</c> writes it.</summary>
    private static async Task WriteItem(Stream body)
    {
        var token = TestContext.Current.CancellationToken;

        await body.WriteAsync(Prefix, 0, Prefix.Length, token);
        await body.WriteAsync(Payload.AsMemory(), token);
        await body.WriteAsync(Terminator, 0, Terminator.Length, token);
        await body.FlushAsync(token);
    }

    [Fact]
    public async Task AnItemReachesTheTransportAsOneWrite()
    {
        var transport = new RecordingTransport();

        using var body = ItemBufferStream.Install(Response(transport), new CountingStreamPool());

        await WriteItem(body);

        Assert.Equal("data: {\"id\":1}\n\n", Assert.Single(transport.Writes));
    }

    [Fact]
    public async Task TheStreamGoesBackToThePoolOnceTheItemIsWritten()
    {
        var pool = new CountingStreamPool();

        using var body = ItemBufferStream.Install(Response(new RecordingTransport()), pool);

        await WriteItem(body);

        Assert.Equal(1, pool.Lent);
        Assert.Equal(0, pool.Outstanding);
        Assert.True(pool.NoneWrittenAfterReturn);
    }

    /// <summary>
    /// Each item borrows a stream of its own, so the pool lends one per item rather than holding
    /// one for the length of the response.
    /// </summary>
    [Fact]
    public async Task EachItemBorrowsAStreamOfItsOwn()
    {
        var pool = new CountingStreamPool();
        var transport = new RecordingTransport();

        using var body = ItemBufferStream.Install(Response(transport), pool);

        await WriteItem(body);
        await WriteItem(body);
        await WriteItem(body);

        Assert.Equal(3, transport.Writes.Count);
        Assert.Equal(3, pool.Lent);
        Assert.Equal(0, pool.Outstanding);
    }

    /// <summary>
    /// A compressing transport reads the bytes across its awaits, so the stream they are in cannot
    /// go back to the pool before the transport's write has finished.
    /// </summary>
    [Fact]
    public async Task TheStreamIsHeldUntilTheTransportsWriteFinishes()
    {
        var pool = new CountingStreamPool();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var transport = new RecordingTransport { Gate = gate.Task };

        using var body = ItemBufferStream.Install(Response(transport), pool);

        var writing = WriteItem(body);

        Assert.Single(transport.Writes);
        Assert.Equal(1, pool.Outstanding);

        gate.SetResult();

        await writing;

        Assert.Equal(0, pool.Outstanding);
    }

    [Fact]
    public async Task TheStreamGoesBackWhenTheTransportFails()
    {
        var pool = new CountingStreamPool();
        var transport = new RecordingTransport { FailOnWrite = 1 };

        using var body = ItemBufferStream.Install(Response(transport), pool);

        await Assert.ThrowsAsync<IOException>(() => WriteItem(body));

        Assert.Equal(1, pool.Returned);
        Assert.Equal(0, pool.Outstanding);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AFlushWithNothingWrittenBorrowsNothingAndFlushesTheTransport(bool async)
    {
        var pool = new CountingStreamPool();
        var transport = new RecordingTransport();

        using var body = ItemBufferStream.Install(Response(transport), pool);

        if (async)
        {
            await body.FlushAsync(TestContext.Current.CancellationToken);
        }
        else
        {
            body.Flush();
        }

        Assert.Empty(transport.Writes);
        Assert.Equal(1, transport.Flushes);
        Assert.Equal(0, pool.Lent);
    }

    [Fact]
    public void ASynchronousFlushHandsTheItemOverAsOneWrite()
    {
        var pool = new CountingStreamPool();
        var transport = new RecordingTransport();

        using var body = ItemBufferStream.Install(Response(transport), pool);

        body.Write(Prefix, 0, Prefix.Length);
        body.Write(Payload.AsSpan());
        body.Write(Terminator, 0, Terminator.Length);
        body.Flush();

        Assert.Equal("data: {\"id\":1}\n\n", Assert.Single(transport.Writes));
        Assert.Equal(1, transport.Flushes);
        Assert.Equal(0, pool.Outstanding);
    }

    [Fact]
    public async Task AnItemLargerThanThePooledStreamGoesOutWhole()
    {
        var transport = new RecordingTransport();
        var large = new byte[10_000];

        Array.Fill(large, (byte)'x');

        using var body = ItemBufferStream.Install(Response(transport), new CountingStreamPool());

        await body.WriteAsync(Prefix, 0, Prefix.Length, TestContext.Current.CancellationToken);
        await body.WriteAsync(large.AsMemory(), TestContext.Current.CancellationToken);
        await body.FlushAsync(TestContext.Current.CancellationToken);

        Assert.Equal(Prefix.Length + large.Length, Assert.Single(transport.Writes).Length);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AWriteWithACancelledTokenBorrowsNothing(bool memory)
    {
        var pool = new CountingStreamPool();
        var cancelled = new CancellationToken(canceled: true);

        using var body = ItemBufferStream.Install(Response(new RecordingTransport()), pool);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            if (memory)
            {
                await body.WriteAsync(Payload.AsMemory(), cancelled);
            }
            else
            {
                await body.WriteAsync(Payload, 0, Payload.Length, cancelled);
            }
        });

        Assert.Equal(0, pool.Lent);
    }

    /// <summary>
    /// The Lambda, Azure Functions and testing hosts answer <c>ResponseStarted</c> from the body's
    /// position, so bytes written here count before they reach the transport, as they would have
    /// counted written straight to it.
    /// </summary>
    [Fact]
    public async Task HeldBytesCountTowardsThePosition()
    {
        var transport = new RecordingTransport();

        using var body = ItemBufferStream.Install(Response(transport), new CountingStreamPool());

        await body.WriteAsync(Prefix, 0, Prefix.Length, TestContext.Current.CancellationToken);
        await body.WriteAsync(Payload.AsMemory(), TestContext.Current.CancellationToken);

        Assert.Equal(0, transport.Position);
        Assert.Equal(Prefix.Length + Payload.Length, body.Position);

        await body.FlushAsync(TestContext.Current.CancellationToken);

        Assert.Equal(transport.Position, body.Position);
    }

    /// <summary>
    /// Bytes being handed over stop counting as the handover begins. A compressing transport
    /// decides at its first write whether to compress, and declines once the response reads as
    /// started. The first version of this stream, on 2026-09-25, counted the item it was handing
    /// over, and newline-delimited JSON through the compression filter went out uncompressed on
    /// every host that reads the position. <c>ResponseCompressionFilterTests</c> caught it before
    /// it merged.
    /// </summary>
    [Fact]
    public async Task BytesBeingHandedOverNoLongerCount()
    {
        long? positionDuringHandover = null;
        ItemBufferStream? body = null;
        var transport = new RecordingTransport
        {
            OnWrite = () => positionDuringHandover ??= body!.Position,
        };

        body = ItemBufferStream.Install(Response(transport), new CountingStreamPool());

        using (body)
        {
            await WriteItem(body);
        }

        Assert.Equal(0, positionDuringHandover);
    }

    [Fact]
    public void DisposingPutsTheTransportBack()
    {
        var transport = new RecordingTransport();
        var response = Response(transport);

        var body = ItemBufferStream.Install(response, new CountingStreamPool());

        Assert.Same(body, response.Body);

        body.Dispose();

        Assert.Same(transport, response.Body);
    }

    [Fact]
    public async Task DisposingReturnsAStreamStillHeld()
    {
        var pool = new CountingStreamPool();

        var body = ItemBufferStream.Install(Response(new RecordingTransport()), pool);

        await body.WriteAsync(Prefix, 0, Prefix.Length, TestContext.Current.CancellationToken);

        body.Dispose();

        Assert.Equal(1, pool.Returned);
        Assert.Equal(0, pool.Outstanding);
    }

    /// <summary>
    /// <c>ItemPool</c> does not guard against a second return, and a stream returned twice is lent
    /// to the next two callers at once. The stream clears its reference before returning it.
    /// </summary>
    [Fact]
    public async Task DisposingTwiceReturnsTheStreamOnce()
    {
        var pool = new CountingStreamPool();

        var body = ItemBufferStream.Install(Response(new RecordingTransport()), pool);

        await body.WriteAsync(Prefix, 0, Prefix.Length, TestContext.Current.CancellationToken);

        body.Dispose();
        body.Dispose();

        Assert.Equal(1, pool.Returned);
        Assert.Equal(0, pool.ReturnedTwice);
    }

    /// <summary>
    /// A caller that kept the body writes after the stream has ended. Refusing it keeps the write
    /// out of a stream the pool may have lent to another request.
    /// </summary>
    [Theory]
    [InlineData("Write")]
    [InlineData("WriteSpan")]
    [InlineData("WriteAsync")]
    [InlineData("WriteAsyncMemory")]
    [InlineData("Flush")]
    [InlineData("FlushAsync")]
    public async Task AnythingAfterDisposalIsRefused(string operation)
    {
        var pool = new CountingStreamPool();
        var token = TestContext.Current.CancellationToken;

        var body = ItemBufferStream.Install(Response(new RecordingTransport()), pool);

        body.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
        {
            switch (operation)
            {
                case "Write":
                    body.Write(Payload, 0, Payload.Length);
                    break;
                case "WriteSpan":
                    body.Write(Payload.AsSpan());
                    break;
                case "WriteAsync":
                    await body.WriteAsync(Payload, 0, Payload.Length, token);
                    break;
                case "WriteAsyncMemory":
                    await body.WriteAsync(Payload.AsMemory(), token);
                    break;
                case "Flush":
                    body.Flush();
                    break;
                default:
                    await body.FlushAsync(token);
                    break;
            }
        });

        Assert.Equal(0, pool.Lent);
    }

    [Fact]
    public void TheStreamIsWriteOnly()
    {
        using var body = ItemBufferStream.Install(
            Response(new RecordingTransport()),
            new CountingStreamPool()
        );

        Assert.False(body.CanRead);
        Assert.False(body.CanSeek);
        Assert.True(body.CanWrite);
        Assert.Throws<NotSupportedException>(() => body.Length);
        Assert.Throws<NotSupportedException>(() => body.Position = 1);
        Assert.Throws<NotSupportedException>(() => body.Read(new byte[1], 0, 1));
        Assert.Throws<NotSupportedException>(() => body.Seek(0, SeekOrigin.Begin));
        Assert.Throws<NotSupportedException>(() => body.SetLength(0));
    }
}
