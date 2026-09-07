using System.Text.Json;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Middleware;
using Hardened.Requests.Runtime.QueryString;
using Hardened.Requests.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;

namespace Hardened.Functions.Testing;

/// <summary>
/// Delivers by building a request and running the pipeline.
/// </summary>
/// <remarks>
/// <para>
/// The default, and the whole of what a delivery is once an envelope has been read: routing on the
/// scheme and path, the batch fan-out, binding, validation, every filter and the handler. What it
/// does not do is parse a provider's envelope, so it names no cloud and a test written against it
/// is unchanged if the application moves host.
/// </para>
/// <para>
/// A project wanting the adapter exercised too adds its provider's testing attribute, which
/// replaces this. Whether an adapter's scheme and path agree with the ones the generator registered
/// is only visible on that path - it is the bug class the envelope route exists to catch.
/// </para>
/// </remarks>
public sealed class PipelineDelivery : ITriggerDelivery {
    private readonly IServiceProvider _provider;
    private bool _installed;

    public PipelineDelivery(IServiceProvider provider) {
        _provider = provider;
    }

    /// <summary>
    /// camelCase, which is what a publisher sends and what a handler's binder is set up to read.
    /// </summary>
    private static readonly JsonSerializerOptions Wire =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public async Task Deliver(IReadOnlyList<object> messages, string scheme, string path) {
        Install();

        var bodies = messages
            .Select(message => JsonSerializer.SerializeToUtf8Bytes(message, Wire))
            .ToArray();

        using var scope = _provider.CreateScope();

        var context = new TestExecutionContext(
            _provider,
            scope.ServiceProvider,
            scope.ServiceProvider.GetRequiredService<IKnownServices>(),
            new TriggerRequest(scheme, path, bodies),
            new TestExecutionResponse(new MemoryStream()),
            CancellationToken.None);

        // Rethrow, because every trigger family does: failing the invocation is what makes a source
        // redeliver, and a test asserting that a bad message fails has to see the exception.
        await _provider.GetRequiredService<IRequestExecutor>()
            .Run(context, HostFailurePolicy.Rethrow);
    }

    /// <remarks>
    /// The answer is read off <c>ResponseValue</c> rather than deserialized from the body: the
    /// handler's return value is set there before the IO filter turns it into bytes, so taking it
    /// from there is both exact and free. An envelope delivery has no such shortcut, which is why
    /// the response type is passed rather than assumed.
    /// </remarks>
    public async Task<object?> Call(object message, string scheme, string path, Type? responseType) {
        Install();

        var body = JsonSerializer.SerializeToUtf8Bytes(message, Wire);

        using var scope = _provider.CreateScope();

        var response = new TestExecutionResponse(new MemoryStream());

        var context = new TestExecutionContext(
            _provider,
            scope.ServiceProvider,
            scope.ServiceProvider.GetRequiredService<IKnownServices>(),
            new InvocationRequest(scheme, path, body),
            response,
            CancellationToken.None);

        await _provider.GetRequiredService<IRequestExecutor>()
            .Run(context, HostFailurePolicy.Rethrow);

        return response.ResponseValue;
    }

    /// <summary>
    /// Appends the application's dispatch to the middleware chain, once.
    /// </summary>
    /// <remarks>
    /// The same thing a host does at start. <c>MiddlewareService</c> holds a plain list, so
    /// appending per message would put a second copy of dispatch in the chain and run every handler
    /// twice from the second message onwards.
    /// </remarks>
    private void Install() {
        if (_installed) {
            return;
        }

        _installed = true;

        var dispatch = _provider.GetServices<IHandlerDispatch>().ToArray();

        if (dispatch.Length != 1) {
            var kinds = dispatch.Select(one => one.GetType().Name).Distinct().ToArray();

            throw new InvalidOperationException(
                dispatch.Length == 0
                    ? "This application declares no handlers, so there is nothing to send to."
                    : kinds.Length == 1
                        // Same kind twice means two applications in one container, which is what a
                        // second entry point does: it is added to the first rather than replacing
                        // it, and both handler tables end up registered.
                        ? "This container holds two applications, so there are two handler tables " +
                          "and nothing says which one a message routes through. One application " +
                          "per container - a test comparing two of them has to build each its own."
                        : "This application declares more than one kind of handler, which no host " +
                          "will run. Split the web routes and the triggers into two applications.");
        }

        _provider.GetRequiredService<IMiddlewareService>().Use(_ => dispatch[0]);
    }

    /// <summary>
    /// One invocation, which is not a batch and never fans out.
    /// </summary>
    private sealed class InvocationRequest : TestExecutionRequest {
        public InvocationRequest(string scheme, string path, byte[] body)
            : base(scheme, path, "application/json",
                new SimpleQueryStringCollection((IDictionary<string, string>?)null)) {
            Body = new MemoryStream(body, writable: false);
            Headers = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// A delivery, as the pipeline sees one.
    /// </summary>
    /// <remarks>
    /// A batch whatever its size, because every trigger source that carries one message can carry
    /// ten and the fan-out filter is what a handler actually runs behind. Sending one message
    /// through a non-batch request would test a path no deployed function takes.
    /// </remarks>
    private sealed class TriggerRequest : TestExecutionRequest, IBatchRequest {
        private readonly IReadOnlyList<byte[]> _bodies;

        public TriggerRequest(string scheme, string path, IReadOnlyList<byte[]> bodies)
            : base(scheme, path, "application/json",
                new SimpleQueryStringCollection((IDictionary<string, string>?)null)) {
            _bodies = bodies;
            Body = new MemoryStream();
        }

        public int Count => _bodies.Count;

        public IExecutionRequest ForItem(int index) =>
            new TestExecutionRequest(
                Method, Path, "application/json",
                new SimpleQueryStringCollection((IDictionary<string, string>?)null)) {
                Body = new MemoryStream(_bodies[index], writable: false),
                Headers = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase)
            };

        /// <summary>
        /// False, matching every trigger source's default. A failed message fails the invocation,
        /// which is what a test asserting a failure has to be able to see.
        /// </summary>
        public bool ReportsItemFailures => false;

        public IReadOnlyList<int> FailedItems => Array.Empty<int>();

        public void RecordFailure(int index, Exception failure) =>
            throw new NotSupportedException(
                "Item failures are not reported here, so the filter rethrows instead of recording.");
    }
}
