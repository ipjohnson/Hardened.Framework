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
}
