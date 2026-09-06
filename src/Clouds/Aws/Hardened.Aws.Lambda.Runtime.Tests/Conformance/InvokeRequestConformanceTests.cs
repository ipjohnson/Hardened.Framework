using Hardened.Aws.Lambda.Runtime.Adapters;
using Hardened.Requests.Abstract.Execution;
using Hardened.Aws.Lambda.Runtime.Tests.Infrastructure;
using Hardened.Requests.Testing.Conformance;

namespace Hardened.Aws.Lambda.Runtime.Tests.Conformance;

/// <summary>
/// The direct-invoke adapter, held to the payload-shaped profile.
/// </summary>
/// <remarks>
/// <para>
/// The first AWS enrolment there has ever been. Hardened.Amz enrolled in none of the three suites,
/// and the reason was structural rather than an oversight: with the mapping spread across a host, a
/// processor, an execution context and a request class, there was no single object to hand a suite.
/// An adapter is that object.
/// </para>
/// <para>
/// The payload profile rather than the web one, because a direct invocation carries no query string
/// and no cookies. The three assertions the web profile adds need the transport to have carried a
/// value, and no adapter for this shape can make them true.
/// </para>
/// </remarks>
public class InvokeRequestConformanceTests : PayloadExecutionRequestConformanceTests {
    protected override IExecutionRequestConformanceAdapter Adapter { get; } = new InvokeAdapter_();

    /// <summary>
    /// Does the least work possible beyond building the native input and handing it to the real
    /// adapter, so anything the adapter gets wrong reaches the assertions rather than being
    /// smoothed over here.
    /// </summary>
    private sealed class InvokeAdapter_ : IExecutionRequestConformanceAdapter {
        private readonly InvokeAdapter _adapter = new();

        public string TransportName => "Lambda direct invoke";

        public IExecutionRequest CreateRequest(ConformanceRequestSpec spec) {
            // The client context's custom values are the only header-like channel a direct
            // invocation has, so that is where the spec's headers go. A caller using the SDK sets
            // them; an event source sets none.
            var context = new TestLambdaContext(spec.Headers);

            var body = spec.Body == null ? Stream.Null : new MemoryStream(spec.Body);

            var request = _adapter.CreateRequest(body, context);

            // The suite names a method and a path the way a web transport would, and this shape
            // derives both rather than receiving them: the scheme is the adapter's, the path is the
            // function name. So the spec's are applied through Clone, the same door a filter uses.
            //
            // Worth being straight about the cost: MethodIsSurfaced and PathIsSurfaced therefore
            // exercise Clone rather than CreateRequest for this shape. Twenty-one of the
            // twenty-three still hold the adapter itself to the contract, and the alternative -
            // asking a direct invocation to surface "GET /" - is asking it to lie.
            return request.Clone(method: spec.Method, path: spec.Path);
        }
    }

}
