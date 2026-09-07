using Amazon.Lambda.Core;
using Hardened.Aws.Lambda.Runtime.Adapters;
using Hardened.Aws.Lambda.Runtime.Execution;
using Hardened.Requests.Abstract.Execution;
using Hardened.Shared.Runtime.Metrics;
using Microsoft.Extensions.DependencyInjection;

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
    private readonly IServiceProvider _rootServiceProvider;
    private readonly IRequestExecutor _executor;
    private readonly IMetricLoggerProvider _metricLoggerProvider;
    private readonly IPayloadAdapter[] _adapters;

    public LambdaInvocationHandler(
        IServiceProvider rootServiceProvider,
        IRequestExecutor executor,
        IMetricLoggerProvider metricLoggerProvider,
        IEnumerable<IPayloadAdapter> adapters) {
        _rootServiceProvider = rootServiceProvider;
        _executor = executor;
        _metricLoggerProvider = metricLoggerProvider;
        _adapters = adapters.ToArray();
    }

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
        using var payload = await Buffer(input);

        var adapter = Select(payload);

        using var deadline = LambdaExecutionContext.ForInvocation(lambdaContext);
        using var scope = _rootServiceProvider.CreateScope();

        var body = new MemoryStream();
        var output = new MemoryStream();

        var context = new LambdaExecutionContext(
            _rootServiceProvider,
            scope.ServiceProvider,
            scope.ServiceProvider.GetRequiredService<IKnownServices>(),
            adapter.CreateRequest(payload, lambdaContext),
            adapter.CreateResponse(body),
            deadline.Token,
            _metricLoggerProvider.CreateLogger("lambda-invocation"));

        await _executor.Run(context, adapter.FailurePolicy);

        await adapter.WriteResponse(context, output);

        output.Position = 0;

        return output;
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
