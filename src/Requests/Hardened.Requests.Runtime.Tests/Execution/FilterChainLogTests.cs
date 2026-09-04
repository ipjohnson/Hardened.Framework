using System.Reflection.Emit;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.RequestFilter;
using Hardened.Requests.Runtime.Execution;
using Hardened.Requests.Runtime.Filters;
using Hardened.Requests.Runtime.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Execution;

/// <summary>
/// The one line that shows what a handler's chain was composed into.
///
/// <para>
/// Until it existed the only way to see the assembled chain was to throw inside a filter and read
/// the stack, which is how the 0.19.0-rc1000 trial found a filter at a position that read right
/// and landed wrong. The line is written once, as the chain is built, at Debug on its own category,
/// and the assertions here are as much about what it costs when nobody is listening as about what
/// it says when somebody is.
/// </para>
/// </summary>
public class FilterChainLogTests {

    private class Controller { }

    private sealed class InstanceStandIn : IExecutionFilter {
        public Task Execute(IExecutionChain chain) => chain.Next();
    }

    private sealed class IoStandIn : IExecutionFilter {
        public Task Execute(IExecutionChain chain) => chain.Next();
    }

    private sealed class TenantFilter : IExecutionFilter {
        public Task Execute(IExecutionChain chain) => chain.Next();
    }

    private sealed class AuditFilter : IExecutionFilter {
        public Task Execute(IExecutionChain chain) => chain.Next();
    }

    private sealed class Generic<T> : IExecutionFilter {
        public Task Execute(IExecutionChain chain) => chain.Next();
    }

    /// <summary>
    /// Registers the way an attribute that gives no name does: a closure over the filter, and a
    /// static lambda, both of which the compiler nests inside this type.
    /// </summary>
    private sealed class TenantProvider : IRequestFilterProvider {
        public IEnumerable<RequestFilterInfo> GetFilters(IExecutionRequestHandlerInfo handlerInfo) {
            var filter = new TenantFilter();

            yield return new RequestFilterInfo(_ => filter, FilterOrder.Authentication);
            yield return new RequestFilterInfo(static _ => new AuditFilter(), FilterOrder.Retry);
        }
    }

    /// <summary>
    /// A logger that records what it was asked and what it was told, so a test can see the check
    /// happen without the line following it.
    /// </summary>
    private sealed class RecordingLogger : ILogger {
        private readonly bool _debug;

        public RecordingLogger(bool debug) {
            _debug = debug;
        }

        public int EnabledChecks { get; private set; }

        public List<(LogLevel Level, EventId EventId, string Message)> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) {
            EnabledChecks++;

            return _debug && logLevel >= LogLevel.Debug;
        }

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Lines.Add((logLevel, eventId, formatter(state, exception)));
    }

    private static (ILoggerFactory Factory, RecordingLogger Logger) Logging(bool debug) {
        var logger = new RecordingLogger(debug);
        var factory = Substitute.For<ILoggerFactory>();

        factory.CreateLogger(Arg.Any<string>()).Returns(logger);

        return (factory, logger);
    }

    /// <summary>
    /// Composes the real filter array through <c>ExecutionHelper</c>, with stand-ins for the
    /// three filters the pipeline pins itself, and returns it without running it.
    /// </summary>
    private static ExecutionHandlerSetup Compose(
        ILoggerFactory? loggerFactory,
        Action<IGlobalFilterRegistry>? register = null,
        params IRequestFilterProvider[] filterProviders) {
        var registry = new GlobalFilterRegistry(Array.Empty<IRequestFilterProvider>());

        register?.Invoke(registry);

        var ioProvider = Substitute.For<IIOFilterProvider>();
        ioProvider.ProvideFilter(
                Arg.Any<IExecutionRequestHandlerInfo>(),
                Arg.Any<Func<IExecutionContext, Task<IExecutionRequestParameters>>>())
            .Returns(new IoStandIn());

        var instanceProvider = Substitute.For<IInstanceFilterProvider>();
        instanceProvider.ProvideFilter<Controller>(Arg.Any<IServiceProvider>())
            .Returns(new InstanceStandIn());

        var context = Pipeline.Context(configureServices: services => {
            services.AddSingleton<IGlobalFilterRegistry>(registry);
            services.AddSingleton(ioProvider);
            services.AddSingleton(instanceProvider);

            if (loggerFactory != null) {
                services.AddSingleton(loggerFactory);
            }
        });

        var handlerInfo = new ExecutionRequestHandlerInfo(
            "/orders", "GET", typeof(Controller), "Read");

        return ExecutionHelper.StandardFilterEmptyParameters<Controller>(
            context.RequestServices, handlerInfo, (_, _) => { }, filterProviders);
    }

    private static IRequestFilterProvider Providing(RequestFilterInfo info) {
        var provider = Substitute.For<IRequestFilterProvider>();

        provider.GetFilters(Arg.Any<IExecutionRequestHandlerInfo>()).Returns(new[] { info });

        return provider;
    }

    /// <summary>
    /// Every filter, in the order it runs, with its order beside it. The pinned three and a filter
    /// registered by instance are named for their types, with a generic's arity dropped; a
    /// registration that gave no name is named for the type that made it, whether the lambda
    /// captured anything or not.
    /// </summary>
    [Fact]
    public void TheComposedChainIsWrittenOnceAtDebugOnItsOwnCategory() {
        var (factory, logger) = Logging(debug: true);

        Compose(
            factory,
            registry => registry.RegisterFilter(new Generic<int>(), FilterOrder.Validation),
            new TenantProvider());

        factory.Received(1).CreateLogger(ExecutionHelper.FilterChainLogCategory);

        var line = Assert.Single(logger.Lines);

        Assert.Equal(LogLevel.Debug, line.Level);
        Assert.Equal(78010, line.EventId.Id);
        Assert.Equal(
            "GET /orders filter chain: " +
            "InstanceStandIn@-10000, TenantProvider@2000, IoStandIn@7000, Generic@8000, " +
            "TenantProvider@10000, InvokeNoParametersFilter@200000",
            line.Message);
    }

    [Fact]
    public void ANamedRegistrationIsWrittenByItsName() {
        var (factory, logger) = Logging(debug: true);

        Compose(
            factory,
            filterProviders: Providing(
                new RequestFilterInfo(_ => new TenantFilter(), FilterOrder.Authentication, "Tenant")));

        Assert.Contains("Tenant@2000", Assert.Single(logger.Lines).Message);
    }

    /// <summary>
    /// A filter with no order sorts at the default, and the line says so rather than leaving the
    /// position blank.
    /// </summary>
    [Fact]
    public void AFilterWithNoOrderIsWrittenAtTheDefaultValue() {
        var (factory, logger) = Logging(debug: true);

        Compose(
            factory,
            filterProviders: Providing(new RequestFilterInfo(_ => new TenantFilter(), null, "Tenant")));

        Assert.Contains("Tenant@" + FilterOrder.DefaultValue, Assert.Single(logger.Lines).Message);
    }

    /// <summary>
    /// A factory with no declaring type at all - an emitted method - is named for the method,
    /// because there is nothing else to name it for.
    /// </summary>
    [Fact]
    public void AFactoryWithNoDeclaringTypeIsNamedForItsMethod() {
        var method = new DynamicMethod("Factory", typeof(IExecutionFilter), [typeof(IExecutionContext)]);
        var il = method.GetILGenerator();

        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);

        var factory = (Func<IExecutionContext, IExecutionFilter>)method.CreateDelegate(
            typeof(Func<IExecutionContext, IExecutionFilter>));

        var (loggerFactory, logger) = Logging(debug: true);

        Compose(
            loggerFactory,
            filterProviders: Providing(new RequestFilterInfo(factory, FilterOrder.Authentication)));

        Assert.Contains("Factory@2000", Assert.Single(logger.Lines).Message);
    }

    /// <summary>
    /// What the line costs when nobody is listening: the level is asked once, per handler, and
    /// nothing is described or written. The generated logging method would ask again before
    /// writing, so a single check is proof the description was never built.
    /// </summary>
    [Fact]
    public void BelowDebugTheLevelIsCheckedOnceAndNothingIsWritten() {
        var (factory, logger) = Logging(debug: false);

        Compose(factory, filterProviders: new TenantProvider());

        Assert.Equal(1, logger.EnabledChecks);
        Assert.Empty(logger.Lines);
    }

    /// <summary>
    /// A pipeline assembled with no logging at all - the bare chains some tests build - composes
    /// as it always did.
    /// </summary>
    [Fact]
    public void AnApplicationWithoutLoggingComposesAsBefore() {
        var setup = Compose(loggerFactory: null, filterProviders: new TenantProvider());

        Assert.Equal(5, setup.Filters.Length);
    }
}
