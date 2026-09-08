using System.Text;
using Amazon.Lambda.Core;
using Hardened.Aws.Lambda.Runtime.Adapters;
using Hardened.Aws.Lambda.Runtime.Execution;
using Hardened.Aws.Lambda.Runtime.Hosting;
using Hardened.Aws.Lambda.Runtime.Streaming;
using Hardened.Aws.Lambda.Runtime.Tests.Infrastructure;
using Hardened.Aws.Lambda.ApiGateway;
using Hardened.Aws.Lambda.EventBridge;
using Hardened.Aws.Lambda.Invoke;
using Hardened.Aws.Lambda.Sns;
using Hardened.Aws.Lambda.Sqs;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Middleware;
using Hardened.Requests.Runtime.Middleware;
using Hardened.Shared.Runtime.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Hardened.Aws.Lambda.Runtime.Tests.Hosting;

/// <summary>
/// The invocation loop: which adapter is chosen, what reaches the pipeline, and what comes back.
/// </summary>
public class LambdaInvocationHandlerTests {

    /// <summary>
    /// Records what the executor was handed, so a test can assert on the request the adapter built
    /// and the policy the adapter asked for without running a real pipeline.
    /// </summary>
    private sealed class RecordingExecutor : IRequestExecutor {
        public IExecutionContext? Context;
        public HostFailurePolicy? Policy;
        public Func<IExecutionContext, Task>? Body;

        public void Begin(IExecutionContext context) { }

        public Task RunChain(IExecutionContext context, HostFailurePolicy onFailure) =>
            Task.CompletedTask;

        public void End(IExecutionContext context) { }

        public Task Run(IExecutionContext context, HostFailurePolicy onFailure) {
            Context = context;
            Policy = onFailure;

            return Body?.Invoke(context) ?? Task.CompletedTask;
        }
    }

    private static (LambdaInvocationHandler Handler, RecordingExecutor Executor) Build(
        params IPayloadAdapter[] adapters) {
        var executor = new RecordingExecutor();

        var services = new ServiceCollection();

        services.AddSingleton<IKnownServices>(new StubKnownServices());

        // The handler appends the application's dispatch here on first invocation, the way a web
        // host appends routing at start. The real MiddlewareService is used rather than a
        // substitute because appending twice is the failure the once-only guard exists for.
        services.AddSingleton<IMiddlewareService, MiddlewareService>();

        // What a routing generator would have registered. Which kind it is does not matter to the
        // host - that is the point of IHandlerDispatch - so a stand-in is honest here.
        services.AddSingleton<IHandlerDispatch, StubDispatch>();

        var provider = services.BuildServiceProvider();

        return (new LambdaInvocationHandler(
                provider, executor, new NullMetricLoggerProvider(), adapters,
                new CapturingResponseStreamFactory(), Mode()),
            executor);
    }

    /// <summary>
    /// The response mode as the configuration would have read it. Buffered unless a test says
    /// otherwise, which is the default a function with no variable set runs under.
    /// </summary>
    private static IOptions<ILambdaResponseModeConfiguration> Mode(
        LambdaResponseMode mode = LambdaResponseMode.Buffered) =>
        Options.Create<ILambdaResponseModeConfiguration>(
            new LambdaResponseModeConfiguration { Mode = mode });

    private static ILambdaContext Context() =>
        new TestLambdaContext(remainingTime: TimeSpan.FromSeconds(30));

    private static Stream Input(string json) => new MemoryStream(Encoding.UTF8.GetBytes(json));

    // ------------------------------------------------------------------ adapter selection

    /// <summary>
    /// One adapter is used without being asked, which is what keeps the HTTP and invoke families
    /// from ever parsing a payload to find out what they already know.
    /// </summary>
    [Fact]
    public async Task OneAdapterIsNotAsked() {
        var adapter = new CountingAdapter(new InvokeAdapter());

        var (handler, _) = Build(adapter);

        await handler.Invoke(Input("not json at all"), Context());

        Assert.Equal(0, adapter.Asked);
    }

    [Fact]
    public async Task SeveralAdaptersAreAskedUntilOneClaims() {
        var (handler, executor) = Build(new SqsAdapter(), new SnsAdapter(), new EventBridgeAdapter());

        await handler.Invoke(Input(Payloads.SnsJson), Context());

        Assert.Equal("TOPIC", executor.Context!.Request.Method);
        Assert.Equal("/order-events", executor.Context.Request.Path);
    }

    /// <summary>
    /// The deployment and the code have diverged, and guessing would hand an event to code written
    /// for a different shape. The message names what the function was built for.
    /// </summary>
    [Fact]
    public async Task APayloadNothingClaimsIsAnError() {
        var (handler, _) = Build(new SqsAdapter(), new SnsAdapter());

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.Invoke(Input("""{"detail-type":"OrderPlaced","source":"acme"}"""), Context()));

        Assert.Contains("SqsAdapter", failure.Message);
        Assert.Contains("SnsAdapter", failure.Message);
    }

    [Fact]
    public async Task AFunctionWithNoAdapterSaysSo() {
        var (handler, _) = Build();

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.Invoke(Input("{}"), Context()));

        Assert.Contains("registered no payload adapter", failure.Message);
    }

    // ------------------------------------------------------------------ the policy

    [Fact]
    public async Task AWebAdapterAsksForTheFailureToBeAnswered() {
        var (handler, executor) = Build(new ApiGatewayAdapter());

        await handler.Invoke(Input(Payloads.ApiGatewayJson), Context());

        Assert.Equal(HostFailurePolicy.Answer500, executor.Policy);
    }

    [Fact]
    public async Task AnEventAdapterAsksForTheFailureToBeRethrown() {
        var (handler, executor) = Build(new SqsAdapter());

        await handler.Invoke(Input(Payloads.SqsJson), Context());

        Assert.Equal(HostFailurePolicy.Rethrow, executor.Policy);
    }

    // ------------------------------------------------------------------ the answer

    /// <summary>
    /// The adapter writes the output, not the host: a proxy response here, a batch failure report
    /// for SQS, nothing at all for SNS.
    /// </summary>
    [Fact]
    public async Task TheAdapterWritesTheAnswer() {
        var (handler, executor) = Build(new ApiGatewayAdapter());

        executor.Body = context => {
            context.Response.Status = 201;

            return Task.CompletedTask;
        };

        var output = await handler.Invoke(Input(Payloads.ApiGatewayJson), Context());

        Assert.Contains("\"statusCode\":201", new StreamReader(output).ReadToEnd());
    }

    [Fact]
    public async Task ASourceThatReadsNoResponseWritesNothing() {
        var (handler, _) = Build(new SnsAdapter());

        var output = await handler.Invoke(Input(Payloads.SnsJson), Context());

        Assert.Equal(0, output.Length);
    }

    /// <summary>
    /// The stream is handed back positioned at zero, because the bootstrap reads it straight out.
    /// </summary>
    [Fact]
    public async Task TheAnswerIsRewound() {
        var (handler, _) = Build(new SqsAdapter());

        var output = await handler.Invoke(Input(Payloads.SqsJson), Context());

        Assert.Equal(0, output.Position);
    }

    // ------------------------------------------------------------------ the deadline

    /// <summary>
    /// Lambda kills a function at its deadline, so a handler that only finds out then has no time
    /// to log, flush or report. The token trips before that.
    /// </summary>
    [Fact]
    public async Task TheDeadlineBecomesACancellationToken() {
        var (handler, executor) = Build(new SqsAdapter());

        await handler.Invoke(
            Input(Payloads.SqsJson),
            new TestLambdaContext(remainingTime: TimeSpan.FromSeconds(30)));

        Assert.True(executor.Context!.CancellationToken.CanBeCanceled);
        Assert.False(executor.Context.CancellationToken.IsCancellationRequested);
    }

    /// <summary>
    /// Already inside the margin. Cancelled from the start is the honest answer: there is no time
    /// to do the work, and starting it would be killed halfway.
    /// </summary>
    [Fact]
    public async Task AnInvocationWithNoTimeLeftStartsCancelled() {
        var (handler, executor) = Build(new SqsAdapter());

        await handler.Invoke(
            Input(Payloads.SqsJson),
            new TestLambdaContext(remainingTime: TimeSpan.FromMilliseconds(10)));

        Assert.True(executor.Context!.CancellationToken.IsCancellationRequested);
    }

    /// <summary>
    /// Dispatch is appended once however many invocations a warm sandbox serves.
    /// </summary>
    /// <remarks>
    /// <c>MiddlewareService</c> holds a plain list and every invocation goes through the same
    /// handler instance, so appending per invocation would put a second copy of dispatch in the
    /// chain on the second message and run every handler twice.
    /// </remarks>
    [Fact]
    public async Task DispatchIsInstalledOnlyOnce() {
        var (handler, _) = Build(new SqsAdapter());

        await handler.Invoke(Input(Payloads.SqsJson), Context());
        await handler.Invoke(Input(Payloads.SqsJson), Context());

        Assert.Equal(1, DispatchFilters(handler));
    }

    /// <summary>Stands in for whichever dispatch a routing generator registered.</summary>
    private sealed class StubDispatch : IHandlerDispatch {
        public Task Execute(IExecutionChain chain) => Task.CompletedTask;
    }

    /// <summary>
    /// How many dispatch filters the middleware service would put in a chain.
    /// </summary>
    /// <remarks>
    /// Read off the service's own list by reflection, which is unpleasant and is still the honest
    /// way to ask: the alternative is running a chain and counting how often a handler was reached,
    /// which needs a routed handler and turns a two-line assertion into a second integration test.
    /// </remarks>
    private static int DispatchFilters(LambdaInvocationHandler handler) {
        var middleware = (MiddlewareService)handler.Middleware;

        var field = typeof(MiddlewareService).GetField(
            "_filters",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        var filters =
            (List<Func<IExecutionContext, IExecutionFilter>>)field!.GetValue(middleware)!;

        return filters.Count(factory => factory(null!) is StubDispatch);
    }

    /// <summary>
    /// An application whose handlers compiled to two kinds of dispatch. Ordering them cannot help -
    /// web dispatch answers 404 for anything its table misses - so the host refuses and names both.
    /// </summary>
    [Fact]
    public async Task TwoKindsOfDispatchAreRefused() {
        var services = new ServiceCollection();

        services.AddSingleton<IKnownServices>(new StubKnownServices());
        services.AddSingleton<IMiddlewareService, MiddlewareService>();
        services.AddSingleton<IHandlerDispatch, StubDispatch>();
        services.AddSingleton<IHandlerDispatch, OtherStubDispatch>();

        var handler = new LambdaInvocationHandler(
            services.BuildServiceProvider(), new RecordingExecutor(),
            new NullMetricLoggerProvider(), [new SqsAdapter()],
            new CapturingResponseStreamFactory(), Mode());

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.Invoke(Input(Payloads.SqsJson), Context()));

        Assert.Contains("StubDispatch", failure.Message);
        Assert.Contains("OtherStubDispatch", failure.Message);
    }

    /// <summary>
    /// No handlers at all. Said at the first invocation rather than answered with an empty
    /// response, which is what an application with no routing generator referenced would otherwise
    /// do forever.
    /// </summary>
    [Fact]
    public async Task NoDispatchAtAllIsRefused() {
        var services = new ServiceCollection();

        services.AddSingleton<IKnownServices>(new StubKnownServices());
        services.AddSingleton<IMiddlewareService, MiddlewareService>();

        var handler = new LambdaInvocationHandler(
            services.BuildServiceProvider(), new RecordingExecutor(),
            new NullMetricLoggerProvider(), [new SqsAdapter()],
            new CapturingResponseStreamFactory(), Mode());

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.Invoke(Input(Payloads.SqsJson), Context()));

        Assert.Contains("declares no handlers", failure.Message);
    }

    private sealed class OtherStubDispatch : IHandlerDispatch {
        public Task Execute(IExecutionChain chain) => Task.CompletedTask;
    }

    /// <summary>Counts how often the peek was run, without changing what the adapter answers.</summary>
    private sealed class CountingAdapter : IPayloadAdapter {
        private readonly IPayloadAdapter _inner;

        public int Asked;

        public CountingAdapter(IPayloadAdapter inner) {
            _inner = inner;
        }

        public bool Handles(System.Text.Json.JsonElement payload) {
            Asked++;

            return _inner.Handles(payload);
        }

        public IExecutionRequest CreateRequest(LambdaPayload payload, ILambdaContext context) =>
            _inner.CreateRequest(payload, context);

        public IExecutionResponse CreateResponse(Stream output) => _inner.CreateResponse(output);

        public HostFailurePolicy FailurePolicy => _inner.FailurePolicy;

        public ValueTask WriteResponse(IExecutionContext context, Stream output) =>
            _inner.WriteResponse(context, output);
    }
}
