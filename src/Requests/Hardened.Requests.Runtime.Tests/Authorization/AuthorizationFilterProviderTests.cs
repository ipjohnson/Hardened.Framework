using Hardened.Requests.Abstract.Authorization;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.RequestFilter;
using Hardened.Requests.Runtime.Authorization;
using Hardened.Requests.Runtime.Execution;
using Hardened.Requests.Runtime.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Authorization;

/// <summary>
/// What guards a handler, decided once from its attributes.
///
/// <para>
/// Everything here is a rule about the whole set of attributes rather than about any one of them,
/// which is why it lives in a provider the registry calls once per handler rather than in the
/// attributes themselves.
/// </para>
/// </summary>
public class AuthorizationFilterProviderTests {

    private class CanManagePets : AuthorizationPolicy {
        protected override Requirement Define() => Grant("pets:manage");
    }

    private class MustOwnTheResource : AuthorizationPolicy {
        protected override Requirement Define() => Predicate((_, _) => true, "owns it");
    }

    /// <summary>
    /// The hand-authored form: a grant named once, spelled as a type everywhere it is required.
    /// </summary>
    private sealed class RequiresPetWriteAttribute : AuthorizeGrantsAttribute {
        public RequiresPetWriteAttribute() : base("pets:read", "pets:write") { }
    }

    /// <summary>
    /// A real handler rather than a substitute.
    /// </summary>
    /// <remarks>
    /// The requirement is derived from metadata by the handler info itself, so a substitute has to
    /// be told the answer to the very thing under test - and NSubstitute auto-substitutes an
    /// unconfigured <c>Requirement</c>, which silently guards a handler the test declared nothing
    /// on. The real type costs nothing here and cannot disagree with what production builds.
    /// </remarks>
    private static IExecutionRequestHandlerInfo Handler(params object[] metadata) =>
        new ExecutionRequestHandlerInfo(
            "/orders", "GET", typeof(AuthorizationFilterProviderTests), "Handler", null, metadata);

    private static RequestFilterInfo? Filter(bool requireAuthorization, params object[] metadata) =>
        new AuthorizationFilterProvider(requireAuthorization).GetFilter(Handler(metadata));

    #region nothing to do

    /// <summary>
    /// The default posture. A handler carrying nothing is public, and costs no filter at all - so an
    /// application that has adopted none of this pays nothing per request.
    /// </summary>
    [Fact]
    public void AnUnannotatedHandlerGetsNoFilterByDefault() {
        Assert.Null(Filter(requireAuthorization: false));
    }

    [Fact]
    public void AllowAnonymousGetsNoFilter() {
        Assert.Null(Filter(requireAuthorization: false, new AllowAnonymousAttribute()));
    }

    #endregion

    #region the backstop

    /// <summary>
    /// Once the application has opted in, carrying no attribute is the thing being guarded against
    /// rather than a statement that the handler is public.
    /// </summary>
    [Fact]
    public void AnUnannotatedHandlerIsGuardedOnceAuthorizationIsRequired() {
        Assert.NotNull(Filter(requireAuthorization: true));
    }

    /// <summary>
    /// And the opt-out still works, which is what makes a login endpoint or a health check
    /// expressible.
    /// </summary>
    [Fact]
    public void AllowAnonymousOptsBackOutOfTheBackstop() {
        Assert.Null(Filter(requireAuthorization: true, new AllowAnonymousAttribute()));
    }

    /// <summary>
    /// The backstop requires authentication and nothing more, so it settles before a body is read
    /// rather than after parameters are bound.
    /// </summary>
    [Fact]
    public void TheBackstopRunsAheadOfSerialization() {
        Assert.True(Filter(requireAuthorization: true)!.Order < FilterOrder.Serialization);
    }

    #endregion

    #region folding attributes

    [Fact]
    public void APolicyAttributeProducesAFilter() {
        Assert.NotNull(Filter(false, new AuthorizeAttribute<CanManagePets>()));
    }

    /// <summary>
    /// The opt-out wins over a requirement written on the same handler. It is the more explicit
    /// statement - somebody wrote it there on purpose - and the alternative is a route that reads as
    /// public in the source and refuses in production.
    /// </summary>
    [Fact]
    public void AllowAnonymousWinsOverARequirementOnTheSameHandler() {
        Assert.Null(Filter(false, new AuthorizeAttribute<CanManagePets>(), new AllowAnonymousAttribute()));
    }

    /// <summary>
    /// Repeated <c>[AuthorizeGrants]</c> is <em>and</em>, like every other pair of authorization
    /// attributes. Writing a second one can only narrow what is admitted.
    /// </summary>
    /// <remarks>
    /// This is the rule the whole design rests on. Under the alternative - repeating means
    /// <em>or</em> - the second attribute here would admit a caller holding only <c>admin:*</c>,
    /// which means adding a line to a handler granted access rather than restricting it. That makes
    /// a method-level attribute weaken its controller's, a derived attribute weaken its base, and a
    /// convention weaken every handler it touched.
    /// </remarks>
    [Fact]
    public async Task RepeatedGrantAttributesMustAllHold() {
        object[] metadata = [
            new AuthorizeGrantsAttribute("pets:read", "pets:write"),
            new AuthorizeGrantsAttribute("admin:*"),
        ];

        Assert.True(await Admits(metadata, "pets:read", "pets:write", "admin:*"));

        Assert.False(await Admits(metadata, "pets:read", "pets:write"));
        Assert.False(await Admits(metadata, "admin:*"));
        Assert.False(await Admits(metadata));
    }

    /// <summary>
    /// An attribute deriving from <c>[AuthorizeGrants]</c> is honoured as itself, and conjoins with
    /// everything else exactly like the attribute it derives from.
    /// </summary>
    /// <remarks>
    /// The reason the base attribute is not sealed. Naming a grant once and spelling it as a type
    /// everywhere is the hand-authored form, and it only works if the pipeline recognises the
    /// derived type - which it does by interface, not by name.
    /// </remarks>
    [Fact]
    public async Task ADerivedGrantAttributeIsHonoured() {
        object[] metadata = [new RequiresPetWriteAttribute()];

        Assert.True(await Admits(metadata, "pets:read", "pets:write"));
        Assert.False(await Admits(metadata, "pets:read"));
    }

    /// <summary>
    /// A controller's attribute and a method's conjoin, because a handler's metadata carries both
    /// and both are requirements like any other.
    /// </summary>
    /// <remarks>
    /// The case the old rule got backwards: a controller-wide guard plus a method-level one meant
    /// either sufficed, so writing a narrower attribute on one route opened it up.
    /// </remarks>
    [Fact]
    public async Task AControllerAndAMethodRequirementBothApply() {
        object[] metadata = [
            new AuthorizeGrantsAttribute("tenant:member"),  // written on the controller
            new RequiresPetWriteAttribute(),                // written on the method
        ];

        Assert.True(await Admits(metadata, "tenant:member", "pets:read", "pets:write"));
        Assert.False(await Admits(metadata, "pets:read", "pets:write"));
        Assert.False(await Admits(metadata, "tenant:member"));
    }

    /// <summary>
    /// Grants within one attribute are an <em>and</em>, which is what a single OpenAPI requirement
    /// object means.
    /// </summary>
    [Fact]
    public async Task GrantsWithinOneAttributeMustAllHold() {
        object[] metadata = [new AuthorizeGrantsAttribute("pets:read", "pets:write")];

        Assert.True(await Admits(metadata, "pets:read", "pets:write"));
        Assert.False(await Admits(metadata, "pets:read"));
    }

    /// <summary>
    /// A generated attribute alongside a hand-written policy requires both: the specification's
    /// answer, and the extra condition somebody added on top of it. It is the stricter reading of an
    /// ambiguous case, and it matches what stacking authorization attributes means elsewhere.
    /// </summary>
    [Fact]
    public async Task APolicyAndAGrantAttributeMustBothHold() {
        object[] metadata = [
            new AuthorizeAttribute<CanManagePets>(),
            new AuthorizeGrantsAttribute("pets:read"),
        ];

        Assert.True(await Admits(metadata, "pets:manage", "pets:read"));
        Assert.False(await Admits(metadata, "pets:manage"));
        Assert.False(await Admits(metadata, "pets:read"));
    }

    #endregion

    #region position

    /// <summary>
    /// A requirement over grants alone is settled before the body is read, which is the whole reason
    /// there are two positions.
    /// </summary>
    [Fact]
    public void AGrantOnlyRequirementRunsAheadOfSerialization() {
        var filter = Filter(false, new AuthorizeGrantsAttribute("pets:read"))!;

        Assert.Equal(FilterOrder.Authentication + 1, filter.Order);
        Assert.True(filter.Order < FilterOrder.Serialization);
    }

    /// <summary>
    /// One reading bound parameters cannot run before they exist.
    /// </summary>
    [Fact]
    public void ARequirementOverTheRequestRunsAfterParametersAreBound() {
        var filter = Filter(false, new AuthorizeAttribute<MustOwnTheResource>())!;

        Assert.Equal(FilterOrder.Authorization, filter.Order);
        Assert.True(filter.Order > FilterOrder.Validation);
    }

    /// <summary>
    /// One predicate anywhere moves the whole thing late, including a requirement that is mostly
    /// grants. Running it early would evaluate the predicate before parameters are bound, which is
    /// the case it exists to serve.
    /// </summary>
    [Fact]
    public void MixingAPredicateIntoGrantsMovesTheWholeRequirementLate() {
        var filter = Filter(
            false,
            new AuthorizeGrantsAttribute("pets:read"),
            new AuthorizeAttribute<MustOwnTheResource>())!;

        Assert.Equal(FilterOrder.Authorization, filter.Order);
    }

    #endregion

    /// <summary>
    /// Runs the filter the provider actually built and reports whether it refused.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The folded requirement is not exposed anywhere, deliberately, so the only honest way to ask
    /// what the provider decided is to run what it produced. Rebuilding the fold in the test would
    /// assert that the test agrees with itself.
    /// </para>
    /// <para>
    /// The refusal is read off the response rather than from whether the next filter ran, because a
    /// filter placed ahead of the serializer continues the chain even when it refuses - it has to,
    /// or nothing writes the response. In production the serializing filter is what stops the
    /// handler; here there is no serializer, so the response is the signal.
    /// </para>
    /// </remarks>
    private static async Task<bool> Admits(object[] metadata, params string[] grants) {
        var filterInfo = new AuthorizationFilterProvider(requireAuthorization: false)
            .GetFilter(Handler(metadata));

        if (filterInfo == null) {
            return true;
        }

        var context = Pipeline.Context(configureServices: services => {
            services.AddSingleton<IActivityAuthorizationService, ActivityAuthorizationService>();
            services.AddSingleton<IActivityAuthorizationHandler, PrincipalGrantAuthorizationHandler>();
        });

        context.CallerPrincipal = new CallerPrincipal("bearer", grants);

        await Pipeline.Chain(
            context,
            filterInfo.FilterFunc(context),
            new Pipeline.Recording([], "handler")).Next();

        return context.Response.ExceptionValue == null;
    }
}
