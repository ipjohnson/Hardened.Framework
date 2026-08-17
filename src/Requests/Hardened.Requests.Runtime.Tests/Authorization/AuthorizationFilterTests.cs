using Hardened.Requests.Abstract.Authorization;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Runtime.Authorization;
using Hardened.Requests.Runtime.Execution;
using Hardened.Requests.Runtime.Filters;
using Hardened.Requests.Runtime.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Authorization;

/// <summary>
/// The filter that decides whether a request proceeds.
///
/// <para>
/// Run over a real chain with a real context rather than against substitutes, because the two things
/// most worth checking are not what the filter returns but what happens around it: that a refused
/// request never reaches the handler, and that its body is never read.
/// </para>
/// </summary>
public class AuthorizationFilterTests {

    /// <summary>Resolves grants that are not in the credential - a permissions table stands in.</summary>
    private sealed class ResolvingHandler : IActivityAuthorizationHandler {
        private readonly HashSet<string> _resolvable;
        private readonly AuthorizationDecision _verdict;

        public ResolvingHandler(
            IEnumerable<string> resolvable,
            AuthorizationDecision verdict = AuthorizationDecision.Abstain) {
            _resolvable = new HashSet<string>(resolvable, StringComparer.Ordinal);
            _verdict = verdict;
        }

        /// <summary>How many times the table was consulted.</summary>
        public int Calls { get; private set; }

        public ValueTask<GrantResolution> Resolve(
            IExecutionContext context, IReadOnlyList<string> grants) {
            Calls++;

            return new ValueTask<GrantResolution>(
                new GrantResolution(
                    grants.Where(_resolvable.Contains).ToHashSet(StringComparer.Ordinal),
                    _verdict));
        }
    }

    private static IExecutionContext Context(
        ICallerPrincipal? principal = null,
        params IActivityAuthorizationHandler[] handlers) {
        var context = Pipeline.Context(configureServices: services => {
            services.AddSingleton<IActivityAuthorizationService, ActivityAuthorizationService>();
            services.AddSingleton<IActivityAuthorizationHandler, PrincipalGrantAuthorizationHandler>();

            foreach (var handler in handlers) {
                services.AddSingleton(handler);
            }
        });

        if (principal != null) {
            context.CallerPrincipal = principal;
        }

        return context;
    }

    private static ICallerPrincipal Holding(params string[] grants) =>
        new CallerPrincipal("bearer", grants);

    private static AuthorizationException Refusal(IExecutionContext context) =>
        Assert.IsType<AuthorizationException>(context.Response.ExceptionValue);

    #region the fast path

    [Fact]
    public async Task ARequirementTheCredentialSatisfiesLetsTheRequestThrough() {
        var log = new List<string>();
        var context = Context(Holding("pets:read"));

        var filter = new AuthorizationFilter(Requirement.Grant("pets:read"), beforeSerialization: true);

        await Pipeline.Chain(context, filter, new Pipeline.Recording(log, "handler")).Next();

        Assert.Equal(["handler"], log);
        Assert.Null(context.Response.ExceptionValue);
    }

    /// <summary>
    /// The point of evaluating the tree against the credential first: a request that is already
    /// authorized resolves no service and awaits nothing.
    /// </summary>
    [Fact]
    public async Task ASatisfiedRequirementConsultsNoContributor() {
        var handler = new ResolvingHandler([], AuthorizationDecision.Deny);
        var context = Context(Holding("pets:read"), handler);

        var filter = new AuthorizationFilter(Requirement.Grant("pets:read"), beforeSerialization: true);

        await Pipeline.Chain(context, filter, new Pipeline.Recording([], "handler")).Next();

        // The deny-everything contributor was never asked; had it been, this would have refused.
        Assert.Null(context.Response.ExceptionValue);
    }

    #endregion

    #region refusing ahead of the serializer

    /// <summary>
    /// The case the early position exists for. A request presenting no credential must not cost a
    /// deserialization before it is rejected - so the body is never read, and the handler never runs,
    /// but a response is still written.
    /// </summary>
    [Fact]
    public async Task ARefusalAheadOfTheSerializerReadsNoBodyAndRunsNoHandler() {
        var log = new List<string>();
        var context = Context();
        var deserialized = false;

        var auth = new AuthorizationFilter(Requirement.Grant("pets:read"), beforeSerialization: true);

        var io = new IoFilter(
            _ => {
                deserialized = true;

                return Task.FromResult(EmptyParameters.Instance);
            },
            _ => {
                log.Add("serialize");

                return Task.CompletedTask;
            },
            headerActions: null);

        await Pipeline.Chain(context, auth, io, new Pipeline.Recording(log, "handler")).Next();

        Assert.False(deserialized);
        Assert.DoesNotContain("handler", log);

        // Still serialized, so the caller gets a status and a challenge rather than a dead connection.
        Assert.Contains("serialize", log);
        Assert.Equal(401, Refusal(context).StatusCode);
    }

    #endregion

    #region refusing behind the serializer

    /// <summary>
    /// The later position is inside the serializing filter, so stopping means returning without
    /// continuing - the refusal is written on the way back out.
    /// </summary>
    [Fact]
    public async Task ARefusalBehindTheSerializerStopsTheChain() {
        var log = new List<string>();
        var context = Context();

        var filter = new AuthorizationFilter(
            Requirement.Predicate((_, _) => false, "never"), beforeSerialization: false);

        await Pipeline.Chain(context, filter, new Pipeline.Recording(log, "handler")).Next();

        Assert.Empty(log);
        Assert.NotNull(context.Response.ExceptionValue);
    }

    #endregion

    #region which refusal

    /// <summary>
    /// No credential presented: 401, and no <c>error</c> parameter, because there is no token to
    /// have been wrong about.
    /// </summary>
    [Fact]
    public async Task AnAnonymousCallerIsToldToAuthenticate() {
        var context = Context();

        await Run(context, Requirement.Grant("pets:read"));

        var challenge = Refusal(context).Challenge;

        Assert.Equal(401, challenge.StatusCode);
        Assert.Null(challenge.Error);
    }

    /// <summary>
    /// Authenticated but short of grants: 403, naming what would have worked.
    /// </summary>
    [Fact]
    public async Task AnAuthenticatedCallerShortOfGrantsIsToldWhichOnes() {
        var context = Context(Holding("pets:read"));

        await Run(context, Requirement.Grant("pets:read") & Requirement.Grant("pets:write"));

        var challenge = Refusal(context).Challenge;

        Assert.Equal(403, challenge.StatusCode);
        Assert.Equal("insufficient_scope", challenge.Error);
        Assert.Contains("pets:write", challenge.Scope);
    }

    /// <summary>
    /// The one case a principal cannot decide: a valid credential that is too weak. It is a 401
    /// rather than a 403 because the remedy is a better credential, and the only thing that knows
    /// that is the contributor comparing the claim.
    /// </summary>
    [Fact]
    public async Task AContributorAskingForAStrongerCredentialTurnsA403IntoA401() {
        var context = Context(
            Holding("pets:read"),
            new ResolvingHandler([], AuthorizationDecision.DenyInsufficientAuthentication));

        await Run(context, Requirement.Grant("pets:admin"));

        var challenge = Refusal(context).Challenge;

        Assert.Equal(401, challenge.StatusCode);
        Assert.Equal("insufficient_user_authentication", challenge.Error);
    }

    #endregion

    #region resolved grants

    /// <summary>
    /// The reason the service exists: a grant that is not in the credential but is held somewhere
    /// the framework does not know about.
    /// </summary>
    [Fact]
    public async Task AGrantResolvedByAContributorSatisfiesTheRequirement() {
        var log = new List<string>();
        var context = Context(Holding(), new ResolvingHandler(["pets:read"]));

        var filter = new AuthorizationFilter(Requirement.Grant("pets:read"), beforeSerialization: true);

        await Pipeline.Chain(context, filter, new Pipeline.Recording(log, "handler")).Next();

        Assert.Equal(["handler"], log);
        Assert.Null(context.Response.ExceptionValue);
    }

    /// <summary>
    /// One call, however many grants the requirement names. A contributor backed by a table gets to
    /// make a single round trip with the whole list rather than one per grant, which is why the
    /// resolution returns the subset held rather than a yes or no.
    /// </summary>
    [Fact]
    public async Task EveryGrantIsResolvedInOneCall() {
        var handler = new ResolvingHandler(["a", "b", "c"]);
        var context = Context(Holding(), handler);

        var requirement = Requirement.Grant("a") & Requirement.Grant("b") & Requirement.Grant("c");

        await Pipeline.Chain(
            context,
            new AuthorizationFilter(requirement, beforeSerialization: true),
            new Pipeline.Recording([], "handler")).Next();

        Assert.Equal(1, handler.Calls);
        Assert.Null(context.Response.ExceptionValue);
    }

    /// <summary>
    /// The correctness case that one bulk call has to preserve. A contributor is asked which of the
    /// grants the caller holds, not whether it holds all of them - a single verdict over several
    /// grants could only mean "all", which would turn <c>a | b</c> into <c>a &amp; b</c> and refuse
    /// a caller who legitimately holds one branch.
    /// </summary>
    [Fact]
    public async Task ResolvingOneBranchOfAnOrIsEnough() {
        var log = new List<string>();
        var context = Context(Holding(), new ResolvingHandler(["admin:*"]));

        var requirement = Requirement.Grant("pets:read") | Requirement.Grant("admin:*");
        var filter = new AuthorizationFilter(requirement, beforeSerialization: true);

        await Pipeline.Chain(context, filter, new Pipeline.Recording(log, "handler")).Next();

        Assert.Equal(["handler"], log);
    }

    /// <summary>
    /// And the other half of that: resolving one branch of an <em>and</em> is not enough.
    /// </summary>
    [Fact]
    public async Task ResolvingOneBranchOfAnAndIsNotEnough() {
        var context = Context(Holding(), new ResolvingHandler(["pets:read"]));

        await Run(context, Requirement.Grant("pets:read") & Requirement.Grant("pets:write"));

        Assert.Equal(403, Refusal(context).StatusCode);
    }

    /// <summary>
    /// Resolved grants answer this requirement and are not left on the principal. A grant resolved
    /// for one check is not a grant the caller holds generally, and installing it would silently
    /// widen every later check in the same request.
    /// </summary>
    [Fact]
    public async Task ResolvedGrantsDoNotStayOnThePrincipal() {
        var context = Context(Holding(), new ResolvingHandler(["pets:read"]));

        await Run(context, Requirement.Grant("pets:read"));

        Assert.DoesNotContain("pets:read", context.CallerPrincipal.Grants);
        Assert.Empty(context.CallerPrincipal.Grants);
    }

    #endregion

    private static Task Run(IExecutionContext context, Requirement requirement) =>
        Pipeline.Chain(
            context,
            new AuthorizationFilter(requirement, beforeSerialization: false),
            new Pipeline.Recording([], "handler")).Next();
}
