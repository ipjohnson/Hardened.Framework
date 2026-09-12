using Amazon.Lambda.APIGatewayEvents;
using Hardened.Aws.Lambda.Runtime.Execution;
using Hardened.Aws.Lambda.Http;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Testing.Conformance;

namespace Hardened.Aws.Lambda.Runtime.Tests.Conformance;

/// <summary>
/// The Lambda HTTP request, held to the web-shaped profile: all twenty-six assertions.
/// </summary>
/// <remarks>
/// The full profile rather than the payload subset, because this transport really does carry a
/// query string and a cookie header and can be asked about both. That is the whole distinction the
/// split encodes - it is about what the transport carried, not about how much of the pipeline the
/// adapter happens to use.
/// </remarks>
public class LambdaHttpRequestConformanceTests : ExecutionRequestConformanceTests {
    protected override IExecutionRequestConformanceAdapter Adapter { get; } = new HttpAdapter_();

    /// <summary>
    /// Builds the proxy event the front door would have sent and hands it to the real request,
    /// nothing else. Anything the mapping gets wrong reaches the assertions rather than being
    /// smoothed over here.
    /// </summary>
    private sealed class HttpAdapter_ : IExecutionRequestConformanceAdapter {
        public string TransportName => "Lambda HTTP";

        public IExecutionRequest CreateRequest(ConformanceRequestSpec spec) {
            var proxy = new APIGatewayHttpApiV2ProxyRequest {
                RawPath = spec.Path,
                Version = "2.0",
                Headers = new Dictionary<string, string>(spec.Headers, StringComparer.OrdinalIgnoreCase),
                RequestContext = new APIGatewayHttpApiV2ProxyRequest.ProxyRequestContext {
                    Http = new APIGatewayHttpApiV2ProxyRequest.HttpDescription {
                        Method = spec.Method,
                        Path = spec.Path,
                        Protocol = "HTTP/1.1",
                        SourceIp = "203.0.113.7"
                    },
                    DomainName = "api.example.test"
                }
            };

            // Payload format 2.0 carries queryStringParameters already percent-decoded, so they
            // go across as themselves. Encoding them here would be testing this file's encoder
            // rather than the request's decoding, and the suite asserts a "+" in a timestamp and a
            // trailing "=" in base64 survive - which they do because nothing decodes twice.
            if (spec.QueryString.Count > 0) {
                proxy.QueryStringParameters = new Dictionary<string, string>(spec.QueryString);
            }

            // Payload format 2.0 carries cookies as their own array rather than a Cookie header,
            // and omits the field entirely when there are none - which is the null the request
            // turns into an empty list.
            if (spec.Cookies.Count > 0) {
                proxy.Cookies = spec.Cookies.ToArray();
            }

            var body = spec.Body == null ? Stream.Null : new MemoryStream(spec.Body);

            return new LambdaHttpRequest(proxy, body);
        }
    }
}
