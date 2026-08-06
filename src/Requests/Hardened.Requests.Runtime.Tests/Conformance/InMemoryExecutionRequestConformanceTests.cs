using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Runtime.QueryString;
using Hardened.Requests.Testing;
using Hardened.Requests.Testing.Conformance;
using Microsoft.Extensions.Primitives;

namespace Hardened.Requests.Runtime.Tests.Conformance;

/// <summary>
/// Runs the shared transport conformance suite against the in-memory transport used by the
/// test harness. This transport has no wire format of its own - values are supplied
/// directly - so it acts as the reference implementation the real transports are held to.
/// </summary>
public class InMemoryExecutionRequestConformanceTests : ExecutionRequestConformanceTests {

    protected override IExecutionRequestConformanceAdapter Adapter { get; } = new InMemoryAdapter();

    private class InMemoryAdapter : IExecutionRequestConformanceAdapter {
        public string TransportName => "In-memory";

        public IExecutionRequest CreateRequest(ConformanceRequestSpec spec) {
            var headers = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);

            foreach (var header in spec.Headers) {
                headers[header.Key] = new StringValues(header.Value);
            }

            // Accept is a constructor parameter on this transport rather than being parsed
            // out of a header block, so translating the spec means passing it through.
            spec.Headers.TryGetValue("Accept", out var accept);

            var queryString = new SimpleQueryStringCollection(
                spec.QueryString.ToDictionary(q => q.Key, q => q.Value));

            return new TestExecutionRequest(spec.Method, spec.Path, accept, queryString) {
                Headers = headers,
                Cookies = spec.Cookies.ToList(),
                Body = spec.Body is null ? Stream.Null : new MemoryStream(spec.Body)
            };
        }
    }
}
