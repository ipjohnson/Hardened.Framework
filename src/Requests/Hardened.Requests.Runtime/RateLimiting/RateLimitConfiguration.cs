using DependencyModules.Runtime.Attributes;

namespace Hardened.Requests.Runtime.RateLimiting;

/// <summary>
/// Defaults for rate limiting, and the knobs the in-process store needs.
/// </summary>
[SingletonService(Using = RegistrationType.Try)]
public class RateLimitConfiguration {

    /// <summary>
    /// The header identifying an unauthenticated caller - an API key, a tenant id. Empty means
    /// every unauthenticated request shares one allowance.
    /// </summary>
    /// <remarks>
    /// Only trust a header a proxy in front of this application sets and strips from the inbound
    /// request. A header a caller can set is a bucket a caller can choose.
    /// </remarks>
    public string PartitionHeader { get; set; } = "";

    /// <summary>Requests per <see cref="DefaultWindow"/> when an attribute names no limit.</summary>
    public int DefaultPermitLimit { get; set; } = 100;

    public TimeSpan DefaultWindow { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// How many partitions the in-process store will track before it stops creating new ones.
    /// </summary>
    /// <remarks>
    /// A partition per caller is unbounded by construction, and a limiter that runs the process out
    /// of memory has done more damage than the traffic it was refusing. At the cap the store fails
    /// open - see <see cref="InProcessRateLimitStore"/> for why open rather than closed.
    /// </remarks>
    public int MaxTrackedPartitions { get; set; } = 10_000;
}
