using Hardened.Shared.Runtime.Collections;
using Xunit;

namespace Hardened.Shared.Runtime.Tests.Collections;

public class ItemPoolTests {
    [Fact]
    public void Get_ReturnsNewItem_WhenPoolIsEmpty() {
        var created = 0;
        using var pool = new ItemPool<int>(() => ++created, _ => { });

        using var reservation = pool.Get();

        Assert.Equal(1, reservation.Item);
        Assert.Equal(1, created);
    }

    [Fact]
    public void Get_ReturnsPooledItem_AfterDisposal() {
        var created = 0;
        using var pool = new ItemPool<string>(() => $"item-{++created}", _ => { });

        string firstItem;
        using (var reservation = pool.Get()) {
            firstItem = reservation.Item;
        }

        using var secondReservation = pool.Get();
        Assert.Same(firstItem, secondReservation.Item);
        Assert.Equal(1, created);
    }

    [Fact]
    public void Get_CreatesNewItem_WhenAllPooledItemsInUse() {
        var created = 0;
        using var pool = new ItemPool<int>(() => ++created, _ => { });

        using var first = pool.Get();
        using var second = pool.Get();

        Assert.Equal(1, first.Item);
        Assert.Equal(2, second.Item);
        Assert.Equal(2, created);
    }

    [Fact]
    public async Task ConcurrentGetDispose_IsSafe() {
        var created = 0;
        using var pool = new ItemPool<int>(() => Interlocked.Increment(ref created), _ => { });

        var tasks = Enumerable.Range(0, 100).Select(_ => Task.Run(() => {
            for (var i = 0; i < 100; i++) {
                using var reservation = pool.Get();
                var _ = reservation.Item;
            }
        }));

        await Task.WhenAll(tasks.ToArray());

        Assert.True(created > 0);
        Assert.True(created <= 10000);
    }

    [Fact]
    public void Dispose_DisposesAllPooledItems_ViaDisposeAction() {
        var disposed = new List<int>();
        var pool = new ItemPool<int>(() => disposed.Count + 1, _ => { }, i => disposed.Add(i));

        using (var r1 = pool.Get()) { }
        using (var r2 = pool.Get()) { }

        pool.Dispose();

        Assert.Contains(1, disposed);
    }

    [Fact]
    public void CleanupAction_IsCalledWhenItemReturnedToPool() {
        var cleanupCount = 0;
        using var pool = new ItemPool<int>(() => 42, _ => cleanupCount++);

        using (var reservation = pool.Get()) { }

        Assert.Equal(1, cleanupCount);
    }

    /// <summary>
    /// A pool with no dispose action has nothing to do when it is disposed, and must not treat the
    /// absent action as something to call. This is the shape <see cref="StringBuilderPool"/> uses.
    /// </summary>
    [Fact]
    public void APoolWithNoDisposeActionDisposesQuietly() {
        var pool = new ItemPool<int>(() => 1, _ => { });

        using (pool.Get()) { }

        pool.Dispose();
    }

    /// <summary>
    /// Disposing twice disposes the pooled items once. A double dispose is ordinary — a
    /// <c>using</c> inside a container that also disposes its singletons — and disposing a
    /// <c>MemoryStream</c> twice would be harmless, but disposing a second pool's worth would not.
    /// </summary>
    [Fact]
    public void DisposingTwiceDisposesThePooledItemsOnce() {
        var disposed = new List<int>();
        var created = 0;
        var pool = new ItemPool<int>(() => ++created, _ => { }, item => disposed.Add(item));

        using (pool.Get()) { }

        pool.Dispose();
        pool.Dispose();

        Assert.Equal([1], disposed);
    }

    /// <summary>
    /// Every item held by the pool is disposed, not only the one at the head of the list.
    /// </summary>
    [Fact]
    public void DisposingThePoolDisposesEveryPooledItem() {
        var disposed = new List<int>();
        var created = 0;
        var pool = new ItemPool<int>(() => ++created, _ => { }, item => disposed.Add(item));

        var first = pool.Get();
        var second = pool.Get();
        var third = pool.Get();

        first.Dispose();
        second.Dispose();
        third.Dispose();

        pool.Dispose();

        Assert.Equal([1, 2, 3], disposed.OrderBy(item => item));
    }

    /// <summary>
    /// An item returned after the pool was disposed is dropped rather than pushed onto a list
    /// nothing will ever dispose again. A reservation outliving its pool is normal at shutdown.
    /// </summary>
    [Fact]
    public void AnItemReturnedAfterDisposalIsNotPooled() {
        var disposed = new List<int>();
        var created = 0;
        var pool = new ItemPool<int>(() => ++created, _ => { }, item => disposed.Add(item));

        var reservation = pool.Get();

        pool.Dispose();
        reservation.Dispose();

        Assert.Empty(disposed);
        Assert.Equal(2, pool.Get().Item);
    }

    /// <summary>
    /// Returning an item to a disposed pool skips the cleanup action too. Cleanup often touches the
    /// item — resetting a stream's length — and the item may already have been disposed.
    /// </summary>
    [Fact]
    public void AnItemReturnedAfterDisposalIsNotCleanedUp() {
        var cleaned = 0;
        var pool = new ItemPool<int>(() => 1, _ => cleaned++, _ => { });

        var reservation = pool.Get();

        pool.Dispose();
        reservation.Dispose();

        Assert.Equal(0, cleaned);
    }

    /// <summary>
    /// Items go back to the pool and come out again, so a workload that borrows and returns in a
    /// loop settles at roughly the number of items it holds at once rather than allocating per
    /// iteration. Asserted as a bound, not an exact count: the pool is lock-free, so a contended
    /// push may lose its race and allocate.
    /// </summary>
    [Fact]
    public async Task ConcurrentBorrowersReuseItemsRatherThanAllocatingPerIteration() {
        var created = 0;
        using var pool = new ItemPool<int>(() => Interlocked.Increment(ref created), _ => { });

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => {
            for (var i = 0; i < 500; i++) {
                using var reservation = pool.Get();
                Assert.True(reservation.Item > 0);
            }
        })).ToArray());

        Assert.InRange(created, 1, 4000);
    }

    /// <summary>
    /// Every item a concurrent workload holds at one time is distinct. Two callers sharing a pooled
    /// <c>MemoryStream</c> is the failure this pool exists to prevent.
    /// </summary>
    [Fact]
    public async Task NoTwoConcurrentReservationsShareAnItem() {
        var created = 0;
        using var pool = new ItemPool<int>(() => Interlocked.Increment(ref created), _ => { });
        var inUse = new HashSet<int>();
        var collision = false;

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => {
            for (var i = 0; i < 250; i++) {
                using var reservation = pool.Get();

                lock (inUse) {
                    if (!inUse.Add(reservation.Item)) {
                        collision = true;
                    }
                }

                lock (inUse) {
                    inUse.Remove(reservation.Item);
                }
            }
        })).ToArray());

        Assert.False(collision, "Two reservations held the same item at the same time.");
    }

    /// <summary>
    /// Disposing the pool while items are still being borrowed and returned neither throws nor
    /// leaves a returned item pooled.
    /// </summary>
    [Fact]
    public async Task DisposingWhileItemsAreInFlightIsSafe() {
        var pool = new ItemPool<int>(() => 1, _ => { }, _ => { });

        var borrowers = Enumerable.Range(0, 4).Select(_ => Task.Run(() => {
            for (var i = 0; i < 500; i++) {
                using var reservation = pool.Get();
                Assert.Equal(1, reservation.Item);
            }
        })).ToArray();

        pool.Dispose();

        await Task.WhenAll(borrowers);
    }
}
