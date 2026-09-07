using System.Text;
using Amazon.Lambda.Core;
using Hardened.Aws.Lambda.Runtime.Adapters;
using Hardened.Aws.Lambda.Runtime.Execution;
using Hardened.Aws.Lambda.Runtime.Hosting;
using Hardened.Aws.Lambda.Runtime.Tests.Infrastructure;
using Hardened.Requests.Abstract.Execution;
using Hardened.Shared.Runtime.Metrics;
using Microsoft.Extensions.DependencyInjection;
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

        var provider = services.BuildServiceProvider();

        return (new LambdaInvocationHandler(
            provider, executor, new NullMetricLoggerProvider(), adapters), executor);
    }

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
