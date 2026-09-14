namespace Hardened.Web.Runtime.Routing;

/// <summary>
/// A string-keyed table that can be probed with a <see cref="ReadOnlySpan{T}"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why not a dictionary.</b> <c>Dictionary&lt;string, T&gt;</c> and
/// <c>FrozenDictionary&lt;string, T&gt;</c> both need a <c>string</c> key, and the matcher has a
/// slice of the request path. Calling <c>ToString()</c> on it would allocate one string per segment
/// per request, which is what the <c>BytesAllocatedPerOperation</c> gate exists to catch.
/// <c>GetAlternateLookup&lt;ReadOnlySpan&lt;char&gt;&gt;</c> is the framework answer and it is .NET
/// 9; this package is net8.0.
/// </para>
/// <para>
/// So: open addressing with linear probing, built once and never written to again. The hash comes
/// from <see cref="string.GetHashCode(ReadOnlySpan{char}, StringComparison)"/>, which is the same
/// function for a string and for a span of its characters, so a key stored by string is found by
/// span.
/// </para>
/// </remarks>
internal sealed class SegmentLookup<TValue>
    where TValue : class
{
    private readonly string[] _keys;
    private readonly TValue?[] _values;
    private readonly int _mask;
    private readonly StringComparison _comparison;

    public static readonly SegmentLookup<TValue> Empty = new(
        new Dictionary<string, TValue>(),
        false
    );

    public SegmentLookup(IReadOnlyDictionary<string, TValue> entries, bool ignoreCase)
    {
        _comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        Count = entries.Count;

        // Half full at most. Linear probing degrades sharply past about two thirds, and a routing
        // table is built once and read for the life of the process, so the memory is the cheap side
        // of the trade.
        var capacity = 4;

        while (capacity < entries.Count * 2)
        {
            capacity <<= 1;
        }

        _keys = new string[capacity];
        _values = new TValue?[capacity];
        _mask = capacity - 1;

        foreach (var entry in entries)
        {
            var slot = string.GetHashCode(entry.Key.AsSpan(), _comparison) & _mask;

            while (_values[slot] != null)
            {
                slot = (slot + 1) & _mask;
            }

            _keys[slot] = entry.Key;
            _values[slot] = entry.Value;
        }
    }

    public int Count { get; }

    public bool TryGetValue(ReadOnlySpan<char> key, out TValue value)
    {
        if (Count == 0)
        {
            value = null!;

            return false;
        }

        var slot = string.GetHashCode(key, _comparison) & _mask;

        while (true)
        {
            var stored = _values[slot];

            if (stored == null)
            {
                value = null!;

                return false;
            }

            if (key.Equals(_keys[slot].AsSpan(), _comparison))
            {
                value = stored;

                return true;
            }

            slot = (slot + 1) & _mask;
        }
    }
}
