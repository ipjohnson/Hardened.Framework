using System.Diagnostics.CodeAnalysis;
using Hardened.Requests.Abstract.Authorization;
using Hardened.Requests.Abstract.Execution;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Requests.Runtime.Authorization;

/// <summary>
/// Decides whether a request may proceed, and refuses it if not.
/// </summary>
/// <remarks>
/// <para>
/// The fast path is the whole design: a requirement over grants the credential already carries is
/// settled synchronously, against a set, with no service resolved and nothing awaited. Everything
/// below it runs only for a request that is about to be refused.
/// </para>
/// <para>
/// One instance per handler, shared across requests. The requirement is immutable and the filter
/// holds no per-request state.
/// </para>
/// </remarks>
public class AuthorizationFilter : IExecutionFilter {
    private readonly Requirement _requirement;
    private readonly bool _beforeSerialization;

    /// <param name="beforeSerialization">
    /// Whether this filter sits ahead of the filter that turns a failure into a response. It decides
    /// how a refusal is delivered, and it must agree with the order the filter was registered at -
    /// which is why both are computed together in <see cref="AuthorizationFilterProvider"/> rather
    /// than passed in from two places.
    /// </param>
    public AuthorizationFilter(Requirement requirement, bool beforeSerialization) {
        _requirement = requirement;
        _beforeSerialization = beforeSerialization;
    }

    public async Task Execute(IExecutionChain chain) {
        var context = chain.Context;

        if (_requirement.IsSatisfiedBy(context.CallerPrincipal, context)) {
            await chain.Next();
            return;
        }

        var (satisfied, refusal) = await Resolve(context);

        if (satisfied) {
            await chain.Next();
            return;
        }

        await Refuse(chain, refusal);
    }

    /// <summary>
    /// Asks whether any grant the requirement wants can be resolved from somewhere other than the
    /// credential, then re-evaluates against what came back.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One grant per question, deliberately. The service answers "does this caller hold these
    /// grants", which is a conjunction - asking it about a requirement's whole grant set would turn
    /// <c>a | b</c> into <c>a &amp; b</c> and refuse a caller who legitimately holds one of them.
    /// Resolving grant by grant and re-walking the tree preserves whatever structure the requirement
    /// actually has.
    /// </para>
    /// <para>
    /// The refusals are kept as well as the permits, because a handler saying the credential is too
    /// weak is what turns the eventual answer from a 403 into a 401.
    /// </para>
    /// </remarks>
    private async ValueTask<(bool Satisfied, AuthorizationDecision Refusal)> Resolve(
        IExecutionContext context) {
        var principal = context.CallerPrincipal;
        var service = context.RequestServices.GetService<IActivityAuthorizationService>();

        if (service == null) {
            return (false, AuthorizationDecision.Abstain);
        }

        HashSet<string>? resolved = null;
        var refusal = AuthorizationDecision.Abstain;

        foreach (var grant in _requirement.RequiredGrants) {
            // Already held, so there is nothing to resolve and the tree has already accounted for it.
            if (principal.Grants.Contains(grant)) {
                continue;
            }

            var decision = await service.Authorize(context, grant);

            if (decision.Permits()) {
                (resolved ??= new HashSet<string>(StringComparer.Ordinal)).Add(grant);
            }
            else {
                refusal = AuthorizationDecisions.Combine(refusal, decision);
            }
        }

        if (resolved == null) {
            return (false, refusal);
        }

        return (_requirement.IsSatisfiedBy(new ResolvedPrincipal(principal, resolved), context), refusal);
    }

    /// <summary>
    /// Turns the refusal into a response.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="ICallerPrincipal.IsAuthenticated"/> decides the common cases: a caller who
    /// presented nothing is told to authenticate, and an authenticated caller who lacks grants is
    /// told which ones. The case it cannot decide is a valid credential that is too weak, which is
    /// why that answer travels on the decision rather than being inferred here.
    /// </para>
    /// </remarks>
    private Task Refuse(IExecutionChain chain, AuthorizationDecision refusal) {
        var context = chain.Context;

        var challenge =
            !context.CallerPrincipal.IsAuthenticated
                ? AuthorizationChallenge.AuthenticationRequired()
                : refusal == AuthorizationDecision.DenyInsufficientAuthentication
                    ? AuthorizationChallenge.InsufficientAuthentication()
                    : AuthorizationChallenge.InsufficientScope(_requirement.RequiredGrants);

        context.Response.ExceptionValue = new AuthorizationException(challenge);

        // The two positions sit on opposite sides of the filter that writes a response, so a refusal
        // reaches the wire two different ways.
        //
        // Ahead of it, the chain has to continue or nothing serializes at all - and continuing is
        // safe, because that filter finds a request already decided, reads no body and invokes no
        // handler. Behind it, that filter has already been entered, so returning without calling
        // Next() is exactly what stops the handler and lets it write the refusal on the way out.
        return _beforeSerialization ? chain.Next() : Task.CompletedTask;
    }

    /// <summary>
    /// The caller plus whatever was resolved for them, for the second walk of the tree.
    /// </summary>
    /// <remarks>
    /// Not installed on the context. The resolved grants exist to answer this one requirement;
    /// leaving them on the principal would silently widen every later check in the request, and a
    /// grant resolved for "may read this pet" is not a grant the caller holds generally.
    /// </remarks>
    private sealed class ResolvedPrincipal : ICallerPrincipal {
        private readonly ICallerPrincipal _principal;

        public ResolvedPrincipal(ICallerPrincipal principal, IReadOnlySet<string> resolved) {
            _principal = principal;
            Grants = new UnionSet(principal.Grants, resolved);
        }

        public string? AuthenticationScheme => _principal.AuthenticationScheme;

        public IReadOnlySet<string> Grants { get; }

        public string? Subject => _principal.Subject;

        public string? Issuer => _principal.Issuer;

        public bool TryGetClaim(string name, [MaybeNullWhen(false)] out string value) =>
            _principal.TryGetClaim(name, out value);
    }

    /// <summary>
    /// Both sets, without copying either. Only <c>Contains</c> is on the hot path; the rest exists
    /// because the interface asks for it.
    /// </summary>
    private sealed class UnionSet : IReadOnlySet<string> {
        private readonly IReadOnlySet<string> _first;
        private readonly IReadOnlySet<string> _second;

        public UnionSet(IReadOnlySet<string> first, IReadOnlySet<string> second) {
            _first = first;
            _second = second;
        }

        public bool Contains(string item) => _first.Contains(item) || _second.Contains(item);

        public int Count => this.Distinct(StringComparer.Ordinal).Count();

        public IEnumerator<string> GetEnumerator() =>
            _first.Concat(_second).Distinct(StringComparer.Ordinal).GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
            GetEnumerator();

        public bool IsProperSubsetOf(IEnumerable<string> other) => Materialize().IsProperSubsetOf(other);

        public bool IsProperSupersetOf(IEnumerable<string> other) => Materialize().IsProperSupersetOf(other);

        public bool IsSubsetOf(IEnumerable<string> other) => Materialize().IsSubsetOf(other);

        public bool IsSupersetOf(IEnumerable<string> other) => Materialize().IsSupersetOf(other);

        public bool Overlaps(IEnumerable<string> other) => Materialize().Overlaps(other);

        public bool SetEquals(IEnumerable<string> other) => Materialize().SetEquals(other);

        private HashSet<string> Materialize() => new(this, StringComparer.Ordinal);
    }
}
