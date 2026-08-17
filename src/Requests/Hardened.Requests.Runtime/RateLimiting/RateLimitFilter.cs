using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.RequestFilter;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Requests.Runtime.RateLimiting;

/// <summary>
/// Refuses a request whose partition has spent its allowance.
/// </summary>
/// <remarks>
/// <para>
/// <b>How it refuses depends on where it sits.</b> The filter that turns a failure into a response
/// is at <see cref="FilterOrder.Serialization"/>. Ahead of it, returning without calling
/// <c>Next</c> produces a 429 with an empty body, because nothing downstream ever runs to write
/// one - so ahead of it the filter records the refusal and <em>continues</em>, and the
/// serialization filter finds a request already decided, reads no body, invokes no handler, and
/// writes the refusal on the way out. Behind it, an ordinary short circuit is what stops the
/// handler. This is the same split <c>AuthorizationFilter</c> makes, for the same reason.
/// </para>
/// <para>
/// <b>Both positions are legitimate and they are not the same feature.</b> A limiter meant to blunt
/// credential stuffing has to run before authentication, keyed on whatever identifies the transport.
/// A limiter keyed on who the caller turned out to be has to run after it. Which one this is comes
/// from the order it was registered at.
/// </para>
/// </remarks>
public class RateLimitFilter : IExecutionFilter {
    private readonly RateLimitPolicy _policy;
    private readonly bool _beforeSerialization;

    /// <param name="beforeSerialization">
    /// Whether this sits ahead of the filter that turns a failure into a response. Must agree with
    /// the order the filter was registered at, which is why both are decided together in
    /// <see cref="RateLimitAttribute"/> rather than passed in from two places.
    /// </param>
    public RateLimitFilter(RateLimitPolicy policy, bool beforeSerialization) {
        _policy = policy;
        _beforeSerialization = beforeSerialization;
    }

    public async Task Execute(IExecutionChain chain) {
        var context = chain.Context;

        var store = context.RequestServices.GetService<IRateLimitStore>();

        if (store == null) {
            await chain.Next();

            return;
        }

        var partitioner = context.RequestServices.GetRequiredService<IRateLimitPartitioner>();

        var decision = await store.Acquire(
            partitioner.Partition(context), _policy, context.CancellationToken);

        if (decision.Allowed) {
            // Told before being refused, so a client can slow down rather than discover the limit
            // by hitting it.
            RateLimitExceededException.ApplyRateLimitHeaders(
                context.Response.Headers, decision, (int)_policy.Window.TotalSeconds);

            await chain.Next();

            return;
        }

        context.Response.ExceptionValue = new RateLimitExceededException(decision);

        if (_beforeSerialization) {
            await chain.Next();
        }
    }
}
