using Hardened.Requests.Abstract.Execution;

namespace Hardened.Requests.Abstract.Authorization;

/// <summary>
/// What an operation requires of its caller, as an immutable expression over grants.
/// </summary>
/// <remarks>
/// <para>
/// <b>Composition lives here rather than in the attribute.</b> One type per named policy, with the
/// permutations inside it, is what stops a type explosion - an attribute form carrying the boolean
/// structure instead would need a new type per permutation of grants.
/// </para>
/// <para>
/// Evaluated, not compiled, and cheap: a policy builds its tree once and each request walks it
/// against an <see cref="IReadOnlySet{T}"/>.
/// </para>
/// <example>
/// <code>
/// (Grant("pets:read") &amp; Grant("pets:write")) | Grant("admin:*")
/// </code>
/// <c>&amp;</c> binds tighter than <c>|</c> in C#, so the parentheses above are documentation rather
/// than necessity.
/// </example>
/// </remarks>
public abstract class Requirement {
    /// <summary>
    /// True when evaluating this requirement needs the execution context, not merely the principal.
    /// </summary>
    /// <remarks>
    /// Decides where the authorization filter sits in the chain. A requirement over grants alone can
    /// be settled before the body is read; one that inspects bound parameters - "may this caller edit
    /// <em>this</em> pet" - cannot. Known at filter-construction time, so it costs one test per
    /// handler rather than one per request.
    /// </remarks>
    public abstract bool RequiresContext { get; }

    /// <summary>
    /// Every grant named anywhere in this requirement.
    /// </summary>
    /// <remarks>
    /// A deny names what it wanted: a 403 for insufficient scope carries these in its
    /// <c>WWW-Authenticate</c> header. The set is the union across branches, so an OR reports every
    /// grant that would have satisfied it rather than picking one arbitrarily.
    /// </remarks>
    public abstract IEnumerable<string> RequiredGrants { get; }

    /// <summary>
    /// Walks the tree against a caller.
    /// </summary>
    public abstract bool IsSatisfiedBy(ICallerPrincipal principal, IExecutionContext context);

    /// <summary>Requires one grant.</summary>
    public static Requirement Grant(string grant) {
        if (string.IsNullOrEmpty(grant)) {
            throw new ArgumentException("A grant cannot be empty.", nameof(grant));
        }

        return new GrantRequirement(grant);
    }

    /// <summary>Requires all of <paramref name="requirements"/>.</summary>
    public static Requirement AllOf(params Requirement[] requirements) =>
        Combine(requirements, all: true);

    /// <summary>Requires any one of <paramref name="requirements"/>.</summary>
    public static Requirement AnyOf(params Requirement[] requirements) =>
        Combine(requirements, all: false);

    /// <summary>
    /// The escape hatch: an arbitrary test over the caller and the request.
    /// </summary>
    /// <param name="predicate">The test. Runs after parameters are bound.</param>
    /// <param name="description">
    /// What the predicate checks, for diagnostics. A lambda has no useful name of its own.
    /// </param>
    /// <remarks>
    /// Using one moves the whole requirement to the later pipeline position, because a predicate may
    /// read bound parameters.
    /// </remarks>
    public static Requirement Predicate(
        Func<ICallerPrincipal, IExecutionContext, bool> predicate,
        string? description = null) {
        ArgumentNullException.ThrowIfNull(predicate);

        return new PredicateRequirement(predicate, description);
    }

    public static Requirement operator &(Requirement left, Requirement right) => AllOf(left, right);

    public static Requirement operator |(Requirement left, Requirement right) => AnyOf(left, right);

    /// <remarks>
    /// <para>
    /// An empty set throws rather than evaluating to anything. Both readings are defensible in
    /// isolation - nothing required, or nothing satisfiable - and one of them silently grants
    /// access. A requirement is only ever built because something declared a constraint, so an empty
    /// one means the declaration was wrong, and saying so is better than picking.
    /// </para>
    /// <para>
    /// Nested nodes of the same kind are flattened, so <c>(a &amp; b) &amp; c</c> is one node of
    /// three rather than a chain. Purely a tidiness measure - it changes no result - but it keeps
    /// <see cref="RequiredGrants"/> and the rendered description readable.
    /// </para>
    /// </remarks>
    private static Requirement Combine(Requirement[] requirements, bool all) {
        if (requirements is null || requirements.Length == 0) {
            throw new ArgumentException(
                "A requirement must name at least one condition. An empty one has no safe reading: " +
                "it means either 'nothing is required' or 'nothing can satisfy this', and the first " +
                "of those silently grants access.",
                nameof(requirements));
        }

        if (requirements.Any(r => r is null)) {
            throw new ArgumentException("A requirement cannot be null.", nameof(requirements));
        }

        if (requirements.Length == 1) {
            return requirements[0];
        }

        var flattened = requirements
            .SelectMany(r => r is CompositeRequirement composite && composite.All == all
                ? composite.Requirements
                : [r])
            .ToArray();

        return new CompositeRequirement(flattened, all);
    }

    private sealed class GrantRequirement : Requirement {
        private readonly string _grant;

        public GrantRequirement(string grant) {
            _grant = grant;
        }

        public override bool RequiresContext => false;

        public override IEnumerable<string> RequiredGrants => [_grant];

        public override bool IsSatisfiedBy(ICallerPrincipal principal, IExecutionContext context) =>
            principal.Grants.Contains(_grant);

        public override string ToString() => _grant;
    }

    private sealed class CompositeRequirement : Requirement {
        public CompositeRequirement(Requirement[] requirements, bool all) {
            Requirements = requirements;
            All = all;
        }

        public Requirement[] Requirements { get; }

        public bool All { get; }

        public override bool RequiresContext => Requirements.Any(r => r.RequiresContext);

        public override IEnumerable<string> RequiredGrants =>
            Requirements.SelectMany(r => r.RequiredGrants).Distinct(StringComparer.Ordinal);

        public override bool IsSatisfiedBy(ICallerPrincipal principal, IExecutionContext context) =>
            All
                ? Requirements.All(r => r.IsSatisfiedBy(principal, context))
                : Requirements.Any(r => r.IsSatisfiedBy(principal, context));

        public override string ToString() =>
            "(" + string.Join(All ? " & " : " | ", Requirements.Select(r => r.ToString())) + ")";
    }

    private sealed class PredicateRequirement : Requirement {
        private readonly Func<ICallerPrincipal, IExecutionContext, bool> _predicate;
        private readonly string? _description;

        public PredicateRequirement(
            Func<ICallerPrincipal, IExecutionContext, bool> predicate,
            string? description) {
            _predicate = predicate;
            _description = description;
        }

        public override bool RequiresContext => true;

        public override IEnumerable<string> RequiredGrants => [];

        public override bool IsSatisfiedBy(ICallerPrincipal principal, IExecutionContext context) =>
            _predicate(principal, context);

        public override string ToString() => _description ?? "predicate";
    }
}
