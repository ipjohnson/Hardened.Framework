using Hardened.Requests.Abstract.Execution;
using Microsoft.Extensions.Primitives;

namespace Hardened.Requests.Testing.Conformance;

/// <summary>
/// One request, as the suite wants it dispatched.
/// </summary>
public sealed class TelemetryConformanceRequest {
    public required string Method { get; init; }

    public required string Path { get; init; }

    public IDictionary<string, StringValues> Headers { get; init; } =
        new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// What the pipeline should do where a handler would run. The adapter arranges for this to run
    /// inside the chain, which is how the suite asks for a particular status without knowing how this
    /// transport produces one.
    /// </summary>
    public Action<IExecutionContext>? Handler { get; init; }
}

/// <summary>
/// Implemented once per host to plug it into <see cref="RequestTelemetryConformanceTests"/>.
/// </summary>
/// <remarks>
/// <para>
/// A host, not an adapter object. The other two suites test an <see cref="IExecutionRequest"/> or an
/// <see cref="IExecutionResponse"/> in isolation, and neither would have caught the defect this one
/// exists for: the two Lambda streaming engines ran requests without telling
/// <c>IRequestLogger</c> anything at all, so those runtimes produced no request logging and would
/// have produced no spans. Every object involved was correct. Nothing called them.
/// </para>
/// <para>
/// So the contract is deliberately about the host's own lifecycle: receive a request, run it, finish
/// it. What the suite then asserts is what an observer sees, which is the only place that omission
/// shows up.
/// </para>
/// </remarks>
public interface IRequestTelemetryConformanceAdapter {
    /// <summary>
    /// Name used in assertion messages so a failure identifies the host.
    /// </summary>
    string TransportName { get; }

    /// <summary>
    /// Runs one request from whatever this host calls "received" to whatever it calls "finished",
    /// returning once it considers the request complete.
    /// </summary>
    /// <remarks>
    /// Including the completion step, whatever it is called - <c>DisposeContext</c> on Kestrel, the
    /// end of the request delegate on ASP.NET. A host that reports a beginning and never an end is
    /// exactly the kind of thing this is looking for, so the adapter must not stop early.
    /// </remarks>
    Task Dispatch(TelemetryConformanceRequest request);
}
