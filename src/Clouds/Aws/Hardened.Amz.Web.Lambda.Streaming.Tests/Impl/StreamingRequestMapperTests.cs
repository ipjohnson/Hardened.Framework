using System.IO.Pipelines;
using Amazon.Lambda.APIGatewayEvents;
using Hardened.Amz.Web.Lambda.Streaming.Impl;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.Outputs;
using Hardened.Shared.Runtime.Metrics;
using Microsoft.Extensions.Primitives;
using NSubstitute;
using Xunit;

namespace Hardened.Amz.Web.Lambda.Streaming.Tests.Impl;

public class StreamingRequestMapperTests {
    private readonly IKnownServices _knownServices;
    private readonly StreamingRequestMapper _mapper;

    public StreamingRequestMapperTests() {
        _knownServices = Substitute.For<IKnownServices>();
        _mapper = new StreamingRequestMapper(_knownServices);
    }

    private static APIGatewayHttpApiV2ProxyRequest CreateProxyRequest(
        string method = "GET",
        string rawPath = "/test",
        string? body = null,
        bool isBase64Encoded = false,
        string? stage = null,
        Dictionary<string, string>? headers = null,
        Dictionary<string, string>? queryStringParameters = null,
        string[]? cookies = null) {
        return new APIGatewayHttpApiV2ProxyRequest {
            RawPath = rawPath,
            Body = body,
            IsBase64Encoded = isBase64Encoded,
            Headers = headers ?? new Dictionary<string, string>(),
            QueryStringParameters = queryStringParameters,
            Cookies = cookies,
            RequestContext = new APIGatewayHttpApiV2ProxyRequest.ProxyRequestContext {
                Http = new APIGatewayHttpApiV2ProxyRequest.HttpDescription {
                    Method = method,
                    Path = rawPath
                },
                Stage = stage
            }
        };
    }

    [Fact]
    public void CreateExecutionContext_ReturnsValidContext() {
        var proxyRequest = CreateProxyRequest();
        var rootProvider = Substitute.For<IServiceProvider>();
        var scopeProvider = Substitute.For<IServiceProvider>();
        var pipe = new Pipe();
        var responseStream = new ResponseStream(pipe.Writer, (_, _) => { }, () => { });
        var bodyStream = new MemoryStream();
        var metricLogger = Substitute.For<IMetricLogger>();

        var context = _mapper.CreateExecutionContext(
            rootProvider, scopeProvider, proxyRequest,
            responseStream,
            bodyStream, metricLogger);

        Assert.NotNull(context);
        Assert.Same(rootProvider, context.RootServiceProvider);
        Assert.Same(scopeProvider, context.RequestServices);
        Assert.Same(metricLogger, context.RequestMetrics);
    }
}

public class StreamingExecutionRequestTests {
    private static APIGatewayHttpApiV2ProxyRequest CreateProxyRequest(
        string method = "GET",
        string rawPath = "/test",
        string? stage = null,
        Dictionary<string, string>? headers = null,
        Dictionary<string, string>? queryStringParameters = null,
        string[]? cookies = null) {
        return new APIGatewayHttpApiV2ProxyRequest {
            RawPath = rawPath,
            Headers = headers ?? new Dictionary<string, string>(),
            QueryStringParameters = queryStringParameters,
            Cookies = cookies,
            RequestContext = new APIGatewayHttpApiV2ProxyRequest.ProxyRequestContext {
                Http = new APIGatewayHttpApiV2ProxyRequest.HttpDescription {
                    Method = method,
                    Path = rawPath
                },
                Stage = stage
            }
        };
    }

    /// <summary>
    /// A cloned request gets its own parameter bag.
    ///
    /// <para>
    /// <c>IExecutionChain.Fork</c> exists so a filter can re-run a handler against a cloned
    /// context. Sharing the parameter bag means rebinding in the fork overwrites what the
    /// original bound and the two corrupt each other. Every <c>IExecutionRequest.Clone</c> in
    /// both repositories copied the reference until 2026-08-12.
    /// </para>
    ///
    /// <para>
    /// The framework holds every transport to this in
    /// <c>ExecutionRequestConformanceTests</c>. This repository consumes that package from the
    /// feed and the published version predates the case, so it is covered directly here until a
    /// package carrying it ships.
    /// </para>
    /// </summary>
    [Fact]
    public void CloneGivesTheCloneItsOwnParameters() {
        var request = new StreamingExecutionRequest(CreateProxyRequest()) {
            Parameters = new RecordingParameters { Value = "original" }
        };

        var clone = request.Clone(method: "POST");

        Assert.NotNull(clone.Parameters);
        Assert.NotSame(request.Parameters, clone.Parameters);

        ((RecordingParameters)clone.Parameters!).Value = "rebound";

        Assert.Equal("original", ((RecordingParameters)request.Parameters!).Value);
    }

    [Fact]
    public void CloneLeavesNullParametersNull() {
        var request = new StreamingExecutionRequest(CreateProxyRequest()) { Parameters = null };

        Assert.Null(request.Clone(method: "POST").Parameters);
    }

    /// <summary>
    /// Clone accepted all five of its arguments and applied none of them until 2026-08-15 — the
    /// same defect the buffered transport carried, in the same shape. The two tests above have
    /// always passed <c>method: "POST"</c> and asserted only on parameters, which is how it stayed
    /// invisible on both.
    /// </summary>
    [Fact]
    public void CloneRebindsTheMethodAndPathItIsGiven() {
        var request = new StreamingExecutionRequest(CreateProxyRequest(method: "GET", rawPath: "/original"));

        var clone = request.Clone(method: "DELETE", path: "/replaced");

        Assert.Equal("DELETE", clone.Method);
        Assert.Equal("/replaced", clone.Path);
    }

    [Fact]
    public void CloneKeepsTheMethodAndPathWhenGivenNeither() {
        var request = new StreamingExecutionRequest(CreateProxyRequest(method: "PATCH", rawPath: "/original"));

        var clone = request.Clone();

        Assert.Equal("PATCH", clone.Method);
        Assert.Equal("/original", clone.Path);
    }

    [Fact]
    public void CloneRebindsTheHeadersItIsGiven() {
        var request = new StreamingExecutionRequest(CreateProxyRequest());

        var clone = request.Clone(
            headers: new Dictionary<string, StringValues> { { "X-Replaced", "yes" } });

        Assert.True(clone.Headers.TryGetValue("X-Replaced", out var value));
        Assert.Equal("yes", value.ToString());
    }

    [Fact]
    public void RebindingHeadersInACloneDoesNotTouchTheOriginal() {
        var request = new StreamingExecutionRequest(CreateProxyRequest());
        var clone = request.Clone();

        clone.Headers["X-Added-In-Fork"] = "yes";

        Assert.False(request.Headers.ContainsKey("X-Added-In-Fork"));
    }

    [Fact]
    public void CloneRebindsTheCookiesItIsGiven() {
        var request = new StreamingExecutionRequest(CreateProxyRequest());

        var clone = request.Clone(cookies: new[] { "session=replaced" });

        Assert.Contains("session=replaced", clone.Cookies);
    }

    /// <summary>One mutable value, so a test can prove the copy is independent.</summary>
    private class RecordingParameters : IExecutionRequestParameters {
        public string Value { get; set; } = "";

        public object this[int index] {
            get => Value;
            set => Value = (string)value;
        }

        public int ParameterCount => 1;

        public IReadOnlyList<IExecutionRequestParameter> Info { get; } =
            Array.Empty<IExecutionRequestParameter>();

        public bool TryGetParameter(string parameterName, out object? parameterValue) {
            parameterValue = Value;

            return true;
        }

        public bool TrySetParameter(string parameterName, object parameterValue) {
            Value = (string)parameterValue;

            return true;
        }

        public IExecutionRequestParameters Clone() => new RecordingParameters { Value = Value };
    }

    [Fact]
    public void Method_ReturnsCorrectValue() {
        var request = new StreamingExecutionRequest(CreateProxyRequest(method: "POST"));
        Assert.Equal("POST", request.Method);
    }

    [Fact]
    public void Path_ReturnsRawPath() {
        var request = new StreamingExecutionRequest(CreateProxyRequest(rawPath: "/api/items"));
        Assert.Equal("/api/items", request.Path);
    }

    [Fact]
    public void Path_StripsStagePath() {
        var request = new StreamingExecutionRequest(
            CreateProxyRequest(rawPath: "/prod/api/items", stage: "prod"));
        Assert.Equal("/api/items", request.Path);
    }

    [Fact]
    public void Path_KeepsPath_WhenStageIsNull() {
        var request = new StreamingExecutionRequest(
            CreateProxyRequest(rawPath: "/prod/api/items", stage: null));
        Assert.Equal("/prod/api/items", request.Path);
    }

    [Fact]
    public void Path_KeepsPath_WhenStageDoesNotMatch() {
        var request = new StreamingExecutionRequest(
            CreateProxyRequest(rawPath: "/api/items", stage: "prod"));
        Assert.Equal("/api/items", request.Path);
    }

    [Fact]
    public void ContentType_DefaultsToApplicationJson() {
        var request = new StreamingExecutionRequest(CreateProxyRequest());
        Assert.Equal("application/json", request.ContentType);
    }

    [Fact]
    public void ContentType_ReturnsHeaderValue_WhenPresent() {
        var headers = new Dictionary<string, string> {
            { "Content-Type", "text/plain" }
        };
        var request = new StreamingExecutionRequest(CreateProxyRequest(headers: headers));
        Assert.Equal("text/plain", request.ContentType);
    }

    [Fact]
    public void Accept_DefaultsToApplicationJson() {
        var request = new StreamingExecutionRequest(CreateProxyRequest());
        Assert.Equal("application/json", request.Accept);
    }

    [Fact]
    public void Accept_ReturnsHeaderValue_WhenPresent() {
        var headers = new Dictionary<string, string> {
            { "Accept", "text/html" }
        };
        var request = new StreamingExecutionRequest(CreateProxyRequest(headers: headers));
        Assert.Equal("text/html", request.Accept);
    }

    [Fact]
    public void Cookies_ReturnsEmptyList_WhenNull() {
        var request = new StreamingExecutionRequest(
            CreateProxyRequest(cookies: null));
        Assert.Empty(request.Cookies);
    }

    [Fact]
    public void Cookies_ReturnsValues() {
        var cookies = new[] { "session=abc123", "theme=dark" };
        var request = new StreamingExecutionRequest(
            CreateProxyRequest(cookies: cookies));

        Assert.Equal(2, request.Cookies.Count);
        Assert.Equal("session=abc123", request.Cookies[0]);
        Assert.Equal("theme=dark", request.Cookies[1]);
    }

    [Fact]
    public void Body_DefaultsToStreamNull() {
        var request = new StreamingExecutionRequest(CreateProxyRequest());
        Assert.Same(Stream.Null, request.Body);
    }

    [Fact]
    public void PathTokens_DefaultsToEmpty() {
        var request = new StreamingExecutionRequest(CreateProxyRequest());
        Assert.NotNull(request.PathTokens);
    }
}

public class StreamingExecutionResponseTests {
    [Fact]
    public void Status_DefaultsToNull() {
        var response = new StreamingExecutionResponse(new MemoryStream());
        Assert.Null(response.Status);
    }

    [Fact]
    public void ShouldSerialize_DefaultsToTrue() {
        var response = new StreamingExecutionResponse(new MemoryStream());
        Assert.True(response.ShouldSerialize);
    }

    [Fact]
    public void Body_IsSetFromConstructor() {
        var stream = new MemoryStream();
        var response = new StreamingExecutionResponse(stream);
        Assert.Same(stream, response.Body);
    }

    [Fact]
    public void ResponseStarted_IsFalse_WhenStreamPositionIsZero() {
        var response = new StreamingExecutionResponse(new MemoryStream());
        Assert.False(response.ResponseStarted);
    }

    [Fact]
    public void ResponseStarted_IsTrue_WhenStreamHasData() {
        var stream = new MemoryStream();
        stream.WriteByte(1);
        var response = new StreamingExecutionResponse(stream);
        Assert.True(response.ResponseStarted);
    }

    [Fact]
    public void Cookies_Append_AddsCookie() {
        var response = new StreamingExecutionResponse(new MemoryStream());

        response.Cookies.Append("session", "abc123");

        Assert.Single(response.Cookies.Cookies);
        Assert.Equal("abc123", response.Cookies.Cookies["session"].Item1);
    }

    [Fact]
    public void Cookies_Append_OverwritesDuplicate() {
        var response = new StreamingExecutionResponse(new MemoryStream());

        response.Cookies.Append("session", "old");
        response.Cookies.Append("session", "new");

        Assert.Single(response.Cookies.Cookies);
        Assert.Equal("new", response.Cookies.Cookies["session"].Item1);
    }

    [Fact]
    public void Cookies_Append_WithOptions() {
        var response = new StreamingExecutionResponse(new MemoryStream());
        var options = new CookieSetOptions(
            Secure: true,
            HttpOnly: true,
            Path: "/",
            Domain: ".example.com");

        response.Cookies.Append("session", "value", options);

        var cookie = response.Cookies.Cookies["session"];
        Assert.Equal("value", cookie.Item1);
        Assert.True(cookie.Item2.Secure);
        Assert.True(cookie.Item2.HttpOnly);
        Assert.Equal("/", cookie.Item2.Path);
    }

    [Fact]
    public void Clone_PreservesProperties() {
        var body = new MemoryStream();
        var output = Substitute.For<IHardenedResponseOutput>();
        Func<IExecutionContext, IHardenedResponseOutput> factory = _ => output;

        var response = new StreamingExecutionResponse(body) {
            ResponseValue = "test-value",
            Output = output,
            OutputFactory = factory,
            ShouldCompress = true,
            IsBinary = true,
            ShouldSerialize = false,
        };

        var clone = response.Clone(null);

        Assert.Equal("test-value", clone.ResponseValue);
        Assert.Same(output, clone.Output);
        Assert.Same(factory, clone.OutputFactory);
        Assert.True(clone.ShouldCompress);
        Assert.True(clone.IsBinary);
        Assert.False(clone.ShouldSerialize);
    }
}

public class StreamingExecutionContextTests {
    [Fact]
    public void Properties_ReturnConstructorValues() {
        var rootProvider = Substitute.For<IServiceProvider>();
        var scopeProvider = Substitute.For<IServiceProvider>();
        var knownServices = Substitute.For<IKnownServices>();
        var request = Substitute.For<IExecutionRequest>();
        var response = Substitute.For<IExecutionResponse>();
        var metrics = Substitute.For<IMetricLogger>();

        var context = new StreamingExecutionContext(
            rootProvider, scopeProvider, knownServices,
            request, response, metrics,
            Hardened.Shared.Runtime.Diagnostics.MachineTimestamp.Now);

        Assert.Same(rootProvider, context.RootServiceProvider);
        Assert.Same(scopeProvider, context.RequestServices);
        Assert.Same(knownServices, context.KnownServices);
        Assert.Same(request, context.Request);
        Assert.Same(response, context.Response);
        Assert.Same(metrics, context.RequestMetrics);
        Assert.Equal(CancellationToken.None, context.CancellationToken);
    }

    [Fact]
    public void Clone_UsesProvidedOverrides() {
        var rootProvider = Substitute.For<IServiceProvider>();
        var scopeProvider = Substitute.For<IServiceProvider>();
        var knownServices = Substitute.For<IKnownServices>();
        var request = Substitute.For<IExecutionRequest>();
        var response = Substitute.For<IExecutionResponse>();
        var metrics = Substitute.For<IMetricLogger>();

        var context = new StreamingExecutionContext(
            rootProvider, scopeProvider, knownServices,
            request, response, metrics,
            Hardened.Shared.Runtime.Diagnostics.MachineTimestamp.Now);

        var newRequest = Substitute.For<IExecutionRequest>();
        var newResponse = Substitute.For<IExecutionResponse>();
        var newProvider = Substitute.For<IServiceProvider>();
        var newMetrics = Substitute.For<IMetricLogger>();

        var clone = context.Clone(newRequest, newResponse, newProvider, newMetrics);

        Assert.Same(newRequest, clone.Request);
        Assert.Same(newResponse, clone.Response);
        Assert.Same(newProvider, clone.RequestServices);
        Assert.Same(newMetrics, clone.RequestMetrics);
        Assert.Same(rootProvider, clone.RootServiceProvider);
    }

    [Fact]
    public void Clone_KeepsOriginals_WhenOverridesNull() {
        var rootProvider = Substitute.For<IServiceProvider>();
        var scopeProvider = Substitute.For<IServiceProvider>();
        var knownServices = Substitute.For<IKnownServices>();
        var request = Substitute.For<IExecutionRequest>();
        var response = Substitute.For<IExecutionResponse>();
        var metrics = Substitute.For<IMetricLogger>();

        var context = new StreamingExecutionContext(
            rootProvider, scopeProvider, knownServices,
            request, response, metrics,
            Hardened.Shared.Runtime.Diagnostics.MachineTimestamp.Now);

        var clone = context.Clone(null, null, null, null);

        Assert.Same(request, clone.Request);
        Assert.Same(response, clone.Response);
        Assert.Same(scopeProvider, clone.RequestServices);
        Assert.Same(metrics, clone.RequestMetrics);
    }
}
