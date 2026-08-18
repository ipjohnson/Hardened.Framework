using Hardened.Requests.Abstract.Authorization;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Runtime.Authorization;
using Hardened.Requests.Runtime.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Authorization;

/// <summary>
/// The caller a requirement sees on its second walk, after a contributor resolved grants.
/// </summary>
/// <remarks>
/// <para>
/// <c>AuthorizationFilterTests</c> covers what the filter <em>decides</em>, and covers it well. What
/// it never looks at is the principal handed to the requirement for the re-walk — a private
/// <c>ResolvedPrincipal</c> wrapping a private <c>UnionSet</c>. CI measured those fourteen lines at
/// zero while the filter as a whole read 58.8%: every member but <c>Contains</c> and the
/// constructor.
/// </para>
/// <para>
/// They are reachable, and they matter. A requirement is an abstract class a consumer may implement,
/// so anything it reads off the principal or asks of the grant set is a supported call — and
/// <c>UnionSet</c> is hand-written set algebra over two sets that may overlap. If <c>Subject</c>
/// came back null on the second walk, a requirement that reads it would see a different caller than
/// it saw on the first.
/// </para>
/// </remarks>
public class ResolvedPrincipalTests {

    /// <summary>Resolves the grants it was built with, so the re-walk has something to see.</summary>
    private sealed class ResolvingHandler : IActivityAuthorizationHandler {
        private readonly HashSet<string> _resolvable;

        public ResolvingHandler(params string[] resolvable) {
            _resolvable = new HashSet<string>(resolvable, StringComparer.Ordinal);
        }

        public ValueTask<GrantResolution> Resolve(
            IExecutionContext context, IReadOnlyList<string> grants) =>
            new(new GrantResolution(
                grants.Where(_resolvable.Contains).ToHashSet(StringComparer.Ordinal),
                AuthorizationDecision.Abstain));
    }

    /// <summary>
    /// Records the principal it is walked with, so a test can ask the resolved one questions.
    /// </summary>
    private sealed class CapturingRequirement : Requirement {
        private readonly string[] _grants;

        public CapturingRequirement(params string[] grants) {
            _grants = grants;
        }

        public ICallerPrincipal? LastWalkedWith { get; private set; }

        public int Walks { get; private set; }

        public override bool RequiresContext => false;

        public override IEnumerable<string> RequiredGrants => _grants;

        public override bool IsSatisfiedBy(ICallerPrincipal principal, IExecutionContext context) {
            Walks++;
            LastWalkedWith = principal;

            return _grants.All(principal.Grants.Contains);
        }
    }

    /// <summary>
    /// Runs the filter to the point where the requirement has been walked a second time, and hands
    /// back the principal it was walked with.
    /// </summary>
    private static async Task<ICallerPrincipal> Resolved(
        ICallerPrincipal caller, string[] required, params string[] resolvable) {
        var requirement = new CapturingRequirement(required);

        var context = Pipeline.Context(configureServices: services => {
            services.AddSingleton<IActivityAuthorizationService, ActivityAuthorizationService>();
            services.AddSingleton<IActivityAuthorizationHandler>(new ResolvingHandler(resolvable));
        });

        context.CallerPrincipal = caller;

        await Pipeline.Chain(
            context, new AuthorizationFilter(requirement, beforeSerialization: true)).Next();

        Assert.Equal(2, requirement.Walks);
        Assert.NotNull(requirement.LastWalkedWith);
        Assert.NotSame(caller, requirement.LastWalkedWith);

        return requirement.LastWalkedWith!;
    }

    private static ICallerPrincipal Holding(params string[] grants) =>
        new CallerPrincipal("bearer", grants, subject: "user-1", issuer: "https://issuer.test");

    private static Task<ICallerPrincipal> Standard() =>
        Resolved(Holding("pets:read"), ["pets:read", "pets:write"], "pets:write");

    #region the caller travels with the resolved grants

    /// <summary>
    /// The wrapper is the same caller. A requirement reading the subject on its second walk must
    /// not see a different one than it saw on its first.
    /// </summary>
    [Fact]
    public async Task TheResolvedPrincipalKeepsTheSubject() {
        Assert.Equal("user-1", (await Standard()).Subject);
    }

    [Fact]
    public async Task TheResolvedPrincipalKeepsTheIssuer() {
        Assert.Equal("https://issuer.test", (await Standard()).Issuer);
    }

    [Fact]
    public async Task TheResolvedPrincipalKeepsTheAuthenticationScheme() {
        Assert.Equal("bearer", (await Standard()).AuthenticationScheme);
    }

    [Fact]
    public async Task TheResolvedPrincipalIsStillAuthenticated() {
        Assert.True((await Standard()).IsAuthenticated);
    }

    [Fact]
    public async Task ClaimsStillResolveThroughTheWrapper() {
        var caller = new CallerPrincipal(
            "bearer", ["pets:read"], claims: new Dictionary<string, string> { ["tenant"] = "acme" });

        var resolved = await Resolved(caller, ["pets:read", "pets:write"], "pets:write");

        Assert.True(resolved.TryGetClaim("tenant", out var tenant));
        Assert.Equal("acme", tenant);
    }

    [Fact]
    public async Task AClaimTheCallerDoesNotHaveIsStillAbsent() {
        Assert.False((await Standard()).TryGetClaim("nope", out _));
    }

    #endregion

    #region the union of held and resolved grants

    [Fact]
    public async Task TheUnionContainsTheGrantTheCredentialCarried() {
        Assert.Contains("pets:read", (await Standard()).Grants);
    }

    [Fact]
    public async Task TheUnionContainsTheResolvedGrant() {
        Assert.Contains("pets:write", (await Standard()).Grants);
    }

    [Fact]
    public async Task TheUnionHoldsNothingElse() {
        Assert.DoesNotContain("pets:delete", (await Standard()).Grants);
    }

    /// <summary>
    /// Enumeration is what a requirement rendering a description walks, and the two sets may
    /// overlap — the same grant held and resolved must appear once.
    /// </summary>
    [Fact]
    public async Task EnumeratingTheUnionYieldsEachGrantOnce() {
        var resolved = await Resolved(
            Holding("pets:read"), ["pets:read", "pets:write"], "pets:read", "pets:write");

        Assert.Equal(
            ["pets:read", "pets:write"], resolved.Grants.OrderBy(grant => grant, StringComparer.Ordinal));
    }

    [Fact]
    public async Task TheCountDoesNotDoubleCountAnOverlap() {
        var resolved = await Resolved(
            Holding("pets:read"), ["pets:read", "pets:write"], "pets:read", "pets:write");

        Assert.Equal(2, resolved.Grants.Count);
    }

    [Fact]
    public async Task TheCountIsTheSizeOfTheUnion() {
        Assert.Equal(2, (await Standard()).Grants.Count);
    }

    #endregion

    #region set algebra

    [Fact]
    public async Task SetEqualsMatchesTheUnion() {
        var grants = (await Standard()).Grants;

        Assert.True(grants.SetEquals(["pets:read", "pets:write"]));
        Assert.False(grants.SetEquals(["pets:read"]));
    }

    [Fact]
    public async Task OverlapsFindsASharedGrant() {
        var grants = (await Standard()).Grants;

        Assert.True(grants.Overlaps(["pets:write", "pets:delete"]));
        Assert.False(grants.Overlaps(["pets:delete"]));
    }

    [Fact]
    public async Task IsSubsetOfComparesAgainstAWiderSet() {
        var grants = (await Standard()).Grants;

        Assert.True(grants.IsSubsetOf(["pets:read", "pets:write", "pets:delete"]));
        Assert.True(grants.IsSubsetOf(["pets:read", "pets:write"]));
        Assert.False(grants.IsSubsetOf(["pets:read"]));
    }

    [Fact]
    public async Task IsProperSubsetOfExcludesTheEqualCase() {
        var grants = (await Standard()).Grants;

        Assert.True(grants.IsProperSubsetOf(["pets:read", "pets:write", "pets:delete"]));
        Assert.False(grants.IsProperSubsetOf(["pets:read", "pets:write"]));
    }

    [Fact]
    public async Task IsSupersetOfComparesAgainstANarrowerSet() {
        var grants = (await Standard()).Grants;

        Assert.True(grants.IsSupersetOf(["pets:read"]));
        Assert.True(grants.IsSupersetOf(["pets:read", "pets:write"]));
        Assert.False(grants.IsSupersetOf(["pets:read", "pets:delete"]));
    }

    [Fact]
    public async Task IsProperSupersetOfExcludesTheEqualCase() {
        var grants = (await Standard()).Grants;

        Assert.True(grants.IsProperSupersetOf(["pets:read"]));
        Assert.False(grants.IsProperSupersetOf(["pets:read", "pets:write"]));
    }

    /// <summary>
    /// Grants are compared ordinally everywhere else, and the union has to agree — a set that
    /// matched case-insensitively would admit a caller holding <c>PETS:READ</c>.
    /// </summary>
    [Fact]
    public async Task TheUnionComparesGrantsOrdinally() {
        var grants = (await Standard()).Grants;

        Assert.False(grants.Contains("PETS:READ"));
        Assert.False(grants.Contains("Pets:Write"));
    }

    #endregion

    /// <summary>
    /// The resolved grants exist to answer one requirement. Leaving them on the context would
    /// silently widen every later check in the request — already asserted by
    /// <c>AuthorizationFilterTests.ResolvedGrantsDoNotStayOnThePrincipal</c>; this is the other
    /// half, that the wrapper handed to the re-walk is not the context's principal.
    /// </summary>
    [Fact]
    public async Task TheWrapperIsNotInstalledOnTheContext() {
        var caller = Holding("pets:read");
        var requirement = new CapturingRequirement("pets:read", "pets:write");

        var context = Pipeline.Context(configureServices: services => {
            services.AddSingleton<IActivityAuthorizationService, ActivityAuthorizationService>();
            services.AddSingleton<IActivityAuthorizationHandler>(new ResolvingHandler("pets:write"));
        });

        context.CallerPrincipal = caller;

        await Pipeline.Chain(
            context, new AuthorizationFilter(requirement, beforeSerialization: true)).Next();

        Assert.Same(caller, context.CallerPrincipal);
        Assert.NotSame(caller, requirement.LastWalkedWith);
    }
}
