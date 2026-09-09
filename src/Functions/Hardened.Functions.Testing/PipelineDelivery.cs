using System.Runtime.CompilerServices;
using System.Text.Json;
using DependencyModules.Testing.Attributes.Interfaces;
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
    private readonly ITestContainerSource? _source;

    public PipelineDelivery(IServiceProvider provider) {
        _provider = provider;
        _source = provider.GetService<ITestContainerSource>();
    }

    /// <summary>
    /// The container one delivery runs against, composed and started.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A container per delivery, because two sends are two invocations and a trigger source makes
    /// no promise that one environment serves both. Two queue handlers deployed as two functions are
    /// two processes, so a handler passing only because the previous send warmed a singleton is a
    /// test that cannot fail for the reason production will.
    /// </para>
    /// <para>
    /// A batch is one delivery and therefore one container, which is right: three messages in one
    /// send are one invocation, and the fan-out to a handler call per message happens inside it.
    /// </para>
    /// <para>
    /// What crosses between them is what the test declared - a <c>[Mock]</c>, anything marked
    /// <c>[Shared]</c>, and the harness services the entry point pins. Absent where the harness was
    /// built by hand rather than run by the runner, and then there is one container as before.
    /// </para>
    /// </remarks>
    private async ValueTask<IServiceProvider> ContainerForDeliveryAsync() {
        var provider = _source is { } source ? await source.CreateAsync() : _provider;

        Install(provider);

        return provider;
    }

    /// <summary>
    /// camelCase, which is what a publisher sends and what a handler's binder is set up to read.
    /// </summary>
    private static readonly JsonSerializerOptions Wire =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public async Task Deliver(IReadOnlyList<object> messages, string scheme, string path) {
        var provider = await ContainerForDeliveryAsync();

        var bodies = messages
            .Select(message => JsonSerializer.SerializeToUtf8Bytes(message, Wire))
            .ToArray();

        using var scope = provider.CreateScope();

        var context = new TestExecutionContext(
            provider,
            scope.ServiceProvider,
            scope.ServiceProvider.GetRequiredService<IKnownServices>(),
            new TriggerRequest(scheme, path, bodies),
            new TestExecutionResponse(new MemoryStream()),
            CancellationToken.None);

        // Rethrow, because every trigger family does: failing the invocation is what makes a source
        // redeliver, and a test asserting that a bad message fails has to see the exception.
        await provider.GetRequiredService<IRequestExecutor>()
            .Run(context, HostFailurePolicy.Rethrow);
    }

    /// <remarks>
    /// The answer is read off <c>ResponseValue</c> rather than deserialized from the body: the
    /// handler's return value is set there before the IO filter turns it into bytes, so taking it
    /// from there is both exact and free. An envelope delivery has no such shortcut, which is why
    /// the response type is passed rather than assumed.
    /// </remarks>
    public async Task<object?> Call(object message, string scheme, string path, Type? responseType) {
        var provider = await ContainerForDeliveryAsync();

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

        await provider.GetRequiredService<IRequestExecutor>()
            .Run(context, HostFailurePolicy.Rethrow);

        return response.ResponseValue;
    }

    /// <summary>
    /// Appends the application's dispatch to the middleware chain, once.
    /// </summary>
    /// <remarks>
    /// The same thing a host does at start. <c>MiddlewareService</c> holds a plain list, so
    /// appending twice would put a second copy of dispatch in the chain and run every handler twice.
    ///
    /// Once per container rather than once per delivery, and a flag would be the wrong shape now: a
    /// fresh container has a fresh empty chain, so it needs its own dispatch, and the previous
    /// container's flag says nothing about it. <c>MiddlewareService</c> is per container, so asking
    /// whether this one has been composed is the question that keeps its answer.
    /// </remarks>
    private static void Install(IServiceProvider provider) {
        var middleware = provider.GetRequiredService<IMiddlewareService>();

        if (Composed.TryGetValue(middleware, out _)) {
            return;
        }

        var dispatch = provider.GetServices<IHandlerDispatch>().ToArray();

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

        middleware.Use(_ => dispatch[0]);

        Composed.Add(middleware, Composed);
    }

    /// <summary>
    /// The chains dispatch has already been appended to.
    /// </summary>
    /// <remarks>
    /// Weak on the key, so a container the test is finished with is collectable rather than held
    /// alive by a record that it was composed.
    /// </remarks>
    private static readonly ConditionalWeakTable<IMiddlewareService, object> Composed = new();

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

        // Never consulted, because nothing here reports. A test delivery names no transport, so
        // there is no position to rewind to and per item is the honest answer.
        public BatchFailureMode FailureMode => BatchFailureMode.PerItem;

        public IReadOnlyList<int> FailedItems => Array.Empty<int>();

        public void RecordFailure(int index, Exception failure) =>
            throw new NotSupportedException(
                "Item failures are not reported here, so the filter rethrows instead of recording.");
    }
}
