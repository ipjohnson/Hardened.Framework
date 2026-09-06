using Hardened.Amz.Web.Lambda.Runtime.Tests.Infrastructure;
using Hardened.Requests.Abstract.Execution;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace Hardened.Amz.Web.Lambda.Runtime.Tests;

/// <summary>
/// What a cloned API Gateway request carries over from the request it came from.
///
/// <para>
/// The parameter bag is the one thing that must <em>not</em> be carried over by reference.
/// <c>IExecutionChain.Fork</c> exists so a filter can re-run a handler against a cloned
/// context; if the clone shares the original's parameters, rebinding in the fork overwrites
/// what the original bound, and the two silently corrupt each other. Every implementation of
/// <c>IExecutionRequest.Clone</c> in both repositories copied the reference until 2026-08-12.
/// </para>
///
/// <para>
/// The framework asserts this across all transports in
/// <c>Hardened.Requests.Testing.Conformance.ExecutionRequestConformanceTests</c>. These two
/// transports cannot inherit it yet — this repository consumes that package from the feed, and
/// the published version predates the conformance case — so they are covered directly here
/// until a package carrying it ships.
/// </para>
/// </summary>
public class RequestCloneTests {

    [Fact]
    public async Task ACloneGetsItsOwnParameters() {
        var harness = new ApiGatewayHarness();

        await harness.Process(ApiGatewayHarness.Event());

        var request = harness.ExecutionContext.Request;

        request.Parameters = new RecordingParameters { Value = "original" };

        var clone = request.Clone(method: "POST");

        Assert.NotNull(clone.Parameters);
        Assert.NotSame(request.Parameters, clone.Parameters);

        ((RecordingParameters)clone.Parameters!).Value = "rebound";

        Assert.Equal("original", ((RecordingParameters)request.Parameters!).Value);
    }

    [Fact]
    public async Task ACloneOfARequestWithNoParametersHasNone() {
        var harness = new ApiGatewayHarness();

        await harness.Process(ApiGatewayHarness.Event());

        var request = harness.ExecutionContext.Request;

        request.Parameters = null;

        Assert.Null(request.Clone(method: "POST").Parameters);
    }

    /// <summary>
    /// Clone's whole purpose is rebinding, and until 2026-08-15 not one of its five arguments was
    /// applied — <c>Method</c> and <c>Path</c> read through to the shared proxy request, and the
    /// rest were accepted and dropped. A filter forking a chain to re-run a handler against a
    /// different method, path or header set silently re-ran it against the original.
    ///
    /// <para>
    /// The test above has always passed <c>method: "POST"</c> and asserted only on parameters,
    /// which is how the drop stayed invisible.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ACloneRebindsTheMethodAndPathItIsGiven() {
        var harness = new ApiGatewayHarness();

        await harness.Process(ApiGatewayHarness.Event());

        var request = harness.ExecutionContext.Request;

        var clone = request.Clone(method: "DELETE", path: "/replaced");

        Assert.Equal("DELETE", clone.Method);
        Assert.Equal("/replaced", clone.Path);
    }

    [Fact]
    public async Task ACloneKeepsTheMethodAndPathWhenGivenNeither() {
        var harness = new ApiGatewayHarness();

        await harness.Process(ApiGatewayHarness.Event());

        var request = harness.ExecutionContext.Request;

        var clone = request.Clone();

        Assert.Equal(request.Method, clone.Method);
        Assert.Equal(request.Path, clone.Path);
    }

    [Fact]
    public async Task ACloneRebindsTheHeadersItIsGiven() {
        var harness = new ApiGatewayHarness();

        await harness.Process(ApiGatewayHarness.Event());

        var clone = harness.ExecutionContext.Request.Clone(
            headers: new Dictionary<string, StringValues> { { "X-Replaced", "yes" } });

        Assert.True(clone.Headers.TryGetValue("X-Replaced", out var value));
        Assert.Equal("yes", value.ToString());
    }

    /// <summary>
    /// Rebinding must not write through, the same property the parameter bag has.
    /// </summary>
    [Fact]
    public async Task RebindingHeadersInACloneDoesNotTouchTheOriginal() {
        var harness = new ApiGatewayHarness();

        await harness.Process(ApiGatewayHarness.Event());

        var request = harness.ExecutionContext.Request;
        var clone = request.Clone();

        clone.Headers["X-Added-In-Fork"] = "yes";

        Assert.False(request.Headers.ContainsKey("X-Added-In-Fork"));
    }

    [Fact]
    public async Task ACloneRebindsTheCookiesItIsGiven() {
        var harness = new ApiGatewayHarness();

        await harness.Process(ApiGatewayHarness.Event());

        var clone = harness.ExecutionContext.Request.Clone(cookies: new[] { "session=replaced" });

        Assert.Contains("session=replaced", clone.Cookies);
    }

    /// <summary>
    /// API Gateway omits the cookie field entirely when none were sent, so the SDK's property is
    /// null — through a non-nullable <see cref="IReadOnlyList{T}"/>.
    /// </summary>
    [Fact]
    public async Task CookiesAreEmptyRatherThanNullWhenNoneWereSent() {
        var harness = new ApiGatewayHarness();

        await harness.Process(ApiGatewayHarness.Event());

        var cookies = harness.ExecutionContext.Request.Cookies;

        Assert.NotNull(cookies);
        Assert.Empty(cookies);
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
}
