using Hardened.Requests.Abstract.Authorization;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Runtime.Authorization;
using Hardened.Requests.Runtime.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Authorization;

/// <summary>
/// The service that composes contributors, and the contributor that ships with it.
/// </summary>
public class AuthorizationServiceTests {

    /// <summary>Answers whatever it was told to, and records that it was asked.</summary>
    private sealed class StubHandler : IActivityAuthorizationHandler {
        private readonly GrantResolution _resolution;

        public StubHandler(AuthorizationDecision decision)
            : this(new GrantResolution(new HashSet<string>(), decision)) { }

        public StubHandler(GrantResolution resolution) {
            _resolution = resolution;
        }

        public int Calls { get; private set; }

        public IReadOnlyList<string>? LastAsked { get; private set; }

        public ValueTask<GrantResolution> Resolve(
            IExecutionContext context, IReadOnlyList<string> grants) {
            Calls++;
            LastAsked = grants;

            return new ValueTask<GrantResolution>(_resolution);
        }
    }

    private static IExecutionContext ContextWith(params IActivityAuthorizationHandler[] handlers) =>
        Pipeline.Context(configureServices: services => {
            foreach (var handler in handlers) {
                services.AddSingleton(handler);
            }
        });

    private static IExecutionContext ContextHolding(params string[] grants) {
        var context = Pipeline.Context(configureServices: services =>
            services.AddSingleton<IActivityAuthorizationHandler, PrincipalGrantAuthorizationHandler>());

        context.CallerPrincipal = new CallerPrincipal("bearer", grants);

        return context;
    }

    #region the default contributor

    [Fact]
    public async Task PrincipalGrants_VouchesForEveryGrantTheCredentialCarries() {
        var context = ContextHolding("pets:read", "pets:write");

        var resolution = await new PrincipalGrantAuthorizationHandler()
            .Resolve(context, ["pets:read", "pets:write"]);

        Assert.Equal(["pets:read", "pets:write"], resolution.Granted.Order());
        Assert.Equal(AuthorizationDecision.Abstain, resolution.Decision);
    }

    /// <summary>
    /// It leaves out what it does not find rather than refusing, and that distinction is the whole
    /// reason a resolution carries a set. "Not in the credential" is not "the caller does not have
    /// it" - the next handler may resolve it from a permissions table, and a refusal here would
    /// outrank that and make every resolver useless.
    /// </summary>
    [Fact]
    public async Task PrincipalGrants_OmitsWhatItDoesNotFindRatherThanRefusing() {
        var context = ContextHolding("pets:read");

        var resolution = await new PrincipalGrantAuthorizationHandler()
            .Resolve(context, ["pets:read", "pets:write"]);

        Assert.Equal(["pets:read"], resolution.Granted);
        Assert.Equal(AuthorizationDecision.Abstain, resolution.Decision);
    }

    /// <summary>
    /// Answering a partial subset is the point: a requirement of <c>read | write</c> is satisfied by
    /// the one grant that came back, and a contributor that had to answer yes or no could not say so.
    /// </summary>
    [Fact]
    public async Task PrincipalGrants_AnswersAboutSeveralGrantsInOneCall() {
        var context = ContextHolding("b");

        var resolution = await new PrincipalGrantAuthorizationHandler()
            .Resolve(context, ["a", "b", "c"]);

        Assert.Equal(["b"], resolution.Granted);
    }

    [Fact]
    public async Task PrincipalGrants_VouchesForNothingForAnAnonymousCaller() {
        var resolution = await new PrincipalGrantAuthorizationHandler()
            .Resolve(Pipeline.Context(), ["pets:read"]);

        Assert.Empty(resolution.Granted);
    }

    [Fact]
    public async Task PrincipalGrants_NeverVouchesForAGrantItWasNotAskedAbout() {
        var context = ContextHolding("pets:read", "admin:*");

        var resolution = await new PrincipalGrantAuthorizationHandler()
            .Resolve(context, ["pets:read"]);

        Assert.Equal(["pets:read"], resolution.Granted);
        Assert.DoesNotContain("admin:*", resolution.Granted);
    }

    #endregion

    #region the imperative form

    [Fact]
    public async Task Authorize_PermitsWhenEveryGrantIsHeld() {
        var context = ContextHolding("pets:read", "pets:write");

        Assert.Equal(
            AuthorizationDecision.Allow,
            await new ActivityAuthorizationService().Authorize(context, "pets:read", "pets:write"));
    }

    /// <summary>
    /// A conjunction: this asks "may the caller do this specific thing", so every grant named is
    /// part of it and holding some of them is not an answer.
    /// </summary>
    [Fact]
    public async Task Authorize_DoesNotPermitOnAPartialMatch() {
        var context = ContextHolding("pets:read");

        Assert.Equal(
            AuthorizationDecision.Abstain,
            await new ActivityAuthorizationService().Authorize(context, "pets:read", "pets:write"));
    }

    /// <summary>
    /// Nothing was asked, so there is nothing to affirm. Reading an empty question as "all zero
    /// grants are held, therefore allow" would turn it into a permit.
    /// </summary>
    [Fact]
    public async Task Authorize_AbstainsWhenAskedAboutNothing() {
        var context = ContextHolding("pets:read");

        Assert.Equal(
            AuthorizationDecision.Abstain,
            await new ActivityAuthorizationService().Authorize(context));
    }

    /// <summary>
    /// A verdict stands whatever the grants say, so a contributor demanding a stronger credential is
    /// not overridden by one that vouched for the grant.
    /// </summary>
    [Fact]
    public async Task Authorize_LetsAVerdictOverrideHeldGrants() {
        var context = Pipeline.Context(configureServices: services => {
            services.AddSingleton<IActivityAuthorizationHandler>(
                new StubHandler(GrantResolution.Granting("pets:read")));
            services.AddSingleton<IActivityAuthorizationHandler>(
                new StubHandler(AuthorizationDecision.DenyInsufficientAuthentication));
        });

        Assert.Equal(
            AuthorizationDecision.DenyInsufficientAuthentication,
            await new ActivityAuthorizationService().Authorize(context, "pets:read"));
    }

    #endregion

    #region composing contributors

    /// <summary>
    /// No contributors registered has to reach the same answer as every contributor abstaining,
    /// because they are the same observable state - and neither permits.
    /// </summary>
    [Fact]
    public async Task NoHandlersRegisteredAbstains() {
        var decision = await new ActivityAuthorizationService()
            .Authorize(Pipeline.Context(), "pets:read");

        Assert.Equal(AuthorizationDecision.Abstain, decision);
        Assert.False(decision.Permits());
    }

    [Fact]
    public async Task OneAllowAmongAbstentionsPermits() {
        var context = ContextWith(
            new StubHandler(AuthorizationDecision.Abstain),
            new StubHandler(AuthorizationDecision.Allow));

        Assert.True((await new ActivityAuthorizationService().Authorize(context, "g")).Permits());
    }

    [Fact]
    public async Task OneDenyOverridesEveryAllow() {
        var context = ContextWith(
            new StubHandler(AuthorizationDecision.Allow),
            new StubHandler(AuthorizationDecision.Deny),
            new StubHandler(AuthorizationDecision.Allow));

        Assert.Equal(
            AuthorizationDecision.Deny,
            await new ActivityAuthorizationService().Authorize(context, "g"));
    }

    [Fact]
    public async Task AStepUpSurvivesAnAllow() {
        var context = ContextWith(
            new StubHandler(AuthorizationDecision.Allow),
            new StubHandler(AuthorizationDecision.DenyInsufficientAuthentication));

        Assert.Equal(
            AuthorizationDecision.DenyInsufficientAuthentication,
            await new ActivityAuthorizationService().Authorize(context, "g"));
    }

    /// <summary>
    /// A deny settles it, so contributors behind it are not asked at all. Intentional: the next one
    /// along may be a database round trip or a call to an entitlement service, and no answer it
    /// could give would change the result.
    /// </summary>
    [Fact]
    public async Task ADenyStopsLaterContributorsBeingAsked() {
        var denying = new StubHandler(AuthorizationDecision.Deny);
        var behind = new StubHandler(AuthorizationDecision.Allow);

        await new ActivityAuthorizationService().Authorize(ContextWith(denying, behind), "g");

        Assert.Equal(1, denying.Calls);
        Assert.Equal(0, behind.Calls);
    }

    /// <summary>
    /// A step-up does not stop the walk, because a plain deny still outranks it and a contributor
    /// further along may yet produce one.
    /// </summary>
    [Fact]
    public async Task AStepUpDoesNotStopLaterContributorsBeingAsked() {
        var stepUp = new StubHandler(AuthorizationDecision.DenyInsufficientAuthentication);
        var behind = new StubHandler(AuthorizationDecision.Deny);

        var decision = await new ActivityAuthorizationService().Authorize(ContextWith(stepUp, behind), "g");

        Assert.Equal(1, behind.Calls);
        Assert.Equal(AuthorizationDecision.Deny, decision);
    }

    #endregion
}
