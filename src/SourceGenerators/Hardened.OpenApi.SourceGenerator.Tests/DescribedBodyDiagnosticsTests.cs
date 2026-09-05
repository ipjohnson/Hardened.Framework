using Hardened.SourceGeneration.Testing;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Hardened.OpenApi.SourceGenerator.Tests;

/// <summary>
/// A described operation reading a body on a verb that carries none.
/// </summary>
/// <remarks>
/// OpenAPI permits a <c>requestBody</c> on a GET and says it should be avoided; the generated
/// handler reads it, and a request that sends none is refused before the handler runs. The same
/// <c>HRDR010</c> a hand-written GET with a body parameter gets, from the same shared emit stage,
/// and the same <c>NoWarn</c> for a description that means it.
/// </remarks>
public class DescribedBodyDiagnosticsTests {

    private static string Spec(string verb) =>
        $$"""
          openapi: "3.0.0"
          info: { title: Things, version: "1.0" }
          paths:
            /things/search:
              {{verb}}:
                tags: [Thing]
                operationId: searchThings
                requestBody:
                  required: true
                  content:
                    application/json:
                      schema:
                        type: object
                        properties:
                          name: { type: string }
                responses:
                  '200': { description: ok }
          """;

    private static IEnumerable<Diagnostic> Reported(GeneratorResult result) =>
        result.GeneratorDiagnostics.Where(diagnostic => diagnostic.Id == "HRDR010");

    [Fact]
    public void ADescribedGetWithABodyIsHRDR010() {
        var diagnostic = Assert.Single(Reported(OpenApiGenerator.Run(Spec("get"))));

        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("a GET carries none", diagnostic.GetMessage());
        Assert.Contains("HRDR010", diagnostic.GetMessage());
    }

    [Fact]
    public void ADescribedPostWithABodyReportsNothing() {
        Assert.Empty(Reported(OpenApiGenerator.Run(Spec("post")).AssertNoErrors()));
    }
}
