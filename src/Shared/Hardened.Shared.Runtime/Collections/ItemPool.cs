namespace Hardened.Shared.Runtime.Collections;

public interface IItemPool<T> {
    IPoolItemReservation<T> Get();
}

public interface IPoolItemReservation<T> : IDisposable {
    T Item { get; }
}

public class ItemPool<T> : IItemPool<T>, IDisposable {
    private int _disposed;
    private PoolItemReservation? _reservations;
    private readonly Func<T> _factory;
    private readonly Action<T> _cleanupAction;
    private readonly Action<T>? _disposeAction;

    public ItemPool(Func<T> factory, Action<T> cleanupAction, Action<T>? disposeAction = null) {
        _factory = factory;
        _cleanupAction = cleanupAction;
        _disposeAction = disposeAction;
    }

    public void Dispose() {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) == 0) {
            if (this._disposeAction != null) {
                // Atomically detach the entire list to prevent concurrent returns
                // from corrupting the iteration.
                var current = Interlocked.Exchange(ref _reservations, null);

                while (current != null) {
                    _disposeAction(current.Item);

                    current = current.Next;
                }
            }
        }
    }

    public IPoolItemReservation<T> Get() {
        for (var i = 0; i < 5; i++) {
            var currentReservation = _reservations;

            if (currentReservation == null) {
                break;
            }

            if (Interlocked.CompareExchange(ref _reservations, currentReservation.Next, currentReservation) ==
                currentReservation) {
                currentReservation.Next = null;
                return currentReservation;
            }
        }

        return new PoolItemReservation(this, _factory());
    }

    private class PoolItemReservation : IPoolItemReservation<T> {
        private readonly ItemPool<T> _pool;

        public PoolItemReservation(ItemPool<T> pool, T item) {
            _pool = pool;
            Item = item;
        }

        public PoolItemReservation? Next { get; set; }

        public void Dispose() {
            if (Next == null) {
                // Don't return items to a disposed pool.
                if (_pool._disposed != 0) {
                    return;
                }

                _pool._cleanupAction(Item);

                while (true) {
                    // Re-check disposal status before each CAS attempt
                    // to avoid pushing onto a pool that's being torn down.
                    if (_pool._disposed != 0) {
                        return;
                    }

                    var current = _pool._reservations;

                    Next = current;

                    if (Interlocked.CompareExchange(ref _pool._reservations, this, current) == current) {
                        return;
                    }
                }
            }
        }

        public T Item { get; }
    }
}