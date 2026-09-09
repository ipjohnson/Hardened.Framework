using System.Net;
using System.Text;
using System.Text.Json;
using Amazon.Lambda.Core;
using DependencyModules.Testing.Attributes.Interfaces;
using Hardened.Aws.Lambda.Runtime.Hosting;
using Hardened.Requests.Testing;
using Hardened.Shared.Runtime.Application;
using Hardened.Web.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using Hardened.Web.Runtime.Responses;

namespace Hardened.Aws.Lambda.Testing;

/// <summary>
/// Runs a web application's tests behind API Gateway.
/// </summary>
/// <remarks>
/// <para>
/// The host seam <c>[WebTesting]</c> already has, with Lambda behind it. A test writes
/// <c>app.Get("/orders/o-1")</c> exactly as it would against Kestrel, and what runs is a real
/// payload format 2.0 event through <c>LambdaInvocationHandler</c> and back out as a proxy
/// response - so the adapter, the request it builds, dispatch and the response writer are all
/// exercised, and none of it is visible in the test.
/// </para>
/// <para>
/// That is what makes the portability claim checkable rather than asserted: the same test file
/// runs on the pipeline, on Kestrel and here, and only an assembly attribute differs.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class | AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class LambdaWebTestingAttribute : TestHostAttribute {
    public override ITestHost CreateHost(ITestMethodContext testMethod, IServiceCollection services) =>
        new LambdaWebHost();
}

/// <summary>
/// API Gateway as a test host: a request in as a proxy event, a proxy response back out.
/// </summary>
public sealed class LambdaWebHost : ITestHost {
    private IServiceProvider? _provider;
    private ITestContainerSource? _source;
    private bool _started;

    /// <summary>
    /// Terminal. API Gateway has nothing behind it to hand an unmatched path to, so a path with no
    /// route is a 404 here exactly as it is in a deployed function.
    /// </summary>
    public bool IsTerminal => true;

    /// <summary>
    /// A container per request, because an execution environment is not promised between
    /// invocations.
    /// </summary>
    /// <remarks>
    /// The case the whole boundary exists for. Two requests to a deployed function may be served by
    /// two environments or by one, and nothing says which, so a handler that leaned on what the
    /// previous request left in a singleton fails here rather than intermittently in production.
    /// </remarks>
    public TestContainerPolicy ContainerPolicy => TestContainerPolicy.PerInvocation;

    /// <summary>
    /// What a relative path resolves against. Nothing sends to it - the request never reaches a
    /// socket - so it only has to be a well-formed base.
    /// </summary>
    public Uri BaseAddress { get; } = new("http://apigateway.test/");

    /// <remarks>
    /// Dispatch is not appended here, unlike the pipeline host: <c>LambdaInvocationHandler</c>
    /// installs it on its first invocation, which is the same thing a deployed function does and
    /// the path worth exercising.
    /// </remarks>
    public Task StartAsync(IServiceProvider provider, CancellationToken cancellationToken) {
        if (_started) {
            return Task.CompletedTask;
        }

        _started = true;
        _provider = provider;
        _source = provider.GetService<ITestContainerSource>();

        return ApplicationLogic.Start(provider, null);
    }

    /// <summary>
    /// The container one invocation runs against.
    /// </summary>
    /// <remarks>
    /// Its <c>LambdaInvocationHandler</c> comes with it, which is the point: the handler installs
    /// dispatch on its first invocation and holds a flag saying it has, so a fresh handler on a
    /// fresh container is a cold environment doing what a cold environment does.
    /// </remarks>
    private async ValueTask<IServiceProvider> ContainerForRequestAsync(bool reuse = false) {
        if (_source is not { } source) {
            return Provider;
        }

        // The test's own container, for the reason PipelineHost gives: it is where every pinned
        // parameter came from, so a client asked to reuse lands where the test already is. That is
        // also the honest picture of a warm sandbox, which is one environment rather than a second
        // one nobody named.
        return reuse ? Provider : await source.CreateAsync();
    }

    public HttpMessageHandler CreateHandler(TestCredential? credential) =>
        new HostHandler(this, credential);

    public HttpMessageHandler CreateHandler(TestCredential? credential, bool reuseContainer) =>
        new HostHandler(this, credential, reuseContainer);

    public Task<TestWebResponse> SendAsync(
        TestHostRequest request, CancellationToken cancellationToken) =>
        SendAsync(request, cancellationToken, reuseContainer: false);

    public async Task<TestWebResponse> SendAsync(
        TestHostRequest request, CancellationToken cancellationToken, bool reuseContainer) {
        var provider = await ContainerForRequestAsync(reuseContainer);

        var handler = provider.GetRequiredService<LambdaInvocationHandler>();

        using var input = new MemoryStream(Encoding.UTF8.GetBytes(Event(request)));

        var output = await handler.Invoke(input, new TestContext());

        using var proxy = JsonDocument.Parse(output);

        return new TestWebResponse(Response(proxy.RootElement));
    }

    /// <summary>
    /// The request as API Gateway would have delivered it.
    /// </summary>
    /// <remarks>
    /// The wire shape rather than a serialized DTO, for the reason the adapter tests give: a
    /// fixture built by round-tripping <c>APIGatewayHttpApiV2ProxyRequest</c> would agree with the
    /// type it came from rather than with AWS.
    /// </remarks>
    private static string Event(TestHostRequest request) {
        var split = request.PathAndQuery.IndexOf('?');

        var path = split < 0 ? request.PathAndQuery : request.PathAndQuery.Substring(0, split);
        var query = split < 0 ? "" : request.PathAndQuery.Substring(split + 1);

        var body = new StreamReader(request.Body).ReadToEnd();

        var headers = string.Join(
            ",",
            request.Headers.Select(header =>
                JsonSerializer.Serialize(header.Key) + ":" +
                JsonSerializer.Serialize(header.Value.ToString())));

        var parameters = string.Join(
            ",",
            query.Split('&', StringSplitOptions.RemoveEmptyEntries).Select(pair => {
                var equals = pair.IndexOf('=');
                var name = equals < 0 ? pair : pair.Substring(0, equals);
                var value = equals < 0 ? "" : pair.Substring(equals + 1);

                // Decoded, because API Gateway hands over queryStringParameters already decoded and
                // the adapter deliberately does not decode a second time.
                return JsonSerializer.Serialize(WebUtility.UrlDecode(name)) + ":" +
                       JsonSerializer.Serialize(WebUtility.UrlDecode(value));
            }));

        var http =
            $$"""
              {"method":{{JsonSerializer.Serialize(request.Method)}},
               "path":{{JsonSerializer.Serialize(path)}},
               "protocol":"HTTP/1.1","sourceIp":"203.0.113.7"}
              """;

        return $$"""
            {"version":"2.0",
             "rawPath":{{JsonSerializer.Serialize(path)}},
             "rawQueryString":{{JsonSerializer.Serialize(query)}},
             "headers":{ {{headers}} },
             "queryStringParameters":{ {{parameters}} },
             "requestContext":{"domainName":"apigateway.test","http":{{http}} },
             "body":{{JsonSerializer.Serialize(body)}},
             "isBase64Encoded":false}
            """;
    }

    /// <summary>
    /// The proxy response, back as something <see cref="TestWebResponse"/> reads.
    /// </summary>
    /// <remarks>
    /// Through <see cref="TestExecutionResponse"/> rather than the internal constructor a socket
    /// host would want, so this needs no access to Hardened.Web.Testing's internals - a host of
    /// anyone's own can be written the same way.
    /// </remarks>
    private static TestExecutionResponse Response(JsonElement proxy) {
        var body = proxy.TryGetProperty("body", out var text) && text.ValueKind == JsonValueKind.String
            ? Encoding.UTF8.GetBytes(text.GetString()!)
            : Array.Empty<byte>();

        var response = new TestExecutionResponse(new MemoryStream(body, writable: false)) {
            Status = proxy.TryGetProperty("statusCode", out var status) ? status.GetInt32() : 200
        };

        if (proxy.TryGetProperty("headers", out var headers) &&
            headers.ValueKind == JsonValueKind.Object) {
            foreach (var header in headers.EnumerateObject()) {
                response.Headers[header.Name] = new StringValues(header.Value.GetString());
            }
        }

        return response;
    }

    public ValueTask DisposeAsync() => default;

    private IServiceProvider Provider =>
        _provider ?? throw new InvalidOperationException(
            "The Lambda web host has not been started, so it has no container to invoke through.");

    /// <summary>Routes an <see cref="HttpClient"/> through the same invocation.</summary>
    private sealed class HostHandler : HttpMessageHandler {
        private readonly LambdaWebHost _host;
        private readonly TestCredential? _credential;
        private readonly bool _reuseContainer;

        public HostHandler(
            LambdaWebHost host, TestCredential? credential, bool reuseContainer = false) {
            _host = host;
            _credential = credential;
            _reuseContainer = reuseContainer;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) {
            var headers = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);

            foreach (var header in request.Headers) {
                headers[header.Key] = new StringValues(header.Value.ToArray());
            }

            var body = request.Content == null
                ? Stream.Null
                : await request.Content.ReadAsStreamAsync(cancellationToken);

            var response = await _host.SendAsync(
                new TestHostRequest(
                    request.Method.Method,
                    request.RequestUri!.PathAndQuery,
                    headers,
                    body,
                    _credential),
                cancellationToken,
                _reuseContainer);

            var message = new HttpResponseMessage((HttpStatusCode)response.StatusCode) {
                Content = new StreamContent(response.Body)
            };

            foreach (var header in response.Headers) {
                message.Headers.TryAddWithoutValidation(header.Key, header.Value.ToString());
            }

            return message;
        }
    }

    /// <summary>Enough context to invoke, with a deadline the host turns into a token.</summary>
    private sealed class TestContext : ILambdaContext {
        public string AwsRequestId => Guid.NewGuid().ToString();
        public IClientContext ClientContext => null!;
        public string FunctionName => "web-test";
        public string FunctionVersion => "$LATEST";
        public ICognitoIdentity Identity => null!;
        public string InvokedFunctionArn => "arn:aws:lambda:us-east-1:123456789012:function:web-test";
        public ILambdaLogger Logger => null!;
        public string LogGroupName => "/aws/lambda/web-test";
        public string LogStreamName => "test";
        public int MemoryLimitInMB => 512;
        public TimeSpan RemainingTime => TimeSpan.FromSeconds(30);
    }
}
