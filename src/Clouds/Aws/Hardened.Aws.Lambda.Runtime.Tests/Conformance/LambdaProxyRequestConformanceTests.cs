using System.Net;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.ApplicationLoadBalancerEvents;
using Hardened.Aws.Lambda.Http;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Testing.Conformance;

namespace Hardened.Aws.Lambda.Runtime.Tests.Conformance;

/// <summary>
/// The payload format 1.0 request from a REST API, held to the web-shaped profile.
/// </summary>
public class LambdaProxyRequestConformanceTests : ExecutionRequestConformanceTests
{
    protected override IExecutionRequestConformanceAdapter Adapter { get; } = new Gateway();

    private sealed class Gateway : IExecutionRequestConformanceAdapter
    {
        public string TransportName => "Lambda REST API";

        public IExecutionRequest CreateRequest(ConformanceRequestSpec spec)
        {
            var headers = spec.Headers.ToDictionary(
                pair => pair.Key,
                pair => (IList<string>)new List<string> { pair.Value },
                StringComparer.OrdinalIgnoreCase
            );

            // Payload format 1.0 carries cookies in the Cookie header, as a browser sent them.
            if (spec.Cookies.Count > 0)
            {
                headers["Cookie"] = new List<string> { string.Join("; ", spec.Cookies) };
            }

            var proxy = new APIGatewayProxyRequest
            {
                HttpMethod = spec.Method,
                Path = spec.Path,
                MultiValueHeaders = headers,
                // Already decoded, as API Gateway hands them over.
                MultiValueQueryStringParameters =
                    spec.QueryString.Count > 0
                        ? spec.QueryString.ToDictionary(
                            pair => pair.Key,
                            pair => (IList<string>)new List<string> { pair.Value }
                        )
                        : null,
                RequestContext = new APIGatewayProxyRequest.ProxyRequestContext
                {
                    Stage = "prod",
                    DomainName = "api.example.test",
                    Identity = new APIGatewayProxyRequest.RequestIdentity
                    {
                        SourceIp = "203.0.113.7",
                    },
                },
            };

            var body = spec.Body == null ? Stream.Null : new MemoryStream(spec.Body);

            return LambdaProxyRequest.FromApiGateway(proxy, body, protocol: "HTTP/1.1");
        }
    }
}

/// <summary>
/// The Application Load Balancer request, held to the web-shaped profile.
/// </summary>
public class LambdaLoadBalancerRequestConformanceTests : ExecutionRequestConformanceTests
{
    protected override IExecutionRequestConformanceAdapter Adapter { get; } = new Balancer();

    private sealed class Balancer : IExecutionRequestConformanceAdapter
    {
        public string TransportName => "Lambda ALB";

        public IExecutionRequest CreateRequest(ConformanceRequestSpec spec)
        {
            var headers = new Dictionary<string, string>(
                spec.Headers,
                StringComparer.OrdinalIgnoreCase
            );

            if (spec.Cookies.Count > 0)
            {
                headers["Cookie"] = string.Join("; ", spec.Cookies);
            }

            var proxy = new ApplicationLoadBalancerRequest
            {
                HttpMethod = spec.Method,
                Path = spec.Path,
                Headers = headers,
                // Encoded, because a load balancer passes the query string on as the caller wrote
                // it. The suite's "+" and "=" have to survive the one decode the request does.
                QueryStringParameters =
                    spec.QueryString.Count > 0
                        ? spec.QueryString.ToDictionary(
                            pair => WebUtility.UrlEncode(pair.Key),
                            pair => WebUtility.UrlEncode(pair.Value)
                        )
                        : null,
            };

            var body = spec.Body == null ? Stream.Null : new MemoryStream(spec.Body);

            return LambdaProxyRequest.FromLoadBalancer(proxy, body);
        }
    }
}
