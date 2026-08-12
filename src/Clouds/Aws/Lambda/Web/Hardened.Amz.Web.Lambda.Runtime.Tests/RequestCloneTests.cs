using Hardened.Amz.Web.Lambda.Runtime.Tests.Infrastructure;
using Hardened.Requests.Abstract.Execution;
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
