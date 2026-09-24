using System.Text.Json;
using Hardened.SourceGeneration.Testing;
using Xunit;

namespace Hardened.OpenApi.SourceGenerator.Tests;

/// <summary>
/// What a described validation mode becomes in the generated handler, and in the document the
/// service publishes.
/// </summary>
/// <remarks>
/// A <c>ValidationModeAttribute</c> in the handler's metadata, as a described deadline is a
/// <c>TimeoutAttribute</c>, because the metadata is where the validation filter reads the mode and
/// where a module's declaration is merged beside it.
/// </remarks>
public class DescribedValidationModeEmitTests
{
    private static string Spec(string operationExtras) =>
        $$"""
            openapi: "3.0.0"
            info: { title: Orders, version: "1.0" }
            paths:
              /orders:
                post:
                  tags: [Order]
                  operationId: placeOrder
            {{operationExtras}}
                  requestBody:
                    content:
                      application/json:
                        schema: { type: string, minLength: 1 }
                  responses:
                    '200':
                      description: Placed
                      content:
                        application/json:
                          schema: { type: string }
            """;

    private static string Handler(string operationExtras)
    {
        var result = OpenApiGenerator.Run(Spec(operationExtras)).AssertNoErrors();

        return string.Join(
            "\n",
            result
                .GeneratedSources.Where(pair => pair.Key.Contains("PlaceOrder"))
                .Select(pair => pair.Value)
        );
    }

    private const string Attribute =
        "global::Hardened.Requests.Runtime.Validation.ValidationModeAttribute";

    [Fact]
    public void ADescribedModeBecomesTheAttributeTheValidationFilterReads()
    {
        var handler = Handler("      x-hardened-validation: stop-on-first-error");

        Assert.Contains(
            $"new {Attribute}(global::ValidationModules.ValidationStopMode.StopOnFirstError)",
            handler
        );
    }

    [Fact]
    public void AnOperationDescribingNoModeCarriesNoAttribute()
    {
        Assert.DoesNotContain(Attribute, Handler(""));
    }

    /// <summary>
    /// Republished, so a service generated from the contract publishes the mode it runs under.
    /// </summary>
    [Fact]
    public void TheServedDocumentRepublishesTheMode()
    {
        var operation = SpecFirstDocumentTests
            .PublishedDocumentFrom(
                OpenApiGenerator.Run(Spec("      x-hardened-validation: stop-on-first-error"))
            )
            .GetProperty("paths")
            .GetProperty("/orders")
            .GetProperty("post");

        Assert.Equal(
            "stop-on-first-error",
            operation.GetProperty("x-hardened-validation").GetString()
        );
    }

    [Fact]
    public void AnOperationDescribingNoModePublishesNone()
    {
        var operation = SpecFirstDocumentTests
            .PublishedDocumentFrom(OpenApiGenerator.Run(Spec("")))
            .GetProperty("paths")
            .GetProperty("/orders")
            .GetProperty("post");

        Assert.False(operation.TryGetProperty("x-hardened-validation", out _));
    }
}
