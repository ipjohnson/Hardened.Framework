using Hardened.Requests.Abstract.Authorization;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.RequestFilter;
using Hardened.Requests.Runtime.Authorization;
using Hardened.Requests.Runtime.Execution;
using Hardened.Requests.Runtime.Filters;
using Hardened.Requests.Runtime.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Authorization;

/// <summary>
/// Requirements added while a handler is being built, rather than written on it.
///
/// <para>
/// Everything here goes through <c>ExecutionHelper</c> rather than calling the filter provider
/// directly, because the thing under test is <em>when</em> conventions run. They have to be applied
/// before the global filter registry is consulted - that is what asks for the handler's guard - and
/// a test that folded them itself would pass whatever the ordering.
/// </para>
/// </summary>
public class AuthorizationConventionTests {

    private class Controller;

    /// <summary>Requires a grant of everything below a path prefix.</summary>
    private sealed class PrefixConvention : IAuthorizationConvention {
        private readonly string _prefix;
        private readonly string _grant;

        public PrefixConvention(string prefix, string grant) {
            _prefix = prefix;
            _grant = grant;
        }

        public Requirement? Apply(IExecutionRequestHandlerInfo handlerInfo) =>
            handlerInfo.Path.StartsWith(_prefix) ? Requirement.Grant(_grant) : null;
    }

    #region what a convention contributes

    /// <summary>
    /// A handler that declared nothing is guarded by the convention alone.
    /// </summary>
    [Fact]
    public async Task AConventionGuardsAnUnannotatedHandler() {
        var conventions = new[] { new PrefixConvention("/admin", "admin:access") };

        Assert.True(await Admits("/admin/users", conventions, [], "admin:access"));
        Assert.False(await Admits("/admin/users", conventions, []));
    }

    /// <summary>
    /// And says nothing about a handler it does not match, which stays public.
    /// </summary>
    [Fact]
    public async Task AConventionLeavesHandlersItDoesNotMatchAlone() {
        var conventions = new[] { new PrefixConvention("/admin", "admin:access") };

        Assert.True(await Admits("/pets", conventions, []));
    }

    /// <summary>
    /// A convention conjoins with what the handler declared rather than replacing it, so the caller
    /// needs both.
    /// </summary>
    /// <remarks>
    /// The direction that matters. If a convention substituted, an attribute would be silently
    /// dropped on every route the convention covered - and the handler would read as guarded by one
    /// thing while being guarded by another.
    /// </remarks>
    [Fact]
    public async Task AConventionNarrowsAHandlerThatDeclaredItsOwnRequirement() {
        var conventions = new[] { new PrefixConvention("/admin", "admin:access") };
        object[] metadata = [new AuthorizeGrantsAttribute("pets:write")];

        Assert.True(await Admits("/admin/pets", conventions, metadata, "admin:access", "pets:write"));

        Assert.False(await Admits("/admin/pets", conventions, metadata, "admin:access"));
        Assert.False(await Admits("/admin/pets", conventions, metadata, "pets:write"));
    }

    /// <summary>
    /// Several conventions all apply, for the same reason several attributes do.
    /// </summary>
    [Fact]
    public async Task EveryConventionThatMatchesApplies() {
        var conventions = new[] {
            new PrefixConvention("/admin", "admin:access"),
            new PrefixConvention("/admin/billing", "billing:read"),
        };

        Assert.True(await Admits("/admin/billing", conventions, [], "admin:access", "billing:read"));
        Assert.False(await Admits("/admin/billing", conventions, [], "admin:access"));
    }

    /// <summary>
    /// <c>[AllowAnonymous]</c> still wins, so a route that reads as public in the source is public.
    /// </summary>
    /// <remarks>
    /// The one case where a local statement beats a convention, and it is the same reasoning that
    /// makes it beat an attribute: the alternative is a handler somebody deliberately opened
    /// refusing in production because of a rule written somewhere else.
    /// </remarks>
    [Fact]
    public async Task AllowAnonymousWinsOverAConvention() {
        var conventions = new[] { new PrefixConvention("/admin", "admin:access") };

        Assert.True(await Admits("/admin/health", conventions, [new AllowAnonymousAttribute()]));
    }

    #endregion

    #region what the handler ends up holding

    /// <summary>
    /// The handler reports the requirement it actually enforces, not the one it declared.
    /// </summary>
    /// <remarks>
    /// <see cref="BaseExecutionHandler{TController}"/> takes its info from the setup the chain was
    /// built from for exactly this reason. If it returned the static field the generator emitted,
    /// the filter would enforce something <see cref="IExecutionContext.HandlerInfo"/> never
    /// mentioned - and every consumer of that property, the document included, would be describing a
    /// different application from the one running.
    /// </remarks>
    [Fact]
    public void TheHandlerCarriesTheAmendedRequirement() {
        var declared = new ExecutionRequestHandlerInfo(
            "/admin/users", "GET", typeof(Controller), "List", null,
            [new AuthorizeGrantsAttribute("users:read")]);

        var setup = Setup(declared, [new PrefixConvention("/admin", "admin:access")]);

        var grants = setup.HandlerInfo.Requirement!.RequiredGrants.ToArray();

        Assert.Contains("users:read", grants);
        Assert.Contains("admin:access", grants);
    }

    /// <summary>
    /// A handler no convention spoke about keeps the instance the generator built.
    /// </summary>
    [Fact]
    public void AHandlerNoConventionMatchesIsNotRebuilt() {
        var declared = new ExecutionRequestHandlerInfo(
            "/pets", "GET", typeof(Controller), "List");

        var setup = Setup(declared, [new PrefixConvention("/admin", "admin:access")]);

        Assert.Same(declared, setup.HandlerInfo);
    }

    /// <summary>
    /// An application registering no conventions builds handlers as before.
    /// </summary>
    /// <remarks>
    /// Worth its own test because the natural spelling of "resolve them all" -
    /// <c>GetServices&lt;T&gt;</c> - asks the container for <c>IEnumerable&lt;T&gt;</c> as a
    /// required service, and this one does not synthesise an empty one. That turned the common case
    /// into a failure to construct any handler at all.
    /// </remarks>
    [Fact]
    public void NoConventionsRegisteredIsNotAnError() {
        var declared = new ExecutionRequestHandlerInfo("/pets", "GET", typeof(Controller), "List");

        Assert.Same(declared, Setup(declared, []).HandlerInfo);
    }

    #endregion

    /// <summary>
    /// Builds a handler's chain the way a generated handler does, through the real helper.
    /// </summary>
    private static ExecutionHandlerSetup Setup(
        IExecutionRequestHandlerInfo declared, IAuthorizationConvention[] conventions) {
        var context = Pipeline.Context(configureServices: services => {
            Register(services, conventions);
            services.AddSingleton<IGlobalFilterRegistry>(
                new GlobalFilterRegistry(Array.Empty<IRequestFilterProvider>()));
        });

        return ExecutionHelper.StandardFilterEmptyParameters<Controller>(
            context.RequestServices,
            declared,
            (_, _) => { },
            Array.Empty<IRequestFilterProvider>());
    }

    /// <summary>
    /// The services <c>ExecutionHelper</c> resolves while assembling a chain.
    /// </summary>
    /// <remarks>
    /// The two filter providers are substitutes because neither is on trial here - one serialises
    /// and one creates the controller, and both would drag a serializer and a container into a test
    /// about when conventions run. Everything the assertions actually depend on is the production
    /// type.
    /// </remarks>
    private static void Register(
        IServiceCollection services, IAuthorizationConvention[] conventions) {
        var ioProvider = Substitute.For<IIOFilterProvider>();
        ioProvider.ProvideFilter(
                Arg.Any<IExecutionRequestHandlerInfo>(),
                Arg.Any<Func<IExecutionContext, Task<IExecutionRequestParameters>>>())
            .Returns(new Pipeline.Recording([], "io"));

        var instanceProvider = Substitute.For<IInstanceFilterProvider>();
        instanceProvider.ProvideFilter<Controller>(Arg.Any<IServiceProvider>())
            .Returns(new Pipeline.Recording([], "instance"));

        services.AddSingleton(ioProvider);
        services.AddSingleton(instanceProvider);
        services.AddSingleton<IActivityAuthorizationService, ActivityAuthorizationService>();
        services.AddSingleton<IActivityAuthorizationHandler, PrincipalGrantAuthorizationHandler>();

        foreach (var convention in conventions) {
            services.AddSingleton<IAuthorizationConvention>(convention);
        }
    }

    /// <summary>
    /// Runs the chain the helper built and reports whether the request survived it.
    /// </summary>
    /// <remarks>
    /// Through the whole chain rather than the authorization filter alone, because the ordering is
    /// half of what is being asserted - the registry has to have been handed the amended handler for
    /// the guard to exist at all.
    /// </remarks>
    private static async Task<bool> Admits(
        string path,
        IAuthorizationConvention[] conventions,
        object[] metadata,
        params string[] grants) {
        var registry = new GlobalFilterRegistry(Array.Empty<IRequestFilterProvider>());
        registry.RegisterFilter(new AuthorizationFilterProvider(requireAuthorization: false).GetFilter);

        var context = Pipeline.Context(path: path, configureServices: services => {
            Register(services, conventions);
            services.AddSingleton<IGlobalFilterRegistry>(registry);
        });

        context.CallerPrincipal = new CallerPrincipal("bearer", grants);

        // The substituted IO filter continues the chain, so it reaches the invoke filter - which
        // reads the controller off the context rather than creating one.
        context.HandlerInstance = new Controller();

        var declared = new ExecutionRequestHandlerInfo(
            path, "GET", typeof(Controller), "Invoke", null, metadata);

        var setup = ExecutionHelper.StandardFilterEmptyParameters<Controller>(
            context.RequestServices, declared, (_, _) => { },
            Array.Empty<IRequestFilterProvider>());

        await new ExecutionChain(setup.Filters, context).Next();

        return context.Response.ExceptionValue == null;
    }
}
