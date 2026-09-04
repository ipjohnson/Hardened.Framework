using System;
using System.Linq;
using System.Threading;
using Hardened.Generation.Models;
using Hardened.OpenApi.SourceGenerator;
using Xunit;

namespace Hardened.OpenApi.BuildTask.Tests;

/// <summary>
/// <c>x-hardened-timeout</c>, which is how a description bounds an operation.
/// </summary>
/// <remarks>
/// An extension because OpenAPI has no field for this. The specification describes the exchange;
/// how long a server may take over it is a property of the server, and only its own vocabulary can
/// carry it. It is the same extension the document writer emits, so a code-first service's
/// contract round-trips back into a service bounded the way it was.
/// </remarks>
public class TimeoutExtensionTests {

    private static string Document(string operationExtras) => $$"""
        openapi: 3.0.3
        info: { title: Rates, version: '1.0' }
        paths:
          /rates:
            get:
              operationId: readRates
              tags: [rates]
        {{operationExtras}}
              responses:
                '200':
                  description: OK
                  content:
                    application/json:
                      schema: { type: string }
        """;

    private static OperationModel Operation(string operationExtras) {
        var model = OpenApiSpecParser.Parse(Document(operationExtras), "rates", CancellationToken.None);

        Assert.NotNull(model);

        return model!.Services.SelectMany(service => service.Operations)
            .Single(operation => operation.OperationId == "readRates");
    }

    /// <summary>
    /// The scalar form, which is what almost every declaration wants and reads better than an
    /// object with one member.
    /// </summary>
    [Fact]
    public void ANumberIsTheBudgetOnItsOwn() {
        var operation = Operation("      x-hardened-timeout: 2000");

        Assert.NotNull(operation.Timeout);
        Assert.Equal(2000, operation.Timeout!.Milliseconds);
    }

    /// <summary>
    /// 504 unless the description says otherwise, matching what the attribute defaults to, so the
    /// two front ends state the same thing.
    /// </summary>
    [Fact]
    public void ABudgetStatingNoStatusAnswers504() {
        Assert.Equal(504, Operation("      x-hardened-timeout: 2000").Timeout!.Status);
        Assert.Equal(0, Operation("      x-hardened-timeout: 2000").Timeout!.RetryAfterSeconds);
    }

    /// <summary>
    /// The object form, for an operation shedding load rather than waiting on something. That is
    /// the only case with anything else to say.
    /// </summary>
    [Fact]
    public void AnObjectCarriesTheStatusAndTheRetryAfter() {
        var operation = Operation("""
                  x-hardened-timeout:
                    milliseconds: 500
                    status: 503
                    retryAfterSeconds: 30
            """);

        Assert.Equal(500, operation.Timeout!.Milliseconds);
        Assert.Equal(503, operation.Timeout.Status);
        Assert.Equal(30, operation.Timeout.RetryAfterSeconds);
    }

    [Fact]
    public void AnOperationDeclaringNoDeadlineCarriesNone() {
        Assert.Null(Operation("").Timeout);
    }

    /// <summary>
    /// A zero would refuse every request the moment the service was deployed, and the description
    /// is the one place that can say so before anything is generated at all.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ABudgetThatCannotMeanAnythingIsRefusedNamingTheOperation(int milliseconds) {
        var failure = Assert.Throws<InvalidOperationException>(
            () => Operation($"      x-hardened-timeout: {milliseconds}"));

        Assert.Contains("readRates", failure.Message);
    }
}
