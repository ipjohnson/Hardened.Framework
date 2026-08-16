using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Testing.Conformance;
using Hardened.Web.Kestrel.Runtime.Impl;
using Hardened.Web.Kestrel.Runtime.Tests.Impl;

namespace Hardened.Web.Kestrel.Runtime.Tests.Conformance;

/// <summary>
/// Runs the shared response conformance suite against the Kestrel adapter.
/// </summary>
/// <remarks>
/// Driven through the same hand-rolled feature collection the rest of this project uses, so the
/// response writes to <c>IHttpResponseFeature</c> and <c>IHttpResponseBodyFeature</c> exactly as it
/// does under a real server — including <c>CompleteAsync</c>, which is what sends the headers of a
/// response that wrote no body.
/// </remarks>
public class FeatureExecutionResponseConformanceTests : ExecutionResponseConformanceTests {

    protected override IExecutionResponseConformanceAdapter Adapter { get; } = new KestrelAdapter();

    /// <summary>
    /// Holds the features it built. Safe because xUnit constructs a new test class instance — and
    /// so a new adapter — per test method, and it is what lets a clone complete against the same
    /// response a fork would answer on.
    /// </summary>
    private class KestrelAdapter : IExecutionResponseConformanceAdapter {
        private ServerFeatures? _features;

        public string TransportName => "Kestrel";

        public IExecutionResponse CreateResponse() {
            _features = new ServerFeatures();

            return new FeatureExecutionResponse(_features.Response, _features.ResponseBody);
        }

        /// <summary>
        /// <c>CompleteAsync</c> is not optional here, and is the reason this transport never had the
        /// ASP.NET host's defect: a response that wrote no body never sends its headers otherwise,
        /// leaving the connection waiting on a request the application already considers finished.
        /// </summary>
        public async Task<ObservedResponse> Complete(IExecutionResponse response) {
            await ((FeatureExecutionResponse)response).CompleteAsync();

            var feature = _features!.Response;

            var headers = feature.Headers.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyList<string>)pair.Value.ToArray()!,
                StringComparer.OrdinalIgnoreCase);

            headers.TryGetValue("Set-Cookie", out var setCookies);

            return new ObservedResponse(
                feature.StatusCode,
                headers,
                setCookies ?? Array.Empty<string>(),
                _features.Body.ToArray());
        }
    }
}
