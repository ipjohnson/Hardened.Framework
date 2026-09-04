using Hardened.Requests.Abstract.Errors;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.RequestFilter;
using Hardened.Requests.Abstract.Responses;

namespace Hardened.Requests.Runtime.RateLimiting;

/// <summary>
/// Whose volume is being limited, which decides where the filter runs.
/// </summary>
public enum RateLimitScope {
    /// <summary>
    /// Whatever identifies the connection, before anyone has looked at a credential. Refuses
    /// without reading the request body, which is what makes it useful against a flood.
    /// </summary>
    Transport,

    /// <summary>
    /// The authenticated caller. Runs late enough to know who that is, which means late enough that
    /// the body has already been read.
    /// </summary>
    Principal
}

/// <summary>
/// Limits how often a handler may be called.
/// </summary>
/// <remarks>
/// <para>
/// Configured entirely by property, so it needs no source generator support beyond what
/// <c>[Retry]</c> already proved: an attribute implementing <see cref="IRequestFilterProvider"/>
/// reaches the pipeline on its own.
/// </para>
/// <para>
/// <b>The counting is not done here.</b> It is done by whatever <see cref="IRateLimitStore"/>
/// resolves to, which by default counts in this process and therefore counts separately on every
/// instance. On more than one replica, and on Lambda especially, that is not a limit - see the
/// store's own documentation for what to do instead.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
[AnswersStatus(429, typeof(ErrorModel),
    Description = "The caller has spent this operation's allowance. Retry-After says when it returns.")]
public class RateLimitAttribute : Attribute, IRequestFilterProvider {

    /// <summary>Requests allowed per <see cref="WindowSeconds"/>.</summary>
    public int PermitLimit { get; set; } = 100;

    public int WindowSeconds { get; set; } = 60;

    /// <summary>
    /// Which allowance this is. Two limits on one handler - a burst limit and an hourly one - need
    /// different names or they share a counter.
    /// </summary>
    public string Name { get; set; } = "default";

    /// <summary>
    /// Whose volume this counts. <see cref="RateLimitScope.Transport"/> by default, which refuses
    /// before the body is read.
    /// </summary>
    public RateLimitScope Scope { get; set; } = RateLimitScope.Transport;

    public IEnumerable<RequestFilterInfo> GetFilters(IExecutionRequestHandlerInfo handlerInfo) {
        var order = Scope == RateLimitScope.Transport
            ? FilterOrder.RateLimitTransport
            : FilterOrder.RateLimitPrincipal;

        var policy = new RateLimitPolicy(PermitLimit, TimeSpan.FromSeconds(WindowSeconds), Name);

        // Computed here rather than read from configuration, so the filter's idea of where it sits
        // and the order it is actually registered at cannot drift apart.
        var beforeSerialization = order < FilterOrder.Serialization;

        yield return new RequestFilterInfo(
            _ => new RateLimitFilter(policy, beforeSerialization), order, nameof(RateLimitFilter));
    }
}
