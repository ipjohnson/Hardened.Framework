using System;
using System.Linq;
using System.Threading;
using Hardened.Generation.Models;
using Hardened.OpenApi.SourceGenerator;
using Xunit;

namespace Hardened.OpenApi.BuildTask.Tests;

/// <summary>
/// <c>x-hardened-validation</c>, which is how a description says whether an operation's validation
/// stops at its first failure.
/// </summary>
/// <remarks>
/// An extension because OpenAPI has no field for this. A schema states what is valid, and how many
/// of an input's failures a refusal reports is a property of the server. It is the same extension
/// the document writer emits, so a code-first service's contract round-trips.
/// </remarks>
public class ValidationExtensionTests
{
    private static string Document(string operationExtras) =>
        $$"""
            openapi: 3.0.3
            info: { title: Orders, version: '1.0' }
            paths:
              /orders:
                post:
                  operationId: placeOrder
                  tags: [orders]
            {{operationExtras}}
                  requestBody:
                    content:
                      application/json:
                        schema: { type: string }
                  responses:
                    '200':
                      description: OK
                      content:
                        application/json:
                          schema: { type: string }
            """;

    private static OperationModel Operation(string operationExtras)
    {
        var model = OpenApiSpecParser.Parse(
            Document(operationExtras),
            "orders",
            CancellationToken.None
        );

        Assert.NotNull(model);

        return model!
            .Services.SelectMany(service => service.Operations)
            .Single(operation => operation.OperationId == "placeOrder");
    }

    [Fact]
    public void StopOnFirstErrorNamesTheMember()
    {
        Assert.Equal(
            "StopOnFirstError",
            Operation("      x-hardened-validation: stop-on-first-error").ValidationMode
        );
    }

    /// <summary>
    /// Stated rather than left out where an operation has to put every failure back under an
    /// application that stops at the first.
    /// </summary>
    [Fact]
    public void CollectAllNamesTheMember()
    {
        Assert.Equal(
            "CollectAll",
            Operation("      x-hardened-validation: collect-all").ValidationMode
        );
    }

    [Fact]
    public void AnOperationDeclaringNoModeCarriesNone()
    {
        Assert.Null(Operation("").ValidationMode);
    }

    /// <summary>
    /// A misspelt mode would otherwise report every failure without a word, which is the thing the
    /// declaration was written to change.
    /// </summary>
    [Theory]
    [InlineData("first-error")]
    [InlineData("StopOnFirstError")]
    [InlineData("true")]
    public void AModeThatNamesNothingIsRefusedNamingTheOperation(string written)
    {
        var failure = Assert.Throws<InvalidOperationException>(() =>
            Operation($"      x-hardened-validation: {written}")
        );

        Assert.Contains("placeOrder", failure.Message);
        Assert.Contains("stop-on-first-error", failure.Message);
    }
}
