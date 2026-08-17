using DependencyModules.Runtime.Attributes;
using Hardened.Requests.Abstract.Execution;

namespace Hardened.Requests.Runtime.RateLimiting;

/// <summary>
/// Decides whose allowance a request draws from.
/// </summary>
/// <remarks>
/// Swapped the same way <see cref="IRateLimitStore"/> is - implement it and register with
/// <c>[SingletonService(Using = RegistrationType.Replace)]</c>.
/// </remarks>
public interface IRateLimitPartitioner {

    /// <summary>
    /// A key identifying the caller. Never null: a request that cannot be attributed to anyone
    /// still has to count against something.
    /// </summary>
    string Partition(IExecutionContext context);
}

/// <summary>
/// Partitions by authenticated identity, and by a configured header otherwise.
/// </summary>
/// <remarks>
/// <para>
/// <b>Not by remote address.</b> <c>IExecutionRequest</c> has no concept of one, and behind API
/// Gateway, an ALB or CloudFront the socket peer is the proxy anyway - so an IP partition means
/// either "everyone shares one bucket" or "whatever the caller put in <c>X-Forwarded-For</c>",
/// and the second lets a caller choose their own bucket. Forwarded-header support has to land
/// before an address is a defensible key.
/// </para>
/// <para>
/// <b>Unattributable requests share one bucket rather than getting one each.</b> The alternative
/// is a distinct partition per anonymous caller, which is not a limit at all - it is a memory leak
/// with a rate limiter's name on it. Sharing is wrong in the sense that one noisy anonymous caller
/// can exhaust the allowance for the rest; it is wrong in the safe direction.
/// </para>
/// </remarks>
[SingletonService(Using = RegistrationType.Try)]
public class DefaultRateLimitPartitioner : IRateLimitPartitioner {

    /// <summary>
    /// Where every request that cannot be attributed to a caller counts.
    /// </summary>
    public const string Anonymous = "anonymous";

    private readonly RateLimitConfiguration _configuration;

    public DefaultRateLimitPartitioner(RateLimitConfiguration configuration) {
        _configuration = configuration;
    }

    public string Partition(IExecutionContext context) {
        var principal = context.CallerPrincipal;

        if (principal.IsAuthenticated && !string.IsNullOrEmpty(principal.Subject)) {
            return "sub:" + principal.Subject;
        }

        var header = _configuration.PartitionHeader;

        if (!string.IsNullOrEmpty(header) &&
            context.Request.Headers.TryGetValue(header, out var value)) {
            var key = value.ToString();

            if (!string.IsNullOrEmpty(key)) {
                return header + ":" + key;
            }
        }

        return Anonymous;
    }
}
