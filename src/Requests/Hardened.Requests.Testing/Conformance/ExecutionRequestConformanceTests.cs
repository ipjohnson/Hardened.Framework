using Hardened.Requests.Abstract.Execution;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace Hardened.Requests.Testing.Conformance;

/// <summary>
/// The behaviour every <see cref="IExecutionRequest"/> implementation must exhibit,
/// expressed once and executed against every transport.
///
/// Hardened's central claim is that it is transport agnostic: a handler should not be able
/// to tell whether its request arrived over ASP.NET Core, API Gateway, Lambda streaming or
/// an in-memory test harness. That claim is only true if every adapter maps its native
/// request onto this contract the same way. This suite is what makes that checkable rather
/// than assumed.
///
/// To enrol a transport, derive from this class and supply an adapter:
///
/// <code>
/// public class MyTransportConformanceTests : ExecutionRequestConformanceTests {
///     protected override IExecutionRequestConformanceAdapter Adapter { get; } = new MyAdapter();
/// }
/// </code>
///
/// Deliberately not asserted yet: that <c>Clone</c> replaces query string and cookies when
/// those arguments are supplied. Implementations currently diverge on it, and tightening
/// that is a separate change from establishing the contract.
/// </summary>
public abstract class ExecutionRequestConformanceTests {

    protected abstract IExecutionRequestConformanceAdapter Adapter { get; }

    private IExecutionRequest Create(Action<ConformanceRequestSpec>? configure = null) {
        var spec = new ConformanceRequestSpec();
        configure?.Invoke(spec);
        return Adapter.CreateRequest(spec);
    }

    private string Because(string what) => $"[{Adapter.TransportName}] {what}";

    // ---------------------------------------------------------------- request surface

    [Fact]
    public void MethodIsSurfaced() {
        var request = Create(s => s.Method = "POST");

        Assert.True("POST".Equals(request.Method, StringComparison.OrdinalIgnoreCase),
            Because($"expected Method 'POST' but got '{request.Method}'"));
    }

    [Fact]
    public void PathIsSurfaced() {
        var request = Create(s => s.Path = "/orders/42");

        Assert.Equal("/orders/42", request.Path);
    }

    [Fact]
    public void HeadersAreSurfaced() {
        var request = Create(s => s.Headers["X-Correlation-Id"] = "abc-123");

        Assert.True(request.Headers.TryGetValue("X-Correlation-Id", out var value),
            Because("expected header 'X-Correlation-Id' to be present"));
        Assert.Equal("abc-123", value.ToString());
    }

    [Fact]
    public void ContentTypeComesFromTheContentTypeHeader() {
        var request = Create(s => s.Headers["Content-Type"] = "application/json");

        Assert.Equal("application/json", request.ContentType);
    }

    /// <summary>
    /// Accept is read by every response serializer to decide whether it can handle the
    /// response (<c>context.Request.Accept?.Contains("application/json")</c>). A transport
    /// that fails to surface it silently disables content negotiation.
    /// </summary>
    [Fact]
    public void AcceptComesFromTheAcceptHeader() {
        var request = Create(s => s.Headers["Accept"] = "application/json");

        Assert.NotNull(request.Accept);
        Assert.Contains("application/json", request.Accept!);
    }

    [Fact]
    public void AcceptReflectsANonJsonValue() {
        var request = Create(s => s.Headers["Accept"] = "text/html");

        Assert.NotNull(request.Accept);
        Assert.Contains("text/html", request.Accept!);
        Assert.DoesNotContain("application/json", request.Accept!);
    }

    [Fact]
    public void QueryStringIsSurfaced() {
        var request = Create(s => {
            s.QueryString["page"] = "2";
            s.QueryString["size"] = "50";
        });

        Assert.Equal("2", request.QueryString.Get("page").ToString());
        Assert.Equal("50", request.QueryString.Get("size").ToString());
    }

    [Fact]
    public void CookiesAreSurfaced() {
        var request = Create(s => {
            s.Cookies.Add("session=abc123");
            s.Cookies.Add("theme=dark");
        });

        Assert.Contains(request.Cookies, c => c.Contains("session=abc123"));
        Assert.Contains(request.Cookies, c => c.Contains("theme=dark"));
    }

    [Fact]
    public void CookiesAreEmptyRatherThanNullWhenNoneWereSent() {
        var request = Create();

        Assert.NotNull(request.Cookies);
        Assert.Empty(request.Cookies);
    }

    [Fact]
    public void BodyIsReadable() {
        var payload = "{\"name\":\"conformance\"}"u8.ToArray();
        var request = Create(s => s.Body = payload);

        request.Body.Position = 0;
        using var reader = new StreamReader(request.Body);

        Assert.Equal("{\"name\":\"conformance\"}", reader.ReadToEnd());
    }

    /// <summary>
    /// Handlers index into PathTokens without null-checking, so an unrouted request must
    /// yield an empty collection rather than null.
    /// </summary>
    [Fact]
    public void PathTokensAreEmptyRatherThanNullBeforeRouting() {
        var request = Create();

        Assert.NotNull(request.PathTokens);
    }

    // ------------------------------------------------------------------ clone contract
    //
    // Settled semantics, already asserted for LambdaExecutionRequest in Hardened.Amz:
    // a null argument keeps the current value, a non-null argument replaces it.

    [Fact]
    public void ClonePreservesMethodWhenMethodIsNull() {
        var request = Create(s => s.Method = "PATCH");

        var clone = request.Clone(method: null, path: "/somewhere-else");

        Assert.True("PATCH".Equals(clone.Method, StringComparison.OrdinalIgnoreCase),
            Because($"Clone(method: null) must keep 'PATCH' but got '{clone.Method}'"));
    }

    [Fact]
    public void CloneReplacesMethodWhenMethodIsProvided() {
        var request = Create(s => s.Method = "GET");

        var clone = request.Clone(method: "DELETE");

        Assert.True("DELETE".Equals(clone.Method, StringComparison.OrdinalIgnoreCase),
            Because($"Clone(method: \"DELETE\") must replace the method but got '{clone.Method}'"));
    }

    [Fact]
    public void ClonePreservesPathWhenPathIsNull() {
        var request = Create(s => s.Path = "/original");

        var clone = request.Clone(method: "PUT", path: null);

        Assert.Equal("/original", clone.Path);
    }

    [Fact]
    public void CloneReplacesPathWhenPathIsProvided() {
        var request = Create(s => s.Path = "/original");

        var clone = request.Clone(path: "/replaced");

        Assert.Equal("/replaced", clone.Path);
    }

    [Fact]
    public void CloneReplacesHeadersWhenHeadersAreProvided() {
        var request = Create(s => s.Headers["X-Original"] = "yes");

        var replacement = new Dictionary<string, StringValues> {
            { "X-Replaced", new StringValues("also-yes") }
        };

        var clone = request.Clone(headers: replacement);

        Assert.True(clone.Headers.TryGetValue("X-Replaced", out var value),
            Because("Clone(headers: ...) must expose the supplied headers"));
        Assert.Equal("also-yes", value.ToString());
    }

    [Fact]
    public void ClonePreservesBody() {
        var payload = "conformance-body"u8.ToArray();
        var request = Create(s => s.Body = payload);

        var clone = request.Clone(method: "POST");

        clone.Body.Position = 0;
        using var reader = new StreamReader(clone.Body);

        Assert.Equal("conformance-body", reader.ReadToEnd());
    }

    [Fact]
    public void CloneDoesNotMutateTheOriginal() {
        var request = Create(s => {
            s.Method = "GET";
            s.Path = "/original";
        });

        request.Clone(method: "DELETE", path: "/replaced");

        Assert.True("GET".Equals(request.Method, StringComparison.OrdinalIgnoreCase),
            Because($"Clone must not mutate the original, but Method became '{request.Method}'"));
        Assert.Equal("/original", request.Path);
    }
}
