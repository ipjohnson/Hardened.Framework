using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Logging;
using Hardened.Requests.Abstract.RequestFilter;
using Hardened.Requests.Runtime.Execution;
using Hardened.Requests.Runtime.Filters;
using Hardened.Requests.Runtime.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Execution;

/// <summary>
/// The invoke filters are the last link: they take the controller the instance filter
/// resolved and the parameters the IO filter bound, and call the handler method the generator
/// emitted a delegate for.
///
/// <para>
/// Their whole job is to fail safely. A handler that throws, a controller of the wrong type,
/// parameters that did not bind - none of them may escape as an exception, because there is no
/// filter above them that could turn one into a response body other than the IO filter, and
/// the pipeline expects to find the failure on the response.
/// </para>
/// </summary>
public class InvokeFilterTests {

    private class Controller {
        public List<string> Calls { get; } = new();
    }

    private class Parameters : IExecutionRequestParameters {
        public bool TryGetParameter(string parameterName, out object? parameterValue) {
            parameterValue = null;

            return false;
        }

        public bool TrySetParameter(string parameterName, object parameterValue) => false;

        public IReadOnlyList<IExecutionRequestParameter> Info => Array.Empty<IExecutionRequestParameter>();

        public object this[int index] {
            get => throw new IndexOutOfRangeException();
            set => throw new IndexOutOfRangeException();
        }

        public int ParameterCount => 0;

        public IExecutionRequestParameters Clone() => this;
    }

    private static IExecutionContext ContextWith(object? handlerInstance, IRequestLogger? logger = null) {
        var context = Pipeline.Context(configureServices: services => {
            if (logger is not null) {
                services.AddSingleton(logger);
            }
        });

        context.HandlerInstance = handlerInstance;

        return context;
    }

    // ------------------------------------------------------------------ synchronous handlers

    [Fact]
    public async Task ASynchronousHandlerWithNoParametersIsCalledWithItsController() {
        var controller = new Controller();
        var context = ContextWith(controller);

        var filter = new InvokeNoParametersFilter<Controller>((_, c) => c.Calls.Add("invoked"));

        await Pipeline.Chain(context, filter).Next();

        Assert.Equal(new[] { "invoked" }, controller.Calls);
        Assert.Null(context.Response.ExceptionValue);
    }

    [Fact]
    public async Task ASynchronousHandlerWithParametersReceivesBoth() {
        var controller = new Controller();
        var parameters = new Parameters();
        var context = ContextWith(controller);

        context.Request.Parameters = parameters;

        object? seen = null;
        var filter = new InvokeWithParametersFilter<Controller, Parameters>(
            (_, c, p) => {
                c.Calls.Add("invoked");
                seen = p;
            });

        await Pipeline.Chain(context, filter).Next();

        Assert.Same(parameters, seen);
        Assert.Equal(new[] { "invoked" }, controller.Calls);
    }

    /// <summary>
    /// A controller of the wrong type means the instance filter and the invoke filter disagree
    /// about the handler. It is reported on the response, naming the type that was expected.
    /// </summary>
    [Fact]
    public async Task AControllerOfTheWrongTypeIsReportedRatherThanThrown() {
        var logger = Substitute.For<IRequestLogger>();
        var context = ContextWith("not a controller", logger);

        var filter = new InvokeNoParametersFilter<Controller>((_, _) => { });

        await Pipeline.Chain(context, filter).Next();

        Assert.NotNull(context.Response.ExceptionValue);
        Assert.Contains(nameof(Controller), context.Response.ExceptionValue!.Message);
    }

    /// <summary>
    /// The same when the controller was never resolved at all - the instance filter did not
    /// run, or ran and found nothing.
    /// </summary>
    [Fact]
    public async Task AMissingControllerIsReportedRatherThanThrown() {
        var context = ContextWith(handlerInstance: null, Substitute.For<IRequestLogger>());

        var filter = new InvokeNoParametersFilter<Controller>((_, _) => { });

        await Pipeline.Chain(context, filter).Next();

        Assert.NotNull(context.Response.ExceptionValue);
    }

    /// <summary>
    /// Parameters that are not the shape the handler expects - a route wired to the wrong
    /// binding - are reported the same way.
    /// </summary>
    [Fact]
    public async Task ParametersOfTheWrongTypeAreReportedRatherThanThrown() {
        var context = ContextWith(new Controller(), Substitute.For<IRequestLogger>());

        context.Request.Parameters = EmptyParameters.Instance;

        var filter = new InvokeWithParametersFilter<Controller, Parameters>((_, _, _) => { });

        await Pipeline.Chain(context, filter).Next();

        Assert.NotNull(context.Response.ExceptionValue);
        Assert.Contains(nameof(Parameters), context.Response.ExceptionValue!.Message);
    }

    /// <summary>
    /// An exception from inside the handler is the common case, and the one the whole
    /// arrangement exists for.
    /// </summary>
    [Fact]
    public async Task AnExceptionFromInsideASynchronousHandlerBecomesTheResponsesException() {
        var context = ContextWith(new Controller(), Substitute.For<IRequestLogger>());
        var failure = new InvalidOperationException("order not found");

        var filter = new InvokeNoParametersFilter<Controller>((_, _) => throw failure);

        await Pipeline.Chain(context, filter).Next();

        Assert.Same(failure, context.Response.ExceptionValue);
    }

    [Fact]
    public async Task AnExceptionFromASynchronousHandlerWithParametersBecomesTheResponsesException() {
        var context = ContextWith(new Controller(), Substitute.For<IRequestLogger>());
        var failure = new InvalidOperationException("order not found");

        context.Request.Parameters = new Parameters();

        var filter = new InvokeWithParametersFilter<Controller, Parameters>((_, _, _) => throw failure);

        await Pipeline.Chain(context, filter).Next();

        Assert.Same(failure, context.Response.ExceptionValue);
    }

    /// <summary>
    /// The invoke filter is terminal: it does not call <c>Next</c>, so nothing ordered after
    /// the handler runs. See <c>FilterOrderingTests</c> for what that means for filter order.
    /// </summary>
    [Fact]
    public async Task TheInvokeFilterDoesNotContinueDownTheChain() {
        var log = new List<string>();
        var context = ContextWith(new Controller());

        await Pipeline.Chain(context,
            new InvokeNoParametersFilter<Controller>((_, _) => log.Add("invoke")),
            new Pipeline.Recording(log, "after")).Next();

        Assert.Equal(new[] { "invoke" }, log);
    }

    // ----------------------------------------------------------------------- async handlers

    [Fact]
    public async Task AnAsyncHandlerWithParametersIsAwaitedBeforeTheFilterReturns() {
        var controller = new Controller();
        var context = ContextWith(controller);

        context.Request.Parameters = new Parameters();

        var filter = new AsyncInvokeWithParametersFilter<Controller, Parameters>(
            async (_, c, _) => {
                await Task.Yield();

                c.Calls.Add("invoked");
            });

        await Pipeline.Chain(context, filter).Next();

        Assert.Equal(new[] { "invoked" }, controller.Calls);
    }

    /// <summary>
    /// An exception thrown after the first await still reaches the response. A filter that
    /// only caught synchronously would let it escape as a faulted task.
    /// </summary>
    [Fact]
    public async Task AnExceptionThrownAfterAnAwaitStillReachesTheResponse() {
        var context = ContextWith(new Controller(), Substitute.For<IRequestLogger>());
        var failure = new InvalidOperationException("failed after awaiting");

        context.Request.Parameters = new Parameters();

        var filter = new AsyncInvokeWithParametersFilter<Controller, Parameters>(
            async (_, _, _) => {
                await Task.Yield();

                throw failure;
            });

        await Pipeline.Chain(context, filter).Next();

        Assert.Same(failure, context.Response.ExceptionValue);
    }

    [Fact]
    public async Task AnAsyncHandlerWithTheWrongControllerTypeIsReportedRatherThanThrown() {
        var context = ContextWith("not a controller", Substitute.For<IRequestLogger>());

        context.Request.Parameters = new Parameters();

        var filter = new AsyncInvokeWithParametersFilter<Controller, Parameters>(
            (_, _, _) => Task.CompletedTask);

        await Pipeline.Chain(context, filter).Next();

        Assert.NotNull(context.Response.ExceptionValue);
    }

    [Fact]
    public async Task AnAsyncHandlerWithTheWrongParameterTypeIsReportedRatherThanThrown() {
        var context = ContextWith(new Controller(), Substitute.For<IRequestLogger>());

        context.Request.Parameters = EmptyParameters.Instance;

        var filter = new AsyncInvokeWithParametersFilter<Controller, Parameters>(
            (_, _, _) => Task.CompletedTask);

        await Pipeline.Chain(context, filter).Next();

        Assert.NotNull(context.Response.ExceptionValue);
    }

    /// <summary>
    /// The async no-parameter filter is internal and only reachable the way generated code
    /// reaches it, through <see cref="ExecutionHelper"/>. Driving it that way also asserts the
    /// helper wires the right filter for an async parameterless handler.
    /// </summary>
    [Fact]
    public async Task AnAsyncHandlerWithNoParametersIsAwaitedBeforeTheFilterReturns() {
        var controller = new Controller();
        var (context, filters) = AsyncNoParameterPipeline(controller, async (_, c) => {
            await Task.Yield();

            c.Calls.Add("invoked");
        });

        await new ExecutionChain(filters, context).Next();

        Assert.Equal(new[] { "invoked" }, controller.Calls);
        Assert.Null(context.Response.ExceptionValue);
    }

    [Fact]
    public async Task AnExceptionFromAnAsyncParameterlessHandlerBecomesTheResponsesException() {
        var failure = new InvalidOperationException("failed");

        var (context, filters) = AsyncNoParameterPipeline(new Controller(), async (_, _) => {
            await Task.Yield();

            throw failure;
        });

        await new ExecutionChain(filters, context).Next();

        Assert.Same(failure, context.Response.ExceptionValue);
    }

    private static (IExecutionContext, Func<IExecutionContext, IExecutionFilter>[]) AsyncNoParameterPipeline(
        Controller controller,
        ExecutionHelper.AsyncInvokeNoParameters<Controller> invoke) {

        var ioProvider = Substitute.For<IIOFilterProvider>();
        ioProvider.ProvideFilter(
                Arg.Any<IExecutionRequestHandlerInfo>(),
                Arg.Any<Func<IExecutionContext, Task<IExecutionRequestParameters>>>())
            .Returns(new Pipeline.Inline(c => c.Next()));

        var instanceProvider = Substitute.For<IInstanceFilterProvider>();
        instanceProvider.ProvideFilter<Controller>(Arg.Any<IServiceProvider>())
            .Returns(new Pipeline.Inline(c => c.Next()));

        var context = Pipeline.Context(configureServices: services => {
            services.AddSingleton<IGlobalFilterRegistry>(
                new GlobalFilterRegistry(Array.Empty<IRequestFilterProvider>()));
            services.AddSingleton(ioProvider);
            services.AddSingleton(instanceProvider);
            services.AddSingleton(Substitute.For<IRequestLogger>());
        });

        context.HandlerInstance = controller;

        var filters = ExecutionHelper.AsyncStandardFilterEmptyParameters(
            context.RequestServices,
            new ExecutionRequestHandlerInfo("/orders", "GET", typeof(Controller), "Get"),
            invoke,
            Array.Empty<IRequestFilterProvider>()).Filters;

        return (context, filters);
    }
}
