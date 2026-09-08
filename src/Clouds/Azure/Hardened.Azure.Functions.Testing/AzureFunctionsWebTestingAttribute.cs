using System.Net;
using DependencyModules.Runtime.Interfaces;
using DependencyModules.Testing.Attributes.Interfaces;
using Hardened.Azure.Functions.Http;
using Hardened.Azure.Functions.Runtime.Execution;
using Hardened.Azure.Functions.Runtime.Hosting;
using Hardened.Requests.Testing;
using Hardened.Shared.Runtime.Application;
using Hardened.Web.Testing;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;

namespace Hardened.Azure.Functions.Testing;

/// <summary>
/// Runs a web application's tests behind the HTTP trigger.
/// </summary>
/// <remarks>
/// <para>
/// The host seam <c>[WebTesting]</c> already has, with the isolated worker's request behind it. A
/// test writes <c>app.Get("/orders/o-1")</c> exactly as it would against Kestrel, and what runs is
/// an <see cref="HttpRequestData"/> through <c>FunctionsInvocationHandler</c> and back out as an
/// <see cref="HttpResponseData"/> - so the adapter, the request it builds, dispatch and the
/// response it answers with are all exercised, and none of it is visible in the test.
/// </para>
/// <para>
/// That is what makes the portability claim checkable rather than asserted: the same test file
/// runs on the pipeline, on Kestrel, behind API Gateway and here, and only an assembly attribute
/// differs.
/// </para>
/// <para>
/// The HTTP adapter's module is loaded beside the application's, deduplicated when the
/// application already carries it, so a test suite whose entry point is the library with the
/// routes - the shape the templates write - runs through the adapter without naming a host
/// project. The invocation handler comes with it, through the runtime module the adapter
/// composes.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class | AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class AzureFunctionsWebTestingAttribute : TestHostAttribute, IDependencyModuleProvider {
    public override ITestHost CreateHost(ITestMethodContext testMethod, IServiceCollection services) =>
        new FunctionsWebHost();

    public IDependencyModule GetModule() => new HttpModule();
}

/// <summary>
/// The HTTP trigger as a test host: a request in as the worker's request data, the worker's
/// response data back out.
/// </summary>
/// <remarks>
/// The URL is built the way the host builds it, under the <c>api</c> route prefix, and the path
/// the catch-all route matched goes in the binding data under <c>path</c>, which is where the
/// adapter reads it. A test that asked for <c>/orders/o-1</c> is therefore routed through the
/// same prefix-stripping a deployed function does.
/// </remarks>
public sealed class FunctionsWebHost : ITestHost {
    /// <summary>The host's default route prefix, which the adapter has to take off.</summary>
    public const string RoutePrefix = "api";

    private IServiceProvider? _provider;

    /// <summary>
    /// Terminal. The HTTP trigger has nothing behind it to hand an unmatched path to, so a path
    /// with no route is a 404 here exactly as it is in a deployed function.
    /// </summary>
    public bool IsTerminal => true;

    /// <summary>
    /// What a relative path resolves against. Nothing sends to it - the request never reaches a
    /// socket - so it only has to be a well-formed base.
    /// </summary>
    public Uri BaseAddress { get; } = new("http://functions.test/");

    /// <remarks>
    /// Dispatch is not appended here, unlike the pipeline host: <c>FunctionsInvocationHandler</c>
    /// installs it on its first invocation, which is the same thing a deployed function does and
    /// the path worth exercising.
    /// </remarks>
    public Task StartAsync(IServiceProvider provider, CancellationToken cancellationToken) {
        _provider = provider;

        return ApplicationLogic.Start(provider, null);
    }

    public HttpMessageHandler CreateHandler(TestCredential? credential) =>
        new HostHandler(this, credential);

    public async Task<TestWebResponse> SendAsync(
        TestHostRequest request, CancellationToken cancellationToken) {
        var handler = Provider.GetRequiredService<FunctionsInvocationHandler>();

        var split = request.PathAndQuery.IndexOf('?');

        var path = split < 0 ? request.PathAndQuery : request.PathAndQuery.Substring(0, split);
        var query = split < 0 ? "" : request.PathAndQuery.Substring(split);

        var routed = path.TrimStart('/');

        var context = new TestFunctionContext(
            "Http",
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["path"] = routed },
            Provider,
            cancellationToken);

        var data = new TestHttpRequestData(
            context,
            request.Method,
            new Uri(BaseAddress, RoutePrefix + "/" + routed + query),
            request.Headers,
            request.Body);

        var answer = await handler.Invoke(
            new FunctionsTrigger("HTTP", "/" + routed, data, FunctionsDispatch.Web), context);

        return new TestWebResponse(Response((HttpResponseData)answer!));
    }

    /// <summary>
    /// The worker's response, back as something <see cref="TestWebResponse"/> reads.
    /// </summary>
    /// <remarks>
    /// Through <see cref="TestExecutionResponse"/> rather than the internal constructor a socket
    /// host would want, for the reason the Lambda host gives: a host of anyone's own can be written
    /// the same way. The cookies become <c>Set-Cookie</c> headers, which is what the host makes of
    /// them.
    /// </remarks>
    private static TestExecutionResponse Response(HttpResponseData data) {
        var body = data.Body;

        if (body.CanSeek) {
            body.Position = 0;
        }

        var response = new TestExecutionResponse(body) {
            Status = (int)data.StatusCode
        };

        foreach (var header in data.Headers) {
            response.Headers[header.Key] = new StringValues(header.Value.ToArray());
        }

        if (data.Cookies is TestHttpCookies cookies && cookies.Cookies.Count > 0) {
            response.Headers["Set-Cookie"] =
                new StringValues(cookies.Cookies.Select(TestHttpCookies.Render).ToArray());
        }

        return response;
    }

    public ValueTask DisposeAsync() => default;

    private IServiceProvider Provider =>
        _provider ?? throw new InvalidOperationException(
            "The Functions web host has not been started, so it has no container to invoke through.");

    /// <summary>Routes an <see cref="HttpClient"/> through the same invocation.</summary>
    private sealed class HostHandler : HttpMessageHandler {
        private readonly FunctionsWebHost _host;
        private readonly TestCredential? _credential;

        public HostHandler(FunctionsWebHost host, TestCredential? credential) {
            _host = host;
            _credential = credential;
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
                cancellationToken);

            var message = new HttpResponseMessage((HttpStatusCode)response.StatusCode) {
                Content = new StreamContent(response.Body)
            };

            foreach (var header in response.Headers) {
                message.Headers.TryAddWithoutValidation(header.Key, header.Value.ToString());
            }

            return message;
        }
    }
}
