using Hardened.Shared.Runtime.Collections;

namespace Hardened.Requests.Runtime.Tests.Support;

/// <summary>
/// A stream pool that counts what it lends and what comes back.
/// </summary>
/// <remarks>
/// Every loan is a new stream, so a returned stream is never lent again and a write made into it
/// after its return cannot hide behind the next borrower. A returned stream is filled with a marker
/// and left empty, and <see cref="NoneWrittenAfterReturn"/> checks that it stayed that way.
/// </remarks>
public sealed class CountingStreamPool : IMemoryStreamPool
{
    private const byte Marker = 0xEE;

    private readonly object _lock = new();
    private readonly HashSet<Reservation> _outstanding = [];
    private readonly List<MemoryStream> _returned = [];
    private int _lent;
    private int _returnedTwice;

    public int Lent
    {
        get
        {
            lock (_lock)
            {
                return _lent;
            }
        }
    }

    public int Outstanding
    {
        get
        {
            lock (_lock)
            {
                return _outstanding.Count;
            }
        }
    }

    public int Returned
    {
        get
        {
            lock (_lock)
            {
                return _returned.Count;
            }
        }
    }

    public int ReturnedTwice
    {
        get
        {
            lock (_lock)
            {
                return _returnedTwice;
            }
        }
    }

    public bool NoneWrittenAfterReturn
    {
        get
        {
            lock (_lock)
            {
                return _returned.TrueForAll(stream =>
                    stream.Length == 0 && Array.TrueForAll(stream.GetBuffer(), b => b == Marker)
                );
            }
        }
    }

    public IPoolItemReservation<MemoryStream> Get()
    {
        lock (_lock)
        {
            var reservation = new Reservation(this, new MemoryStream(1024));

            _outstanding.Add(reservation);
            _lent++;

            return reservation;
        }
    }

    private void Return(Reservation reservation)
    {
        lock (_lock)
        {
            if (!_outstanding.Remove(reservation))
            {
                _returnedTwice++;

                return;
            }

            var stream = reservation.Item;

            Array.Fill(stream.GetBuffer(), Marker);
            stream.Position = 0;
            stream.SetLength(0);

            _returned.Add(stream);
        }
    }

    private sealed class Reservation(CountingStreamPool pool, MemoryStream item)
        : IPoolItemReservation<MemoryStream>
    {
        public MemoryStream Item { get; } = item;

        public void Dispose() => pool.Return(this);
    }
}
