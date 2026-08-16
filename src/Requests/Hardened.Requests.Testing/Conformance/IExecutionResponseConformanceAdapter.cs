using Hardened.Requests.Abstract.Execution;

namespace Hardened.Requests.Testing.Conformance;

/// <summary>
/// Implemented once per transport to plug it into <see cref="ExecutionResponseConformanceTests"/>.
/// </summary>
/// <remarks>
/// There is no spec parameter, unlike the request side. A request arrives pre-formed from the wire
/// and has to be described in neutral terms before an adapter can build one; a response is written
/// through <see cref="IExecutionResponse"/>, which is the contract under test, so the suite drives
/// it directly and the adapter only has to supply one and finish it.
///
/// <para>
/// The implementation should do the least work possible beyond wiring the transport's own response
/// and running its own completion — the point of the suite is to test the adapter, so anything it
/// gets wrong should reach the assertions rather than being smoothed over here.
/// </para>
/// </remarks>
public interface IExecutionResponseConformanceAdapter {
    /// <summary>
    /// Name used in assertion messages so a failure identifies the transport.
    /// </summary>
    string TransportName { get; }

    IExecutionResponse CreateResponse();

    /// <summary>
    /// Runs whatever this transport does to finish a response, and reports what a client receives.
    /// </summary>
    /// <remarks>
    /// This is the assertion boundary. Reading the values back off the <see cref="IExecutionResponse"/>
    /// would prove only that a setter works, and both defects this suite exists for were responses
    /// whose properties were entirely correct and whose clients got something else.
    /// </remarks>
    Task<ObservedResponse> Complete(IExecutionResponse response);
}
