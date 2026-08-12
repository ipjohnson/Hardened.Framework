using System.Text;
using Hardened.Shared.Runtime.Collections;
using Hardened.Shared.Runtime.Utilities;
using Xunit;

namespace Hardened.Shared.Runtime.Tests.Collections;

/// <summary>
/// The two pools the framework ships, and the stream wrapper that puts a pooled
/// <see cref="MemoryStream"/> behind an ordinary <see cref="Stream"/>.
/// </summary>
public class PooledResourceTests {

    /// <summary>
    /// A returned stream is rewound and emptied, so the next borrower does not inherit the previous
    /// response's bytes. Getting this wrong appends one response to another.
    /// </summary>
    [Fact]
    public void AReturnedMemoryStreamIsRewoundAndEmptied() {
        using var pool = new MemoryStreamPool();
        MemoryStream stream;

        using (var reservation = pool.Get()) {
            stream = reservation.Item;
            stream.Write("some content"u8);

            Assert.Equal(12, stream.Length);
            Assert.Equal(12, stream.Position);
        }

        Assert.Equal(0, stream.Length);
        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public void AMemoryStreamComesBackOutOfThePool() {
        using var pool = new MemoryStreamPool();
        MemoryStream first;

        using (var reservation = pool.Get()) {
            first = reservation.Item;
        }

        using var second = pool.Get();

        Assert.Same(first, second.Item);
    }

    /// <summary>
    /// Disposing the pool disposes the streams it holds. A pooled stream is a live buffer, and
    /// leaving it to the finaliser is what the pool exists to avoid.
    /// </summary>
    [Fact]
    public void DisposingTheMemoryStreamPoolDisposesItsStreams() {
        var pool = new MemoryStreamPool();
        MemoryStream stream;

        using (var reservation = pool.Get()) {
            stream = reservation.Item;
        }

        pool.Dispose();

        Assert.Throws<ObjectDisposedException>(() => stream.Position);
    }

    [Fact]
    public void AReturnedStringBuilderIsCleared() {
        using var pool = new StringBuilderPool();
        StringBuilder builder;

        using (var reservation = pool.Get()) {
            builder = reservation.Item;
            builder.Append("some content");
        }

        Assert.Equal(0, builder.Length);
    }

    /// <summary>
    /// The sized constructor sets the builder's initial capacity. A pool sized for the work it does
    /// is the whole reason the overload exists.
    /// </summary>
    [Fact]
    public void ASizedStringBuilderPoolBuildsBuildersOfThatCapacity() {
        using var pool = new StringBuilderPool(1024);
        using var reservation = pool.Get();

        Assert.Equal(1024, reservation.Item.Capacity);
    }

    /// <summary>The parameterless constructor is the sized one with the framework's default.</summary>
    [Fact]
    public void TheDefaultStringBuilderPoolStillProducesUsableBuilders() {
        using var pool = new StringBuilderPool();
        using var reservation = pool.Get();

        reservation.Item.Append("content");

        Assert.Equal("content", reservation.Item.ToString());
    }

    /// <summary>Both pools satisfy the interface the rest of the framework injects.</summary>
    [Fact]
    public void ThePoolsSatisfyTheInterfacesTheyAreInjectedAs() {
        using var streams = new MemoryStreamPool();
        using var builders = new StringBuilderPool();

        Assert.IsAssignableFrom<IMemoryStreamPool>(streams);
        Assert.IsAssignableFrom<IItemPool<MemoryStream>>(streams);
        Assert.IsAssignableFrom<IStringBuilderPool>(builders);
        Assert.IsAssignableFrom<IItemPool<StringBuilder>>(builders);
    }

    /// <summary>
    /// The wrapper forwards every stream operation to the pooled stream, so a caller handed one
    /// cannot tell it apart from a stream it owns.
    /// </summary>
    [Fact]
    public void TheWrapperForwardsReadsAndWritesToThePooledStream() {
        using var pool = new MemoryStreamPool();
        var reservation = pool.Get();

        using var wrapper = new MemoryStreamPoolWrapper(reservation);

        wrapper.Write("content"u8);
        wrapper.Flush();

        Assert.Equal(7, wrapper.Length);
        Assert.Equal(7, wrapper.Position);

        Assert.Equal(0, wrapper.Seek(0, SeekOrigin.Begin));

        var buffer = new byte[7];

        Assert.Equal(7, wrapper.Read(buffer, 0, 7));
        Assert.Equal("content", Encoding.UTF8.GetString(buffer));
    }

    [Fact]
    public void TheWrapperReportsThePooledStreamsCapabilities() {
        using var pool = new MemoryStreamPool();
        using var wrapper = new MemoryStreamPoolWrapper(pool.Get());

        Assert.True(wrapper.CanRead);
        Assert.True(wrapper.CanSeek);
        Assert.True(wrapper.CanWrite);
    }

    [Fact]
    public void TheWrapperForwardsPositionAndLengthChanges() {
        using var pool = new MemoryStreamPool();
        using var wrapper = new MemoryStreamPoolWrapper(pool.Get());

        wrapper.SetLength(16);
        wrapper.Position = 4;

        Assert.Equal(16, wrapper.Length);
        Assert.Equal(4, wrapper.Position);
    }

    /// <summary>
    /// Disposing the wrapper returns the stream to the pool rather than disposing it. This is what
    /// makes a pooled stream safe to hand to code that owns whatever stream it is given.
    /// </summary>
    [Fact]
    public void DisposingTheWrapperReturnsTheStreamToThePool() {
        using var pool = new MemoryStreamPool();
        var reservation = pool.Get();
        var stream = reservation.Item;

        var wrapper = new MemoryStreamPoolWrapper(reservation);

        wrapper.Write("content"u8);
        wrapper.Dispose();

        Assert.Equal(0, stream.Length);

        using var next = pool.Get();

        Assert.Same(stream, next.Item);
    }
}
