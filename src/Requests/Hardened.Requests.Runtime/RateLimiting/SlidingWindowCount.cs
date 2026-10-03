namespace Hardened.Requests.Runtime.RateLimiting;

/// <summary>
/// One partition's count over a sliding window, kept in <see cref="Segments"/> segments.
/// </summary>
/// <remarks>
/// <para>
/// Written here rather than taken from <c>System.Threading.RateLimiting</c>, because
/// <c>SlidingWindowRateLimiter</c> says nothing about when a permit returns: a refused lease carries
/// no <c>RetryAfter</c> and a granted one carries no metadata at all. The store could only answer
/// "one window" for both, however soon a permit came back.
/// </para>
/// <para>
/// A permit spent in segment <c>k</c> returns when segment <c>k + Segments</c> begins. Segments are
/// numbered from the clock rather than from when the count was created, so expiring them needs no
/// timer: each acquire first drops the segments that have ended since the last one.
/// </para>
/// </remarks>
internal sealed class SlidingWindowCount
{
    internal const int Segments = 8;

    private readonly int _permitLimit;
    private readonly long _segmentTicks;
    private readonly int[] _counts = new int[Segments];
    private long _current;
    private int _inUse;

    public SlidingWindowCount(int permitLimit, TimeSpan window, long nowTicks)
    {
        _permitLimit = permitLimit;
        _segmentTicks = Math.Max(1, window.Ticks / Segments);
        _current = nowTicks / _segmentTicks;
    }

    /// <summary>
    /// Spends a permit if one is left.
    /// </summary>
    /// <returns>
    /// Whether it was spent, how many are left, and how long until the oldest spent permit returns.
    /// The last is zero when nothing is spent.
    /// </returns>
    public (bool Acquired, int Remaining, TimeSpan Reset) TryAcquire(long nowTicks)
    {
        lock (_counts)
        {
            Advance(nowTicks / _segmentTicks);

            var acquired = _inUse < _permitLimit;

            if (acquired)
            {
                _counts[Slot(_current)]++;
                _inUse++;
            }

            return (acquired, _permitLimit - _inUse, UntilOldestReturns(nowTicks));
        }
    }

    private void Advance(long segment)
    {
        if (segment <= _current)
        {
            return;
        }

        var ended = Math.Min(segment - _current, Segments);

        // The slot segment _current + i takes is the one segment _current + i - Segments held, and
        // that segment's permits return as this one begins.
        for (var i = 1; i <= ended; i++)
        {
            var slot = Slot(_current + i);

            _inUse -= _counts[slot];
            _counts[slot] = 0;
        }

        _current = segment;
    }

    private TimeSpan UntilOldestReturns(long nowTicks)
    {
        for (var segment = _current - Segments + 1; segment <= _current; segment++)
        {
            if (_counts[Slot(segment)] > 0)
            {
                return TimeSpan.FromTicks((segment + Segments) * _segmentTicks - nowTicks);
            }
        }

        return TimeSpan.Zero;
    }

    private static int Slot(long segment) => (int)(((segment % Segments) + Segments) % Segments);
}
