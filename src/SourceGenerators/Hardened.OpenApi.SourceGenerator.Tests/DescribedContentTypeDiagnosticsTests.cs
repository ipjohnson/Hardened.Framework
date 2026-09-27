using Hardened.SourceGeneration.Testing;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Hardened.OpenApi.SourceGenerator.Tests;

/// <summary>
/// A media type a contract declares that nothing in the compilation writes.
/// </summary>
/// <remarks>
/// <para>
/// The <c>HRDR012</c> a code-first handler gets for <c>[Produces]</c>. A contract had no such
/// check, so a response declared only as a media type nothing produced built clean and answered
/// 500 on every request that reached it. In the 0.41 trial that media type was
/// <c>application/problem+json</c>, which the JSON serializers now write for a failure.
/// </para>
/// <para>
/// A warning, as for code-first, because a host may register the serializer.
/// </para>
/// </remarks>
public class DescribedContentTypeDiagnosticsTests
{
    private static string Spec(string successType, string failureType) =>
        $$"""
            openapi: "3.0.0"
            info: { title: Things, version: "1.0" }
            paths:
              /things/{id}:
                get:
                  tags: [Thing]
                  operationId: getThing
                  parameters:
                    - { name: id, in: path, required: true, schema: { type: string } }
                  responses:
                    '200':
                      description: ok
                      content:
                        {{successType}}:
                          schema: { $ref: '#/components/schemas/Thing' }
                    '404':
                      description: missing
                      content:
                        {{failureType}}:
                          schema: { $ref: '#/components/schemas/Problem' }
            components:
              schemas:
                Thing:
                  type: object
                  properties:
                    name: { type: string }
                Problem:
                  type: object
                  properties:
                    title: { type: string }
            """;

    private static IReadOnlyList<Diagnostic> Reported(string successType, string failureType) =>
        OpenApiGenerator
            .Run(Spec(successType, failureType))
            .GeneratorDiagnostics.Where(diagnostic => diagnostic.Id == "HRDR012")
            .ToList();

    [Fact]
    public void AMediaTypeNothingWritesIsHRDR012()
    {
        var diagnostic = Assert.Single(Reported("application/xml", "application/json"));

        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("'IThingService.GetThing'", diagnostic.GetMessage());
        Assert.Contains(
            "declared by its contract as answering application/xml",
            diagnostic.GetMessage()
        );
    }

    [Fact]
    public void AFailureDeclaredAsAProblemIsWritten()
    {
        Assert.Empty(Reported("application/json", "application/problem+json"));
    }

    /// <summary>
    /// A success is not a problem, and the JSON serializers refuse to label one as a problem, so a
    /// contract declaring its success that way is declaring something nothing writes.
    /// </summary>
    [Fact]
    public void ASuccessDeclaredAsAProblemIsHRDR012()
    {
        var diagnostic = Assert.Single(Reported("application/problem+json", "application/json"));

        Assert.Contains("application/problem+json", diagnostic.GetMessage());
    }

    [Fact]
    public void JsonThroughoutReportsNothing()
    {
        Assert.Empty(Reported("application/json", "application/json"));
    }
}
