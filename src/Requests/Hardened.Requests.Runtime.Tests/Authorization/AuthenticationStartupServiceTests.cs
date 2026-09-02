using Hardened.Requests.Abstract.Authorization;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Middleware;
using Hardened.Requests.Abstract.RequestFilter;
using Hardened.Requests.Runtime.Authorization;
using Hardened.Requests.Runtime.DependencyInjection;
using Hardened.Requests.Runtime.Tests.Support;
using Hardened.Shared.Runtime.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Authorization;

/// <summary>
/// Whether an application has authentication at all. A source the startup service does not find is
/// a source that never runs, and every caller stays anonymous with nothing said.
///
/// <para>
/// The startup service itself is <c>internal</c>, so it is reached the way an application reaches
/// it - through <c>HardenedRequestModule</c>'s registrations.
/// </para>
/// </summary>
public class AuthenticationStartupServiceTests {

    private sealed class BearerScheme : IAuthenticationScheme;

    private sealed class CookieScheme : IAuthenticationScheme;

    /// <summary>
    /// Records what it was asked, so a source that was collected and never reached is
    /// distinguishable from one that was not collected at all.
    /// </summary>
    private class RecordingSource : IPrincipalSource {
        private readonly string? _subject;

        protected RecordingSource(string? subject) => _subject = subject;

        public int Calls { get; private set; }

        public ValueTask<ICallerPrincipal?> Authenticate(IExecutionContext context) {
            Calls++;

            return new ValueTask<ICallerPrincipal?>(
                _subject == null ? null : new CallerPrincipal("test", subject: _subject));
        }
    }

    /// <summary>
    /// Written against the typed interface alone, which is what an application following the
    /// <c>[Authorize&lt;TScheme&gt;]</c> vocabulary writes.
    /// </summary>
    private sealed class BearerSource(string? subject = "ada")
        : RecordingSource(subject), IPrincipalSource<BearerScheme>;

    private sealed class CookieSource(string? subject = "grace")
        : RecordingSource(subject), IPrincipalSource<CookieScheme>;

    private sealed class PlainSource(string? subject = null) : RecordingSource(subject);

    private sealed class OpenSource<TScheme>(string? subject = null)
        : RecordingSource(subject), IPrincipalSource<TScheme>
        where TScheme : IAuthenticationScheme;

    /// <summary>
    /// Composes the module, lets <paramref name="register"/> state the application's sources, runs
    /// every startup service, and hands back the middleware that was installed.
    /// </summary>
    /// <remarks>
    /// The authorization startup service runs alongside and installs into the filter registry
    /// rather than the middleware service, so what comes back is authentication's alone. Its
    /// configuration is registered after the module so it wins the resolve, the way the CORS
    /// fixture does it - the module's own factory reads a configuration manager a test must not
    /// depend on.
    /// </remarks>
    private static async Task<IReadOnlyList<IExecutionFilter>> Installed(
        Action<IServiceCollection> register) {
        var services = new ServiceCollection();

        new HardenedRequestModule().ConfigureServices(services);

        register(services);

        var installed = new List<IExecutionFilter>();
        var middlewareService = Substitute.For<IMiddlewareService>();

        middlewareService
            .When(m => m.Use(Arg.Any<Func<IExecutionContext, IExecutionFilter>>()))
            .Do(call => installed.Add(
                call.Arg<Func<IExecutionContext, IExecutionFilter>>()(
                    Substitute.For<IExecutionContext>())));

        services.AddSingleton(middlewareService);
        services.AddSingleton(Substitute.For<IGlobalFilterRegistry>());
        services.AddSingleton<IOptions<IAuthorizationConfiguration>>(
            Options.Create(Substitute.For<IAuthorizationConfiguration>()));

        var provider = services.BuildServiceProvider();

        foreach (var startupService in provider.GetServices<IStartupService>()) {
            Assert.True(await startupService.Startup(provider));
        }

        return installed;
    }

    /// <summary>
    /// The caller a request comes out of the installed middleware with.
    /// </summary>
    private static async Task<string?> Caller(IExecutionFilter middleware) {
        var context = Pipeline.Context();
        var chain = Substitute.For<IExecutionChain>();

        chain.Context.Returns(context);
        chain.Next().Returns(Task.CompletedTask);

        await middleware.Execute(chain);

        return context.CallerPrincipal.Subject;
    }

    /// <summary>
    /// CS-01 and SU-04. Two arms registered a source by attribute, got the typed interface as its
    /// service type, and lost a debugging session to an application where every protected route
    /// answered 401 and nothing said why.
    /// </summary>
    [Fact]
    public async Task ATypedSourceInstallsTheMiddleware() {
        var installed = await Installed(
            services => services.AddSingleton<IPrincipalSource<BearerScheme>, BearerSource>());

        Assert.IsType<AuthenticationMiddleware>(Assert.Single(installed));
    }

    [Fact]
    public async Task ATypedSourceAuthenticatesTheRequest() {
        var installed = await Installed(
            services => services.AddSingleton<IPrincipalSource<BearerScheme>, BearerSource>());

        Assert.Equal("ada", await Caller(Assert.Single(installed)));
    }

    /// <summary>
    /// The workaround the arms found, which has to keep working.
    /// </summary>
    [Fact]
    public async Task ASourceRegisteredAsThePlainInterfaceStillInstallsTheMiddleware() {
        var installed = await Installed(
            services => services.AddSingleton<IPrincipalSource, BearerSource>());

        Assert.Single(installed);
    }

    /// <summary>
    /// Two schemes are two service types, and one closed generic is not the whole of what the
    /// application registered.
    /// </summary>
    [Fact]
    public async Task EverySchemeIsCollected() {
        var bearer = new BearerSource(subject: null);
        var cookie = new CookieSource();

        var installed = await Installed(services => {
            services.AddSingleton<IPrincipalSource<BearerScheme>>(bearer);
            services.AddSingleton<IPrincipalSource<CookieScheme>>(cookie);
        });

        Assert.Equal("grace", await Caller(Assert.Single(installed)));
        Assert.Equal(1, bearer.Calls);
        Assert.Equal(1, cookie.Calls);
    }

    /// <summary>
    /// First answer wins in registration order, and a plain source declining falls through to a
    /// typed one registered after it. The two forms are one ordered list, not two.
    /// </summary>
    [Fact]
    public async Task RegistrationOrderHoldsAcrossBothForms() {
        var installed = await Installed(services => {
            services.AddSingleton<IPrincipalSource, PlainSource>();
            services.AddSingleton<IPrincipalSource<CookieScheme>, CookieSource>();
        });

        Assert.Equal("grace", await Caller(Assert.Single(installed)));
    }

    /// <summary>
    /// One instance registered under both interfaces is asked once. Asking it twice would only
    /// cost a request a second read of a credential it has already declined.
    /// </summary>
    [Fact]
    public async Task ASourceRegisteredUnderBothInterfacesIsAskedOnce() {
        var source = new BearerSource(subject: null);

        var installed = await Installed(services => {
            services.AddSingleton<IPrincipalSource>(source);
            services.AddSingleton<IPrincipalSource<BearerScheme>>(source);
        });

        await Caller(Assert.Single(installed));

        Assert.Equal(1, source.Calls);
    }

    /// <summary>
    /// An application that registered nothing gets no middleware, which is what keeps the anonymous
    /// default free.
    /// </summary>
    [Fact]
    public async Task NoSourceInstallsNoMiddleware() {
        Assert.Empty(await Installed(_ => { }));
    }

    /// <summary>
    /// An open generic registration is passed over rather than resolved: nothing implements the
    /// source for every scheme, and asking the container for an unbound type throws.
    /// </summary>
    [Fact]
    public async Task AnOpenGenericRegistrationIsPassedOver() {
        Assert.Empty(await Installed(
            services => services.AddSingleton(typeof(IPrincipalSource<>), typeof(OpenSource<>))));
    }
}
