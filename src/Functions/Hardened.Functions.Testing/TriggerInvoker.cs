using System.Text;
using System.Text.Json;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Middleware;
using Hardened.Requests.Testing;
using Hardened.Requests.Runtime.QueryString;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;

namespace Hardened.Functions.Testing;

/// <summary>
/// Delivers a test message to the handler its route names.
/// </summary>
/// <remarks>
/// <para>
/// Builds a request and runs the pipeline, which is the whole of what a delivery is once the
/// envelope has been read. Routing, the batch fan-out, binding, validation, every filter and the
/// handler all run; what does not is the adapter that would have parsed a provider's envelope.
/// </para>
/// <para>
/// <b>That omission is deliberate, and it is why this names no cloud.</b> Whether an adapter's
/// scheme and path agree with the ones the generator registered is a property of the framework, and
/// this repository's own integration fixtures prove it once against a real envelope. Making every
/// application re-prove it would cost every application's tests a dependency on a cloud, and they
/// would then have to change when the host did - which is the one thing the design is for.
/// </para>
/// </remarks>
public sealed class TriggerInvoker {
    private readonly IServiceProvider _provider;
    private bool _installed;

    public TriggerInvoker(IServiceProvider provider) {
        _provider = provider;
    }

    /// <summary>
    /// camelCase, which is what a publisher sends and what a handler's binder is set up to read.
    /// </summary>
    private static readonly JsonSerializerOptions Wire =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>
    /// The delegate a generated façade is constructed with.
    /// </summary>
    /// <remarks>
    /// A <c>Func</c> of types that need no package, so a façade compiled into an application
    /// references nothing from here. Only a test project does.
    /// </remarks>
    public Func<object, string, string, Task> Invoke => Send;

    /// <summary>Builds the façade for one trigger kind, wired to this invoker.</summary>
    public TFacade Facade<TFacade>() =>
        (TFacade)Activator.CreateInstance(typeof(TFacade), Invoke)!;

    private async Task Send(object messages, string scheme, string path) {
        Install();

        var bodies = new List<byte[]>();

        foreach (var message in (System.Collections.IEnumerable)messages) {
            bodies.Add(JsonSerializer.SerializeToUtf8Bytes(message, Wire));
        }

        using var scope = _provider.CreateScope();

        var request = new TriggerRequest(scheme, path, bodies);

        var context = new TestExecutionContext(
            _provider,
            scope.ServiceProvider,
            scope.ServiceProvider.GetRequiredService<IKnownServices>(),
            request,
            new TestExecutionResponse(new MemoryStream()),
            CancellationToken.None);

        // Rethrow, because every trigger family does: failing the invocation is what makes a source
        // redeliver, and a test asserting that a bad message fails has to see the exception.
        await _provider.GetRequiredService<IRequestExecutor>()
            .Run(context, HostFailurePolicy.Rethrow);
    }

    /// <summary>
    /// Appends the application's dispatch to the middleware chain, once.
    /// </summary>
    /// <remarks>
    /// The same thing a host does at start. <c>MiddlewareService</c> holds a plain list, so
    /// appending per message would put a second copy of dispatch in the chain and run every handler
    /// twice from the second test onwards.
    /// </remarks>
    private void Install() {
        if (_installed) {
            return;
        }

        _installed = true;

        var dispatch = _provider.GetServices<IHandlerDispatch>().ToArray();

        if (dispatch.Length != 1) {
            throw new InvalidOperationException(
                dispatch.Length == 0
                    ? "This application declares no handlers, so there is nothing to send to."
                    : "This application declares more than one kind of handler, which no host " +
                      "will run. Split the web routes and the triggers into two applications.");
        }

        _provider.GetRequiredService<IMiddlewareService>().Use(_ => dispatch[0]);
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
            : base(scheme, path, "application/json", new SimpleQueryStringCollection(
                (IDictionary<string, string>?)null)) {
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
