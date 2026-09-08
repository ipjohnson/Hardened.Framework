using System.Runtime.ExceptionServices;
using Amazon.Lambda.Core;
using Hardened.Aws.Lambda.Runtime.Adapters;
using Hardened.Aws.Lambda.Runtime.Execution;
using Hardened.Aws.Lambda.Runtime.Streaming;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Middleware;
using Hardened.Shared.Runtime.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Hardened.Aws.Lambda.Runtime.Hosting;

/// <summary>
/// One Lambda invocation, from bytes to bytes.
/// </summary>
/// <remarks>
/// <para>
/// The third of the three decisions a host makes. The first two - how a payload becomes a request
/// and how a response becomes a payload - are <see cref="IPayloadAdapter"/>. This is the loop that
/// owns the invocation: it buffers the payload, picks the adapter, opens a scope, hands the context
/// to <see cref="IRequestExecutor"/> and lets the adapter write the answer. Everything between is
/// the shared pipeline, which no host reimplements.
/// </para>
/// <para>
/// It is the counterpart of <c>HardenedHttpApplication</c> for Kestrel and
/// <c>AspNetCoreRequestHandler</c> for ASP.NET Core, and the three now differ only in the parts
/// that are genuinely different.
/// </para>
/// </remarks>
public class LambdaInvocationHandler {
    /// <summary>
    /// What a streamed response with an empty body sends instead of nothing.
    /// </summary>
    /// <remarks>
    /// A zero-byte streamed body is not delivered promptly: CloudFront and a function URL both wait
    /// on data that never comes, and a reader waiting on the first byte hangs until the invocation
    /// times out. One newline ends it.
    /// </remarks>
    private static readonly byte[] EmptyStreamedBody = "\n"u8.ToArray();

    private readonly IServiceProvider _rootServiceProvider;
    private readonly IRequestExecutor _executor;
    private readonly IMetricLoggerProvider _metricLoggerProvider;
    private readonly IPayloadAdapter[] _adapters;
    private readonly IResponseStreamFactory _streams;
    private readonly LambdaResponseMode _mode;

    public LambdaInvocationHandler(
        IServiceProvider rootServiceProvider,
        IRequestExecutor executor,
        IMetricLoggerProvider metricLoggerProvider,
        IEnumerable<IPayloadAdapter> adapters,
        IResponseStreamFactory streams,
        IOptions<ILambdaResponseModeConfiguration> mode) {
        _rootServiceProvider = rootServiceProvider;
        _executor = executor;
        _metricLoggerProvider = metricLoggerProvider;
        _adapters = adapters.ToArray();
        _streams = streams;
        // Read once, when the handler is built. The mode is a property of the deployment, so
        // re-reading it per invocation would ask the same question of the same environment.
        _mode = mode.Value.Mode;
    }

    /// <summary>
    /// The middleware chain dispatch is appended to, exposed so a test can see it was appended once.
    /// </summary>
    public IMiddlewareService Middleware =>
        _rootServiceProvider.GetRequiredService<IMiddlewareService>();

    /// <summary>The adapters this function was built with, in registration order.</summary>
    public IReadOnlyList<IPayloadAdapter> Adapters => _adapters;

    /// <summary>
    /// Runs one invocation and returns what the runtime should send back.
    /// </summary>
    /// <remarks>
    /// The output stream is written by the adapter rather than by the handler, because what belongs
    /// in it differs per source: a proxy response for API Gateway, a batch failure report for SQS,
    /// nothing at all for SNS and a scheduled rule.
    /// </remarks>
    public async Task<Stream> Invoke(Stream input, ILambdaContext lambdaContext) {
        Install();

        using var payload = await Buffer(input);

        var adapter = Select(payload);

        using var deadline = LambdaExecutionContext.ForInvocation(lambdaContext);
        using var scope = _rootServiceProvider.CreateScope();

        // The mode is the deployment's, and whether it can be honoured is the adapter's. A function
        // whose source has no caller holding a connection stays buffered under a stream-mode
        // variable rather than failing, because the variable describes a front door it does not have.
        return _mode == LambdaResponseMode.Stream && adapter is IStreamingPayloadAdapter streaming
            ? await Streamed(streaming, payload, lambdaContext, scope, deadline.Token)
            : await Buffered(adapter, payload, lambdaContext, scope, deadline.Token);
    }

    /// <summary>
    /// One payload back when the handler returns, which is what every source but a streaming front
    /// door expects.
    /// </summary>
    private async Task<Stream> Buffered(
        IPayloadAdapter adapter,
        LambdaPayload payload,
        ILambdaContext lambdaContext,
        IServiceScope scope,
        CancellationToken deadline) {
        var body = new MemoryStream();
        var output = new MemoryStream();

        var context = Context(adapter, payload, lambdaContext, scope, deadline, adapter.CreateResponse(body));

        await _executor.Run(context, adapter.FailurePolicy);

        // A failure is a value, not a throw. The invoke filters catch what a handler raised and
        // record it on the response, so the executor's rethrow never sees it. The batch filter reads
        // that value back for a batched source and rethrows when the transport cannot report per
        // item; nothing read it for an unbatched one, and a scheduled rule or a bus event whose
        // handler threw was reported to Lambda as handled - no retry, no dead letter, no failed
        // invocation. Found on 2026-09-08 from the Azure line, whose worker met the same shape.
        if (adapter.FailurePolicy == HostFailurePolicy.Rethrow &&
            context.Response.ExceptionValue is { } failure) {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }

        await adapter.WriteResponse(context, output);

        output.Position = 0;

        return output;
    }

    /// <summary>
    /// A body that leaves as it is written, opening the Lambda response stream at its first byte.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One mechanism, three timings, and the pipeline never asks which it is running. A streaming
    /// handler opens the stream at its first item and writes a chunk per item after it; a buffered
    /// operation opens it when the serializer writes the body, one write and a close; a refusal
    /// opens it with the refusal's own status when the error serializer writes.
    /// </para>
    /// <para>
    /// Nothing catches here. A throw before the first byte never opens a stream, so it reaches the
    /// bootstrap and is reported as a failed invocation with a complete error response. A throw
    /// after the first byte cannot be taken back, and the bootstrap writes it as trailers - which is
    /// what a truncated stream should be.
    /// </para>
    /// </remarks>
    private async Task<Stream> Streamed(
        IStreamingPayloadAdapter adapter,
        LambdaPayload payload,
        ILambdaContext lambdaContext,
        IServiceScope scope,
        CancellationToken deadline) {
        IExecutionResponse? response = null;

        // Built when the stream opens rather than when the response is created, so the prelude
        // carries whatever the pipeline had decided by the first byte.
        var body = new ResponseStream(() => _streams.CreateHttpStream(adapter.CreatePrelude(response!)));

        response = adapter.CreateResponse(body);

        var context = Context(adapter, payload, lambdaContext, scope, deadline, response);

        try {
            await _executor.Run(context, adapter.FailurePolicy);

            if (body.Length == 0) {
                await body.WriteAsync(EmptyStreamedBody);
            }

            await body.CompleteAsync();
        }
        catch {
            // What was written before the failure still goes, so the client's view of the stream is
            // the handler's up to the point it broke. A failure completing is second to the one
            // already in flight.
            try {
                await body.CompleteAsync();
            }
            catch {
                // The exception in flight is the one to surface.
            }

            throw;
        }

        // Ignored by the bootstrap once a stream has been created, and every response here creates
        // one: the empty case wrote a newline above.
        return Stream.Null;
    }

    private LambdaExecutionContext Context(
        IPayloadAdapter adapter,
        LambdaPayload payload,
        ILambdaContext lambdaContext,
        IServiceScope scope,
        CancellationToken deadline,
        IExecutionResponse response) =>
        new(_rootServiceProvider,
            scope.ServiceProvider,
            scope.ServiceProvider.GetRequiredService<IKnownServices>(),
            adapter.CreateRequest(payload, lambdaContext),
            response,
            deadline,
            _metricLoggerProvider.CreateLogger("lambda-invocation"));

    private bool _installed;

    /// <summary>
    /// Puts handler dispatch at the end of the middleware chain, once.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The same thing <c>KestrelServerRunner</c> does at start and <c>UseHardened</c> does for
    /// ASP.NET Core, and for the same reason it lives outside a constructor there:
    /// <c>MiddlewareService</c> is a singleton holding a plain list, so appending twice puts two
    /// copies of dispatch in every chain.
    /// </para>
    /// <para>
    /// On first invocation rather than at construction because a Lambda function has no start
    /// signal of its own - the runtime hands over an invocation and that is the whole lifecycle.
    /// The bootstrap resolves this handler once and the sandbox is single-threaded per invocation,
    /// so a plain flag is enough.
    /// </para>
    /// <para>
    /// <b>Which dispatch depends on what the application declared.</b> Web verbs compile to a
    /// routing table behind <c>IWebExecutionHandlerService</c>; triggers and <c>[HardenedFunction]</c>
    /// compile to a name switch behind <c>IFunctionHandlerProvider</c>. A function serves one or
    /// the other, and which is a property of the handlers rather than of the host.
    /// </para>
    /// </remarks>
    private void Install() {
        if (_installed) {
            return;
        }

        _installed = true;

        var dispatch = _rootServiceProvider.GetServices<IHandlerDispatch>().ToArray();

        if (dispatch.Length == 0) {
            throw new InvalidOperationException(
                "This function declares no handlers. A verb attribute or a trigger attribute on a " +
                "method is what compiles one.");
        }

        if (dispatch.Length > 1) {
            // Refused rather than ordered. Web dispatch answers 404 for anything its table does not
            // match, so putting it in front of function dispatch swallows every queue message, and
            // putting it behind means a web request meets the not-found handler of a table that
            // never saw it. This is the cross-family mixing the split exists to prevent, and this is
            // where it becomes detectable.
            var kinds = dispatch.Select(one => one.GetType().Name).Distinct().ToArray();

            throw new InvalidOperationException(
                kinds.Length == 1
                    // Same kind twice means two applications in one container, which is what a
                    // second entry point does: it is added to the first rather than replacing it,
                    // and both handler tables end up registered.
                    ? "This container holds two applications, so there are two handler tables and " +
                      "nothing says which one a message routes through. One application per " +
                      "container - a test comparing two of them has to build each its own."
                    : "This function declares more than one kind of handler - " +
                      string.Join(", ", kinds) +
                      ". Web routes and function triggers are separate families and cannot share " +
                      "one function: an HTTP route answers a caller waiting on a connection, and a " +
                      "trigger fails the invocation to make its source redeliver. Split them into " +
                      "two functions.");
        }

        _rootServiceProvider.GetRequiredService<IMiddlewareService>().Use(_ => dispatch[0]);
    }

    /// <summary>
    /// The whole payload, in memory.
    /// </summary>
    /// <remarks>
    /// Lambda caps a synchronous payload at 6MB and an asynchronous one at 256KB, so the size is
    /// bounded and known. Buffering rather than streaming is what makes the rest work: an adapter
    /// binds its event from the bytes, the peek parses them, and a direct invocation hands the same
    /// bytes on as a body - none of which a forward-only stream can serve more than once.
    /// </remarks>
    private static async Task<LambdaPayload> Buffer(Stream input) {
        var buffer = new MemoryStream();

        await input.CopyToAsync(buffer);

        return new LambdaPayload(buffer.GetBuffer().AsMemory(0, (int)buffer.Length));
    }

    /// <summary>
    /// Which adapter owns this payload.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>One adapter is not asked.</b> A function serving a single source has nothing to
    /// discriminate, so the HTTP and invoke families never trigger a parse - and a direct
    /// invocation must not, because a caller's payload need not be JSON at all.
    /// </para>
    /// <para>
    /// <b>Matching nothing is an error, not a fallback.</b> The adapters in a function are the ones
    /// its own handlers asked for, and the deployment wired the sources to match, so a payload none
    /// of them recognises means those two have diverged. Guessing would hand an event to code
    /// written for a different shape.
    /// </para>
    /// </remarks>
    private IPayloadAdapter Select(LambdaPayload payload) {
        if (_adapters.Length == 0) {
            throw new InvalidOperationException(
                "This function registered no payload adapter, so nothing can turn an invocation " +
                "into a request. A trigger attribute on a handler is what registers one.");
        }

        if (_adapters.Length == 1) {
            return _adapters[0];
        }

        foreach (var adapter in _adapters) {
            if (adapter.Handles(payload.Json)) {
                return adapter;
            }
        }

        throw new InvalidOperationException(
            "No adapter recognised this payload. The function is built for " +
            string.Join(", ", _adapters.Select(adapter => adapter.GetType().Name)) +
            ", so either an event source is wired to it that no handler asked for, or a handler's " +
            "trigger has no adapter registered.");
    }
}
