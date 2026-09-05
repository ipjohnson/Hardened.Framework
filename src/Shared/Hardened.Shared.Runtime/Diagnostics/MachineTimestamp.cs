using System.Diagnostics;

namespace Hardened.Shared.Runtime.Diagnostics;

/// <summary>
/// Timestamp that uses the machine ticks, it is only valid on the local machine.
/// </summary>
public readonly struct MachineTimestamp : IEquatable<MachineTimestamp>, IComparable<MachineTimestamp> {
    public static readonly double SecondsToTicksRatio = TimeSpan.TicksPerSecond / (double)Stopwatch.Frequency;
    public static readonly double MillisecondsToTicksRatio = 1 / (double)TimeSpan.TicksPerMillisecond;
    private readonly long _timestamp;

    private MachineTimestamp(long timestamp) {
        _timestamp = timestamp;
    }

    /// <summary>
    /// Create timestamp from machine ticks
    /// </summary>
    /// <param name="ticks"></param>
    /// <returns></returns>
    public static MachineTimestamp FromTicks(long ticks) {
        return new MachineTimestamp(ticks);
    }

    /// <summary>
    /// Get timestamp for now
    /// </summary>
    public static MachineTimestamp Now => FromTicks(Stopwatch.GetTimestamp());

    /// <summary>
    /// Get the elapsed milliseconds from now to the timestamp
    /// </summary>
    /// <returns></returns>
    public double GetElapsedMilliseconds() {
        var totalElapsedTime = Stopwatch.GetTimestamp() - TimestampOrThrow();

        return (totalElapsedTime * SecondsToTicksRatio) * MillisecondsToTicksRatio;
    }

    /// <summary>
    /// Get elapsed time from when timestamp was created to now
    /// </summary>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    public TimeSpan GetElapsedTime() {
        var totalElapsedTime = Stopwatch.GetTimestamp() - TimestampOrThrow();

        return new TimeSpan((long)(totalElapsedTime * SecondsToTicksRatio));
    }

    /// <summary>
    /// Move the timestamp forward by a number of milliseconds, giving a deadline that far from it.
    /// A negative value moves it back.
    /// </summary>
    /// <param name="milliseconds"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    public MachineTimestamp AddMs(double milliseconds) {
        var machineTicks = (milliseconds / 1000) * Stopwatch.Frequency;

        return new MachineTimestamp(TimestampOrThrow() + (long)machineTicks);
    }

    /// <summary>
    /// The timestamp has gone by
    /// </summary>
    /// <exception cref="Exception"></exception>
    public bool Past => GetRemainingTicks() < 0;

    /// <summary>
    /// The clock reads the timestamp's own tick. True for at most one tick, so a deadline is
    /// tested with <see cref="Past"/> or <see cref="Future"/> rather than with this.
    /// </summary>
    /// <exception cref="Exception"></exception>
    public bool Present => GetRemainingTicks() == 0;

    /// <summary>
    /// The timestamp is still ahead
    /// </summary>
    /// <exception cref="Exception"></exception>
    public bool Future => GetRemainingTicks() > 0;

    /// <summary>
    /// Get the milliseconds from now to the timestamp, negative once the timestamp is in the past
    /// </summary>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    public double GetRemainingMilliseconds() {
        return (GetRemainingTicks() * SecondsToTicksRatio) * MillisecondsToTicksRatio;
    }

    /// <summary>
    /// Get the seconds from now to the timestamp, negative once the timestamp is in the past
    /// </summary>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    public double GetRemainingSeconds() {
        return (GetRemainingTicks() * SecondsToTicksRatio) / TimeSpan.TicksPerSecond;
    }

    /// <summary>
    /// Get the time from one timestamp to another, negative when the right one is the later
    /// </summary>
    /// <param name="left"></param>
    /// <param name="right"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    public static TimeSpan operator -(MachineTimestamp left, MachineTimestamp right) {
        var totalTime = left.TimestampOrThrow() - right.TimestampOrThrow();

        return new TimeSpan((long)(totalTime * SecondsToTicksRatio));
    }

    /// <summary>
    /// Order two timestamps by the machine ticks they hold. Unlike the readings above this does not
    /// reject an uninitialized timestamp, because sorting and equality must not throw.
    /// </summary>
    /// <param name="other"></param>
    /// <returns></returns>
    public int CompareTo(MachineTimestamp other) {
        return _timestamp.CompareTo(other._timestamp);
    }

    /// <summary>
    /// Two timestamps of the same machine ticks are the same timestamp
    /// </summary>
    /// <param name="other"></param>
    /// <returns></returns>
    public bool Equals(MachineTimestamp other) {
        return _timestamp == other._timestamp;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) {
        return obj is MachineTimestamp other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode() {
        return _timestamp.GetHashCode();
    }

    public static bool operator ==(MachineTimestamp left, MachineTimestamp right) {
        return left._timestamp == right._timestamp;
    }

    public static bool operator !=(MachineTimestamp left, MachineTimestamp right) {
        return left._timestamp != right._timestamp;
    }

    public static bool operator <(MachineTimestamp left, MachineTimestamp right) {
        return left._timestamp < right._timestamp;
    }

    public static bool operator >(MachineTimestamp left, MachineTimestamp right) {
        return left._timestamp > right._timestamp;
    }

    public static bool operator <=(MachineTimestamp left, MachineTimestamp right) {
        return left._timestamp <= right._timestamp;
    }

    public static bool operator >=(MachineTimestamp left, MachineTimestamp right) {
        return left._timestamp >= right._timestamp;
    }

    private long GetRemainingTicks() {
        return TimestampOrThrow() - Stopwatch.GetTimestamp();
    }

    private long TimestampOrThrow() {
        if (_timestamp == 0) {
            throw new Exception("MachineTimestamp was not initialized, can't be used here");
        }

        return _timestamp;
    }
}