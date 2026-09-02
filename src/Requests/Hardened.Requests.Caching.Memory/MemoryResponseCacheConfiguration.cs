namespace Hardened.Requests.Caching.Memory;

/// <summary>
/// What the in-process store will hold.
/// </summary>
public interface IMemoryResponseCacheConfiguration {

    /// <summary>
    /// The most the store will hold, in bytes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Required rather than optional. A store with no limit is a memory leak with an expiry policy,
    /// and the process it grows in is one a container runtime kills without a stack trace. ASP.NET
    /// Core's output cache caps itself at 100 MB for the same reason; this is the same number.
    /// </para>
    /// <para>
    /// Counted in response bodies. <c>MemoryCache</c> enforces a limit in whatever unit its entries
    /// are sized in, and bytes is the only unit that means anything to whoever sets this.
    /// </para>
    /// </remarks>
    long SizeLimit { get; }

    /// <summary>
    /// The largest single response the store will hold, in bytes.
    /// </summary>
    /// <remarks>
    /// A per-entry cap as well as a total, because one large response is how a total gets spent on
    /// something nothing will hit again. 64 MB, matching ASP.NET Core's body cap.
    /// </remarks>
    long MaximumBodySize { get; }
}

/// <inheritdoc />
public class MemoryResponseCacheConfiguration : IMemoryResponseCacheConfiguration {

    public const long DefaultSizeLimit = 100L * 1024 * 1024;

    public const long DefaultMaximumBodySize = 64L * 1024 * 1024;

    public long SizeLimit { get; set; } = DefaultSizeLimit;

    public long MaximumBodySize { get; set; } = DefaultMaximumBodySize;
}
