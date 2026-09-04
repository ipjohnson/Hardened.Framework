using DependencyModules.Runtime.Interfaces;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.RequestFilter;
using Hardened.Requests.Abstract.Timeouts;
using Hardened.Requests.Runtime.DependencyInjection;
using Hardened.Requests.Runtime.Execution;
using Hardened.Requests.Runtime.Filters;
using Hardened.Requests.Runtime.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Filters;

/// <summary>
/// Which of the four places a deadline can be declared decides a handler's budget, and what a
/// convention may do to it.
/// </summary>
/// <remarks>
/// <para>
/// Composed through the real <c>ExecutionHelper</c> rather than by calling the resolver, because
/// the resolution and the amendment are one behaviour: the budget the filter enforces and the one
/// <c>IExecutionRequestHandlerInfo.Timeout</c> reports have to be the same value, and a test that
/// called the resolver alone could not see them diverge. That divergence is the whole reason the
/// timeout is first-class data rather than something each reader derives from metadata.
/// </para>
/// <para>
/// The assembly rung is asserted in <c>TimeoutTests</c> in the web integration suite, where there
/// is a real assembly to hang an attribute on.
/// </para>
/// </remarks>
public class TimeoutCascadeTests {

    private class Controller { }

    private sealed class IoStandIn : IExecutionFilter {
        public Task Execute(IExecutionChain chain) => chain.Next();
    }

    private sealed class InstanceStandIn : IExecutionFilter {
        public Task Execute(IExecutionChain chain) => chain.Next();
    }

    /// <summary>Bounds everything, so a handler that declared nothing still gets a budget.</summary>
    private sealed class EverythingIsFast : IRequestTimeoutConvention {
        private readonly int _milliseconds;

        public EverythingIsFast(int milliseconds) {
            _milliseconds = milliseconds;
        }

        public TimeoutPolicy? Apply(IExecutionRequestHandlerInfo handlerInfo) =>
            new(_milliseconds);
    }

    private sealed class SaysNothing : IRequestTimeoutConvention {
        public TimeoutPolicy? Apply(IExecutionRequestHandlerInfo handlerInfo) => null;
    }

    /// <summary>
    /// Composes the real filter array and hands back the handler as it ended up.
    /// </summary>
    private static ExecutionHandlerSetup Compose(
        object[]? metadata = null,
        Action<ServiceCollection>? configureServices = null) {
        var ioProvider = Substitute.For<IIOFilterProvider>();
        ioProvider.ProvideFilter(
                Arg.Any<IExecutionRequestHandlerInfo>(),
                Arg.Any<Func<IExecutionContext, Task<IExecutionRequestParameters>>>())
            .Returns(new IoStandIn());

        var instanceProvider = Substitute.For<IInstanceFilterProvider>();
        instanceProvider.ProvideFilter<Controller>(Arg.Any<IServiceProvider>())
            .Returns(new InstanceStandIn());

        var context = Pipeline.Context(configureServices: services => {
            services.AddSingleton<IGlobalFilterRegistry>(
                new GlobalFilterRegistry(Array.Empty<IRequestFilterProvider>()));
            services.AddSingleton(ioProvider);
            services.AddSingleton(instanceProvider);

            configureServices?.Invoke(services);
        });

        var handlerInfo = new ExecutionRequestHandlerInfo(
            "/orders", "GET", typeof(Controller), "Read", metadata: metadata);

        return ExecutionHelper.StandardFilterEmptyParameters<Controller>(
            context.RequestServices, handlerInfo, (_, _) => { }, []);
    }

    private static int? Budget(ExecutionHandlerSetup setup) => setup.HandlerInfo.Timeout?.Milliseconds;

    // ------------------------------------------------------------------ nothing declared

    /// <summary>
    /// An application that declares nothing is bounded by nothing, and pays for no timer. This is
    /// the rule the whole feature is opt-in for.
    /// </summary>
    [Fact]
    public void AHandlerNothingDeclaresIsNotBounded() {
        var setup = Compose();

        Assert.Null(setup.HandlerInfo.Timeout);
        Assert.DoesNotContain(setup.Filters, filter => filter(null!) is TimeoutFilter);
    }

    [Fact]
    public void ADeclaredBudgetInstallsExactlyOneFilter() {
        var setup = Compose([new TimeoutAttribute { Milliseconds = 2000 }]);

        Assert.Single(setup.Filters, filter => filter(null!) is TimeoutFilter);
    }

    // ------------------------------------------------------------------ the rungs

    [Fact]
    public void TheOperationsOwnDeclarationIsTheBudget() {
        Assert.Equal(2000, Budget(Compose([new TimeoutAttribute { Milliseconds = 2000 }])));
    }

    /// <summary>
    /// The generator emits a method's own attributes ahead of its class's, and the first is taken -
    /// so a method decides, whether it is tightening its class's number or loosening it.
    /// </summary>
    /// <remarks>
    /// Loosening is the case worth pinning. A tightest-wins rule would pass every other test in
    /// this file and quietly make this one impossible to express.
    /// </remarks>
    [Fact]
    public void AnOperationBeatsItsClassEvenWhenItLoosens() {
        var setup = Compose([
            new TimeoutAttribute { Milliseconds = 60_000 },  // the method
            new TimeoutAttribute { Milliseconds = 100 }      // its class
        ]);

        Assert.Equal(60_000, Budget(setup));
    }

    /// <summary>
    /// The entry point's default is the outermost rung, so anything the handler carries beats it.
    /// </summary>
    [Fact]
    public void ADeclarationBeatsTheEntryPointsDefault() {
        var setup = Compose(
            [new TimeoutAttribute { Milliseconds = 2000 }],
            services => new RequestTimeouts(30_000).ConfigureServices(services));

        Assert.Equal(2000, Budget(setup));
    }

    [Fact]
    public void TheEntryPointsDefaultBoundsAHandlerThatDeclaredNothing() {
        var setup = Compose(
            configureServices: services => new RequestTimeouts(5000).ConfigureServices(services));

        Assert.Equal(5000, Budget(setup));
    }

    /// <summary>
    /// Both spellings of the module reach the container as separate registrations, because
    /// <c>[Enable&lt;RequestTimeouts&gt;]</c> and <c>[RequestTimeouts(n)]</c> are separate
    /// <c>LoadModules</c> passes and module equality cannot collapse them. Taking the tighter makes
    /// an application that wrote both a defined answer rather than whichever the container returned
    /// last.
    /// </summary>
    [Fact]
    public void TwoEntryPointDefaultsResolveToTheTighter() {
        var setup = Compose(configureServices: services => {
            new RequestTimeouts().ConfigureServices(services);
            new RequestTimeouts(5000).ConfigureServices(services);
        });

        Assert.Equal(5000, Budget(setup));
    }

    // ------------------------------------------------------------------ conventions

    [Fact]
    public void AConventionBoundsAHandlerThatDeclaredNothing() {
        var setup = Compose(configureServices: services =>
            services.AddSingleton<IRequestTimeoutConvention>(new EverythingIsFast(2000)));

        Assert.Equal(2000, Budget(setup));
    }

    [Fact]
    public void AConventionTightensADeclarationThatWasTooLoose() {
        var setup = Compose(
            [new TimeoutAttribute { Milliseconds = 60_000 }],
            services => services.AddSingleton<IRequestTimeoutConvention>(new EverythingIsFast(2000)));

        Assert.Equal(2000, Budget(setup));
    }

    /// <summary>
    /// The rule that makes a convention safe to leave registered. Loosening is the one direction
    /// where a rule written far from the handler is likelier to be wrong than the handler is, so an
    /// operation that asked for two seconds cannot be handed a minute by something it cannot see.
    /// </summary>
    [Fact]
    public void AConventionCannotLoosenADeclaration() {
        var setup = Compose(
            [new TimeoutAttribute { Milliseconds = 2000 }],
            services => services.AddSingleton<IRequestTimeoutConvention>(new EverythingIsFast(60_000)));

        Assert.Equal(2000, Budget(setup));
    }

    [Fact]
    public void AConventionWithNothingToSayLeavesTheBudgetAlone() {
        var setup = Compose(
            [new TimeoutAttribute { Milliseconds = 2000 }],
            services => services.AddSingleton<IRequestTimeoutConvention>(new SaysNothing()));

        Assert.Equal(2000, Budget(setup));
    }

    [Fact]
    public void AConventionThatSaysNothingAboutAnUnboundedHandlerLeavesItUnbounded() {
        var setup = Compose(
            configureServices: services =>
                services.AddSingleton<IRequestTimeoutConvention>(new SaysNothing()));

        Assert.Null(setup.HandlerInfo.Timeout);
    }

    // ------------------------------------------------------------------ the guard

    /// <summary>
    /// A zero refuses every request the moment it is deployed and a negative one throws from
    /// <c>CancelAfter</c> on the first request. Both fail as the chain is composed instead, naming
    /// the handler and the rung, which is once at startup rather than once a request.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ABudgetThatCannotMeanAnythingFailsNamingItsHandler(int milliseconds) {
        var failure = Assert.Throws<InvalidOperationException>(
            () => Compose([new TimeoutAttribute { Milliseconds = milliseconds }]));

        Assert.Contains("GET /orders", failure.Message);
        Assert.Contains("operation or its class", failure.Message);
    }

    [Fact]
    public void ABadBudgetFromAConventionNamesTheConvention() {
        var failure = Assert.Throws<InvalidOperationException>(
            () => Compose(
                configureServices: services =>
                    services.AddSingleton<IRequestTimeoutConvention>(new EverythingIsFast(0))));

        Assert.Contains(nameof(EverythingIsFast), failure.Message);
    }

    // ------------------------------------------------------------------ the module's two spellings

    /// <summary>
    /// What <c>[Enable&lt;RequestTimeouts&gt;]</c> installs. The generator turns that attribute
    /// into <c>AddModule(new RequestTimeouts())</c>, so the parameterless constructor is the
    /// application-wide default and there is nowhere on <c>[Enable&lt;T&gt;]</c> for a number to
    /// ride.
    /// </summary>
    [Fact]
    public void EnableInstallsTheDefaultBudget() {
        Assert.Equal(TimeoutPolicy.DefaultMilliseconds, new RequestTimeouts().Milliseconds);
    }

    /// <summary>
    /// The other spelling, and the one that carries a number. DependencyModules generates the
    /// attribute from this module's greediest constructor, so the argument reaches the module
    /// untouched.
    ///
    /// <para>
    /// A settable <c>int Milliseconds</c> on the module would break this: the generated attribute
    /// would carry a property defaulting to <c>default(int)</c> and copy it over the constructor
    /// argument, because the null guard it is copied under is one a value type always passes.
    /// </para>
    /// </summary>
    [Fact]
    public void TheGeneratedAttributeCarriesTheNumberToTheModule() {
        var module = new RequestTimeoutsAttribute(5000).GetModule();

        Assert.Equal(5000, Assert.IsType<RequestTimeouts>(module).Milliseconds);
    }

    [Fact]
    public void TheGeneratedAttributeIsAModuleProvider() {
        Assert.IsAssignableFrom<IDependencyModuleProvider>(new RequestTimeoutsAttribute(5000));
    }

    [Fact]
    public void TwoInstallsOfTheSameBudgetAreOneInstall() {
        Assert.Equal(new RequestTimeouts(5000), new RequestTimeouts(5000));
        Assert.Equal(new RequestTimeouts(5000).GetHashCode(), new RequestTimeouts(5000).GetHashCode());
        Assert.NotEqual(new RequestTimeouts(5000), new RequestTimeouts(2000));
        Assert.NotEqual<object>(new RequestTimeouts(5000), new object());
    }

    /// <summary>
    /// The module registers the policy the cascade reads, and no global filter: the chain builder
    /// installs one filter per handler from whatever it resolved, so there is nothing to stand down
    /// for a handler that declared its own.
    /// </summary>
    [Fact]
    public void TheModuleRegistersAPolicyRatherThanAGlobalFilter() {
        var services = new ServiceCollection();

        new RequestTimeouts(5000).ConfigureServices(services);

        var provider = services.BuildServiceProvider();

        Assert.Equal(5000, Assert.Single(provider.GetServices<TimeoutPolicy>()).Milliseconds);
        Assert.Empty(provider.GetServices<IRequestFilterProvider>());
    }

    /// <summary>
    /// A linked source and a timer per request is a per-request cost, so an application that
    /// declares nothing is bounded by nothing.
    /// </summary>
    [Fact]
    public void TheRequestModuleBoundsNothingWithoutADeclaration() {
        var services = new ServiceCollection();

        new HardenedRequestModule().ConfigureServices(services);

        Assert.Empty(services.BuildServiceProvider().GetServices<TimeoutPolicy>());
    }
}
