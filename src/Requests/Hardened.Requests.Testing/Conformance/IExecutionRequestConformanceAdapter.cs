using Hardened.Requests.Abstract.Execution;

namespace Hardened.Requests.Testing.Conformance;

/// <summary>
/// Implemented once per transport to plug it into <see cref="ExecutionRequestConformanceTests"/>.
///
/// The implementation should do the least work possible beyond building the transport's
/// native request and wrapping it in that transport's <see cref="IExecutionRequest"/> - the
/// point of the suite is to test the adapter, so anything the adapter gets wrong should
/// reach the assertions rather than being smoothed over here.
/// </summary>
public interface IExecutionRequestConformanceAdapter {
    /// <summary>
    /// Name used in assertion messages so a failure identifies the transport.
    /// </summary>
    string TransportName { get; }

    IExecutionRequest CreateRequest(ConformanceRequestSpec spec);
}
