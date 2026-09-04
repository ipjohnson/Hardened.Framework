using System.Reflection;
using System.Runtime.CompilerServices;
using Hardened.Requests.Abstract.Headers;
using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Runtime.Json;
using Hardened.Shared.Testing.Impl;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace Hardened.Web.Testing;

public class TestWebApp : TestContext, ITestWebApp {
    private readonly IApplicationRoot _applicationRoot;
    private readonly TestCancellationToken _testCancellationToken;
    private readonly TestCredential? _credential;
    private readonly Assembly? _testAssembly;

    public TestWebApp(IApplicationRoot applicationRoot, ILogger logger)
        : this(applicationRoot, logger, null, null) {
    }

    /// <param name="credential">
    /// The credential the test's attributes resolved to, sent on every request whose configure
    /// callback set neither test header, and on every client this instance hands out.
    /// </param>
    /// <param name="testAssembly">
    /// Where <see cref="CreateClient{TClient}"/> looks for an <see cref="ITestClientFactory{TClient}"/>.
    /// The calling assembly when null.
    /// </param>
    public TestWebApp(
        IApplicationRoot applicationRoot, ILogger logger, TestCredential? credential, Assembly? testAssembly)
        : base(applicationRoot.Provider.GetRequiredService<TestCancellationToken>().Token, logger) {
        _applicationRoot = applicationRoot;
        _testCancellationToken = _applicationRoot.Provider.GetService<TestCancellationToken>()!;
        _credential = credential;
        _testAssembly = testAssembly;
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

    public HttpClient CreateHttpClient(TestCredential? credential = null) =>
        TestClientBuilder.CreateHttpClient(_applicationRoot.Provider, credential ?? _credential);

    /// <remarks>
    /// The factory is looked for in the test assembly the harness was built for, and for an
    /// instance built by hand in the assembly that is calling - which is the test assembly too,
    /// and stays so because the method is never inlined into its caller.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public TClient CreateClient<TClient>(TestCredential? credential = null) where TClient : class =>
        (TClient)TestClientBuilder.Build(
            typeof(TClient),
            CreateHttpClient(credential),
            _testAssembly ?? Assembly.GetCallingAssembly());

    private async Task<TestWebResponse> ExecuteHttpMethod(string httpMethod, string path,
        Action<TestWebRequest>? webRequest, object? bodyValue = null) {
        _testCancellationToken.Token.ThrowIfCancellationRequested();

        // Case-insensitive, as a transport's are: a test that sets content-type and a pipeline
        // that reads Content-Type are talking about one header.
        var headers = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);

        var testWebRequest = new TestWebRequest { Headers = headers };

        webRequest?.Invoke(testWebRequest);

        testWebRequest.Token ??= _testCancellationToken.Token;

        // A default rather than an override. It used to be assigned unconditionally, after the
        // caller's own configuration had run, so a test setting this header had it silently
        // replaced - which made the uncompressed path of any handler that honours it untestable.
        if (!headers.ContainsKey(KnownHeaders.AcceptEncoding)) {
            headers[KnownHeaders.AcceptEncoding] = KnownEncoding.GZip;
        }

        var hasBody = testWebRequest.Body != null || bodyValue != null;

        if (hasBody && !headers.ContainsKey(KnownHeaders.ContentType)) {
            headers[KnownHeaders.ContentType] = KnownContentType.Js;
        }

        var body = testWebRequest.Body != null
            ? new MemoryStream(testWebRequest.Body)
            : SetupBodyStream(bodyValue);

        var request = PipelineRequest.CreateRequest(httpMethod, path, headers, body, _credential);
        var responseBody = new MemoryStream();

        var response = await PipelineRequest.Run(
            _applicationRoot.Provider, request, responseBody, testWebRequest.Token.Value);

        return new TestWebResponse(response);
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
}
