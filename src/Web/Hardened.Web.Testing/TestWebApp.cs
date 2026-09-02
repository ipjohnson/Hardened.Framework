using System.Text.Json;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.Logging;
using Hardened.Requests.Abstract.Metrics;
using Hardened.Requests.Abstract.Middleware;
using Hardened.Requests.Abstract.QueryString;
using Hardened.Requests.Runtime.Headers;
using Hardened.Requests.Runtime.QueryString;
using Hardened.Requests.Testing;
using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Runtime.Diagnostics;
using Hardened.Shared.Runtime.Json;
using Hardened.Shared.Runtime.Metrics;
using Hardened.Shared.Testing.Impl;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace Hardened.Web.Testing;

public class TestWebApp : TestContext, ITestWebApp {
    private readonly IApplicationRoot _applicationRoot;
    private readonly TestCancellationToken _testCancellationToken;

    public TestWebApp(IApplicationRoot applicationRoot, ILogger logger)
        : base(applicationRoot.Provider.GetRequiredService<TestCancellationToken>().Token, logger) {
        _applicationRoot = applicationRoot;
        _testCancellationToken = _applicationRoot.Provider.GetService<TestCancellationToken>()!;
    }

    public IServiceProvider RootServiceProvider => _applicationRoot.Provider;

    public Task<TestWebResponse> Get(string path, Action<TestWebRequest>? webRequest = null) {
        return ExecuteHttpMethod("GET", path, webRequest);
    }

    public Task<TestWebResponse> Post(object postValue, string path, Action<TestWebRequest>? webRequest = null) {
        return ExecuteHttpMethod("POST", path, webRequest, postValue);
    }

    public Task<TestWebResponse> Put(object value, string path, Action<TestWebRequest>? webRequest = null) {
        return ExecuteHttpMethod("PUT", path, webRequest, value);
    }

    public Task<TestWebResponse> Patch(object value, string path, Action<TestWebRequest>? webRequest = null) {
        return ExecuteHttpMethod("PATCH", path, webRequest, value);
    }

    public Task<TestWebResponse> Delete(string path, Action<TestWebRequest>? webRequest = null) {
        return ExecuteHttpMethod("DELETE", path, webRequest, null);
    }

    public Task<TestWebResponse> Request(string method, object? value, string path,
        Action<TestWebRequest>? webRequest = null) {
        return ExecuteHttpMethod(method, path, webRequest, value);
    }

    private async Task<TestWebResponse> ExecuteHttpMethod(string httpMethod, string path,
        Action<TestWebRequest>? webRequest, object? bodyValue = null) {
        _testCancellationToken.Token.ThrowIfCancellationRequested();

        var startTimestamp = MachineTimestamp.Now;

        var middlewareService = _applicationRoot.Provider.GetRequiredService<IMiddlewareService>();
        var requestLogger = _applicationRoot.Provider.GetRequiredService<IRequestLogger>();
        var metricLoggerProvider = _applicationRoot.Provider.GetRequiredService<IMetricLoggerProvider>();

        var responseBody = new MemoryStream();
        var scope = _applicationRoot.Provider.CreateScope();

        var context = CreateContext(
            httpMethod, path, webRequest, responseBody, scope,
            metricLoggerProvider.CreateLogger("test-session"));

        // A default rather than an override. It used to be assigned unconditionally, after the
        // caller's own configuration had run, so a test setting this header had it silently
        // replaced - which made the uncompressed path of any handler that honours it untestable.
        if (!context.Request.Headers.ContainsKey(KnownHeaders.AcceptEncoding)) {
            context.Request.Headers[KnownHeaders.AcceptEncoding] = KnownEncoding.GZip;
        }

        if (bodyValue != null && string.IsNullOrEmpty(context.Request.ContentType)) {
            context.Request.Headers[KnownHeaders.ContentType] = KnownContentType.Js;
        }

        context.Request.Body = SetupBodyStream(bodyValue);

        var chain = middlewareService.GetExecutionChain(context);

        requestLogger.RequestBegin(context);

        try {
            await chain.Next();
        }
        catch (Exception e) {
            Console.WriteLine(e);
            throw;
        }
        finally {
            // In a finally because these ran as straight-line statements after the chain, so a
            // request that threw closed out nothing: no duration, no end, and the scope leaked.
            context.RequestMetrics.Record(
                RequestMetrics.TotalRequestDuration, startTimestamp.GetElapsedMilliseconds());

            requestLogger.RequestEnd(context);

            // The logger is per request and nothing else owns it. Disposal is how a provider learns
            // the request finished, so a harness assertion about what a request emitted has nothing
            // to read without it.
            context.RequestMetrics.Dispose();

            scope.Dispose();
        }

        responseBody.Position = 0;

        return new TestWebResponse(context.Response);
    }

    private Stream SetupBodyStream(object? bodyValue) {
        if (bodyValue == null)
            return Stream.Null;

        // A string goes on the wire as itself. Serializing it would produce a quoted JSON string,
        // which is right for a JSON body and wrong for every other content type a test wants to
        // send - a form body, plain text, anything hand-written. RawResponseSerializer makes the
        // same call in the other direction: a string is text, not a document about a string.
        if (bodyValue is string raw) {
            return new MemoryStream(System.Text.Encoding.UTF8.GetBytes(raw));
        }

        // And bytes go as themselves too, for a body that is not text at all. Without this they
        // serialized as a JSON array of numbers, so an upload, a truncated payload or a byte
        // sequence that is not valid UTF-8 could only be exercised against a live socket.
        if (bodyValue is byte[] bytes) {
            return new MemoryStream(bytes);
        }

        if (bodyValue is ReadOnlyMemory<byte> memory) {
            return new MemoryStream(memory.ToArray());
        }

        // Resolve IJsonSerializer first so its constructor populates the
        // shared JsonSerializerConfiguration.Options TypeInfoResolverChain
        // with the source-gen contexts the application has registered. The
        // options instance held by IJsonSerializerConfiguration is the same
        // one mutated by AotJsonSerializer/JsonSerializerImpl on construction.
        var serializer = _applicationRoot.Provider.GetRequiredService<IJsonSerializer>();
        var memoryStream = new MemoryStream();
        serializer.SerializeAsync(memoryStream, bodyValue, false, CancellationToken.None)
            .GetAwaiter().GetResult();
        memoryStream.Position = 0;

        return memoryStream;
    }

    private IExecutionContext CreateContext(
        string httpMethod,
        string path,
        Action<TestWebRequest>? webRequest,
        MemoryStream responseBody,
        IServiceScope serviceScope,
        IMetricLogger metricLogger) {
        var header = new Dictionary<string, StringValues>();

        var testWebRequest = new TestWebRequest { Headers = header };

        webRequest?.Invoke(testWebRequest);

        testWebRequest.Token ??= _testCancellationToken.Token;

        var pathMinusQuery = path;
        var questionMark = path.IndexOf('?');
        if (questionMark > -1) {
            pathMinusQuery = path.Substring(0, questionMark);
        }

        // Decoded, because a transport hands one over decoded. Passing it through undecoded made
        // /events/%20 reach the handler as the literal three characters and match nothing, while
        // the same request over a socket decoded to whitespace and reached the validator - which
        // is the divergence a harness exists not to have. See RequestPathDecoder for the rule and
        // where it was measured.
        pathMinusQuery = RequestPathDecoder.Decode(pathMinusQuery);

        // Read from the headers the caller set rather than passed as "", which is what it used to
        // be. TestExecutionRequest takes Accept as a constructor argument instead of parsing it, so
        // hardcoding it here meant a test could set the header and the pipeline would never see it -
        // silently, since an empty Accept is a legitimate request. Content negotiation was
        // untestable through this host, which is how a serializer that ignored Accept entirely
        // passed every integration test it had.
        header.TryGetValue(KnownHeaders.Accept, out var accept);

        // The Cookie header, as the cookie collection - which is what a real transport hands over.
        // Without this a test can set the header and the pipeline sees no cookies at all, so a
        // cookie-bound parameter is untestable through this host: the same shape of gap that let a
        // serializer ignoring Accept pass every integration test it had.
        var cookies = new List<string>();

        if (header.TryGetValue(KnownHeaders.Cookie, out var cookieHeader)) {
            foreach (var pair in cookieHeader.ToString().Split(';')) {
                var trimmed = pair.Trim();

                if (trimmed.Length > 0) {
                    cookies.Add(trimmed);
                }
            }
        }

        var request =
            new TestExecutionRequest(
                httpMethod, pathMinusQuery, accept.ToString(), ParseQueryStringFromPath(path)) {
                Headers = header,
                Cookies = cookies
            };
        var responseHeaders = new Dictionary<string, StringValues>();
        var response = new TestExecutionResponse(responseBody) { Headers = responseHeaders };

        return new TestExecutionContext(
            _applicationRoot.Provider,
            serviceScope.ServiceProvider,
            serviceScope.ServiceProvider.GetRequiredService<IKnownServices>(),
            request,
            response,
            testWebRequest.Token.Value,
            metricLogger);
    }

    /// <summary>
    /// The same parser the Kestrel host uses - see <see cref="QueryStringParser"/>.
    /// </summary>
    /// <remarks>
    /// This used to be its own implementation, and a worse one: it split on every <c>'='</c> and
    /// stored the raw substring, so it decoded nothing and dropped any pair whose value contained an
    /// <c>'='</c>. The harness's whole value is that it drives the real pipeline, and a request that
    /// answers 400 here and 200 on Kestrel is worse than no harness for that case.
    /// </remarks>
    private static IQueryStringCollection ParseQueryStringFromPath(string path) =>
        QueryStringParser.ParseFromPath(path);
}