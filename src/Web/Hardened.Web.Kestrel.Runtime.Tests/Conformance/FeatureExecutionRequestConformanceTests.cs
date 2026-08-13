using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Testing.Conformance;
using Hardened.Web.Kestrel.Runtime.Impl;
using Microsoft.AspNetCore.Http.Features;

namespace Hardened.Web.Kestrel.Runtime.Tests.Conformance;

/// <summary>
/// Runs the shared transport conformance suite against the Kestrel adapter.
///
/// This is the suite's whole purpose: a request carried by Kestrel has to arrive at the pipeline
/// as the same <see cref="IExecutionRequest"/> as one carried by ASP.NET Core or API Gateway.
/// The Kestrel adapter reads the server's <see cref="IHttpRequestFeature"/> directly rather than
/// going through an <c>HttpContext</c>, and parses the raw query string itself instead of
/// converting ASP.NET's parsed collection, so it is exactly the kind of adapter where a
/// divergence could go unnoticed without this.
/// </summary>
public class FeatureExecutionRequestConformanceTests : ExecutionRequestConformanceTests {

    protected override IExecutionRequestConformanceAdapter Adapter { get; } = new KestrelAdapter();

    private class KestrelAdapter : IExecutionRequestConformanceAdapter {
        public string TransportName => "Kestrel";

        public IExecutionRequest CreateRequest(ConformanceRequestSpec spec) {
            var feature = new HttpRequestFeature {
                Method = spec.Method,
                Path = spec.Path
            };

            foreach (var header in spec.Headers) {
                feature.Headers[header.Key] = header.Value;
            }

            // Built the way it arrives on the wire — percent-encoded — because that is what
            // Kestrel hands over and what the adapter has to decode.
            if (spec.QueryString.Count > 0) {
                feature.QueryString = "?" + string.Join("&", spec.QueryString.Select(pair =>
                    $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
            }

            // Cookies arrive in a single Cookie header, which is the form the adapter splits.
            if (spec.Cookies.Count > 0) {
                feature.Headers.Cookie = string.Join("; ", spec.Cookies);
            }

            if (spec.Body is not null) {
                feature.Body = new MemoryStream(spec.Body);
                feature.Headers.ContentLength = spec.Body.Length;
            }

            return new FeatureExecutionRequest(feature);
        }
    }
}
