using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Testing;
using Hardened.Requests.Testing.Conformance;

namespace Hardened.Requests.Runtime.Tests.Conformance;

/// <summary>
/// Runs the shared response conformance suite against the in-memory transport used by the test
/// harness.
/// </summary>
/// <remarks>
/// This is the transport a Hardened test asserts against, so its answers are the ones an
/// application's own suite believes. That is exactly why it belongs here: when it disagreed with
/// the ASP.NET host, the disagreement did not read as a bug — it read as a passing test.
/// </remarks>
public class InMemoryExecutionResponseConformanceTests : ExecutionResponseConformanceTests {

    protected override IExecutionResponseConformanceAdapter Adapter { get; } = new InMemoryAdapter();

    private class InMemoryAdapter : IExecutionResponseConformanceAdapter {
        public string TransportName => "In-memory";

        public IExecutionResponse CreateResponse() => new TestExecutionResponse(new MemoryStream());

        /// <summary>
        /// There is no wire, so completion is reading back what the harness hands a test — which is
        /// what <c>TestWebResponse</c> does, and therefore what an application's assertions see.
        /// </summary>
        public Task<ObservedResponse> Complete(IExecutionResponse response) {
            var body = (MemoryStream)response.Body;

            var headers = response.Headers.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyList<string>)pair.Value.ToArray()!,
                StringComparer.OrdinalIgnoreCase);

            headers.TryGetValue("Set-Cookie", out var setCookies);

            return Task.FromResult(new ObservedResponse(
                response.Status ?? 200,
                headers,
                setCookies ?? Array.Empty<string>(),
                body.ToArray()));
        }
    }
}
