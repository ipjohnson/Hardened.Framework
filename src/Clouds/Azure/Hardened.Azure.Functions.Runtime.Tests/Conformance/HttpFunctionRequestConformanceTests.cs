using Hardened.Azure.Functions.Http;
using Hardened.Azure.Functions.Runtime.Execution;
using Hardened.Azure.Functions.Testing;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Testing.Conformance;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;

namespace Hardened.Azure.Functions.Runtime.Tests.Conformance;

/// <summary>
/// The HTTP trigger's request, held to the web-shaped profile: all twenty-six assertions.
/// </summary>
/// <remarks>
/// The full profile rather than the payload subset, because this transport really does carry a
/// query string and a cookie header and can be asked about both. That is the whole distinction the
/// split encodes - it is about what the transport carried, not about how much of the pipeline the
/// adapter happens to use.
/// </remarks>
public class HttpFunctionRequestConformanceTests : ExecutionRequestConformanceTests {
    protected override IExecutionRequestConformanceAdapter Adapter { get; } = new HttpAdapter_();

    /// <summary>
    /// Builds the request data the worker would have handed over and gives it to the real adapter,
    /// doing nothing else. Anything the mapping gets wrong reaches the assertions rather than being
    /// smoothed over here.
    /// </summary>
    private sealed class HttpAdapter_ : IExecutionRequestConformanceAdapter {
        private readonly HttpAdapter _adapter = new();

        public string TransportName => "Azure Functions HTTP";

        public IExecutionRequest CreateRequest(ConformanceRequestSpec spec) {
            var routed = spec.Path.TrimStart('/');

            // The catch-all route's value goes in the binding data under "path", which is where
            // the adapter reads the path Hardened routes on; the URL carries the host's prefix.
            var context = new TestFunctionContext(
                "Http",
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["path"] = routed },
                new ServiceCollection().BuildServiceProvider());

            var headers = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);

            foreach (var header in spec.Headers) {
                headers[header.Key] = header.Value;
            }

            // The request side of cookies is one header, name=value pairs separated by semicolons,
            // which is what the host sends the worker and what the request data parses.
            if (spec.Cookies.Count > 0) {
                headers["Cookie"] = string.Join("; ", spec.Cookies);
            }

            // Percent-encoded on the way in, because that is what a URL carries, and the request's
            // query is what the worker decodes off it. The suite asserts a "+" in a timestamp and a
            // trailing "=" in base64 survive - which they do because nothing decodes twice.
            var query = spec.QueryString.Count == 0
                ? ""
                : "?" + string.Join("&", spec.QueryString.Select(pair =>
                    Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value)));

            var data = new TestHttpRequestData(
                context,
                spec.Method,
                new Uri("http://functions.test/" + FunctionsWebHost.RoutePrefix + "/" + routed + query),
                headers,
                spec.Body == null ? Stream.Null : new MemoryStream(spec.Body));

            return _adapter.CreateRequest(
                new FunctionsTrigger("HTTP", spec.Path, data, FunctionsDispatch.Web), context);
        }
    }
}
