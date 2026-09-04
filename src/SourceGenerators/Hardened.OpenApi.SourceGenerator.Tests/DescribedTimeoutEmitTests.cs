using Hardened.SourceGeneration.Testing;
using Xunit;

namespace Hardened.OpenApi.SourceGenerator.Tests;

/// <summary>
/// What a described deadline becomes in the generated handler.
/// </summary>
/// <remarks>
/// <para>
/// A <c>TimeoutAttribute</c> in the handler's metadata, rather than a constructor argument on
/// <c>ExecutionRequestHandlerInfo</c> or a filter of its own. That is the decision this pins:
/// metadata is the rung the runtime's cascade reads, so a budget in the description and a
/// <c>[Timeout]</c> on the implementation resolve against each other by the same nearest-wins rule
/// instead of the description silently winning or both installing a deadline.
/// </para>
/// <para>
/// The emitted source is asserted rather than the model, because the model agreeing with itself is
/// not the question - whether the spec bridge writes something the runtime recognises is.
/// </para>
/// </remarks>
public class DescribedTimeoutEmitTests {

    private static string Handler(string operationExtras) {
        var result = OpenApiGenerator.Run(
            $$"""
              openapi: "3.0.0"
              info: { title: Rates, version: "1.0" }
              paths:
                /rates:
                  get:
                    tags: [Rate]
                    operationId: readRates
              {{operationExtras}}
                    responses:
                      '200':
                        description: A rate
                        content:
                          application/json:
                            schema: { type: string }
              """).AssertNoErrors();

        return string.Join(
            "\n",
            result.GeneratedSources
                .Where(pair => pair.Key.Contains("ReadRates"))
                .Select(pair => pair.Value));
    }

    private const string Attribute = "global::Hardened.Requests.Runtime.Filters.TimeoutAttribute";

    [Fact]
    public void ADescribedBudgetBecomesTheAttributeTheRuntimeReads() {
        var handler = Handler("      x-hardened-timeout: 2000");

        Assert.Contains($"new {Attribute}(){{ Milliseconds = 2000 }}", handler);
    }

    /// <summary>
    /// Only what the description said. Writing the defaults out would put a 504 and a zero on every
    /// generated handler, and a reader could not tell which of them the model had asked for.
    /// </summary>
    [Fact]
    public void ADefaultStatusIsNotWrittenOut() {
        var handler = Handler("      x-hardened-timeout: 2000");

        Assert.DoesNotContain("Status =", handler);
        Assert.DoesNotContain("RetryAfterSeconds =", handler);
    }

    /// <summary>
    /// An operation shedding load carries both, since neither is what the attribute would default
    /// to.
    /// </summary>
    [Fact]
    public void AShedStatusAndItsRetryAfterAreWrittenOut() {
        var handler = Handler("""
                  x-hardened-timeout:
                    milliseconds: 500
                    status: 503
                    retryAfterSeconds: 30
            """);

        Assert.Contains(
            $"new {Attribute}(){{ Milliseconds = 500, Status = 503, RetryAfterSeconds = 30 }}",
            handler);
    }

    /// <summary>
    /// An operation the description says nothing about carries nothing, which is the same rule the
    /// code-first front end follows: what declares no budget is bounded by no filter and no timer.
    /// </summary>
    [Fact]
    public void AnOperationDescribingNoDeadlineCarriesNoAttribute() {
        Assert.DoesNotContain(Attribute, Handler(""));
    }
}
