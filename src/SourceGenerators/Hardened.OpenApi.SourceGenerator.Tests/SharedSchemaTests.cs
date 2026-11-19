using System.Text.RegularExpressions;
using Xunit;

namespace Hardened.OpenApi.SourceGenerator.Tests;

/// <summary>
/// Two descriptions of one project that each declare a schema alike.
/// </summary>
/// <remarks>
/// A contract split into one file per resource keeps each file a valid document alone, so each
/// declares the shapes it shares with the others. Every file generates into one models namespace,
/// and each wrote its own record of the shared one: CS0579 and CS8863 on the second.
/// </remarks>
public class SharedSchemaTests
{
    private static readonly Dictionary<string, string> TwoResources = new()
    {
        ["drafts.yaml"] = Resource("drafts"),
        ["runs.yaml"] = Resource("runs"),
    };

    /// <remarks>
    /// Not Union: it writes the same case types as Response under a C# 15 <c>union</c>, which the
    /// Roslyn this harness compiles with cannot parse.
    /// </remarks>
    [Theory]
    [InlineData("Throws")]
    [InlineData("Response")]
    public void TwoFilesDeclaringASchemaAlikeCompile(string responseModel)
    {
        OpenApiGenerator
            .Run(
                TwoResources,
                OpenApiGenerator.MinimalEntryPoint,
                new Dictionary<string, string> { ["HardenedResponseModel"] = responseModel }
            )
            .AssertNoErrors();
    }

    /// <summary>
    /// The first file writes the record, and the second file's operations name that type.
    /// </summary>
    [Fact]
    public void BothFilesOperationsUseTheOneGeneratedType()
    {
        var result = OpenApiGenerator
            .Run(TwoResources, OpenApiGenerator.MinimalEntryPoint)
            .AssertNoErrors();

        var drafts = result.GeneratedSources["drafts.g.cs"];
        var runs = result.GeneratedSources["runs.g.cs"];

        Assert.Contains("record Refusal(", drafts);
        Assert.DoesNotContain("record Refusal(", runs);

        Assert.Contains("global::TestNamespace.Models.Refusal", ServiceOf(drafts));
        Assert.Contains("global::TestNamespace.Models.Refusal", ServiceOf(runs));
    }

    /// <summary>
    /// Each file's resolver keeps its own entry for the shared type. They are named after their
    /// files, so the two do not collide, and each serves its own operations.
    /// </summary>
    [Fact]
    public void EachFilesResolverStillDescribesTheSharedType()
    {
        var result = OpenApiGenerator
            .Run(TwoResources, OpenApiGenerator.MinimalEntryPoint)
            .AssertNoErrors();

        Assert.Contains(
            "typeof(global::TestNamespace.Models.Refusal)",
            result.GeneratedSources["runs.g.cs"]
        );
    }

    /// <summary>
    /// The validator ValidationModules writes for the shared type, written once. The record carries
    /// the constraints and the generator writes one validator per constrained type, so a second
    /// record would also have been a second validator.
    /// </summary>
    [Fact]
    public void TheSharedTypesValidatorIsWrittenOnce()
    {
        var result = OpenApiGenerator
            .Run(
                TwoResources,
                OpenApiGenerator.MinimalEntryPoint,
                ValidationModulesPackage.BuildProperties,
                additionalGenerators: [ValidationModulesPackage.Generator()]
            )
            .AssertNoErrors();

        var validators = result
            .GeneratedSources.Where(pair =>
                Regex.IsMatch(pair.Value, @"class\s+\w*RefusalValidator\b")
            )
            .Select(pair => pair.Key)
            .ToList();

        Assert.True(
            validators.Count == 1,
            "Expected one Refusal validator, found: "
                + string.Join(", ", validators)
                + "\nGenerated: "
                + string.Join(", ", result.GeneratedSources.Keys)
        );
    }

    /// <summary>
    /// Two declarations of one name that differ are refused before anything is emitted. The build
    /// task reports this as HOAT036; Hardened.OpenApi.BuildTask.Tests asserts the diagnostic.
    /// </summary>
    [Fact]
    public void TwoFilesDeclaringASchemaDifferentlyAreRefused()
    {
        var conflict = Assert.Throws<InvalidOperationException>(() =>
            OpenApiGenerator.Run(
                new Dictionary<string, string>
                {
                    ["drafts.yaml"] = Resource("drafts"),
                    ["runs.yaml"] = Resource("runs").Replace("minLength: 1", "minLength: 2"),
                },
                OpenApiGenerator.MinimalEntryPoint
            )
        );

        Assert.Contains("'Refusal'", conflict.Message);
        Assert.Contains("'drafts.yaml'", conflict.Message);
        Assert.Contains("'runs.yaml'", conflict.Message);
    }

    /// <summary>The services namespace of one emitted file, where its operations are declared.</summary>
    private static string ServiceOf(string emitted)
    {
        var start = emitted.IndexOf("interface I", StringComparison.Ordinal);

        Assert.True(start >= 0, "No service interface in:\n" + emitted);

        return emitted.Substring(start);
    }

    /// <summary>
    /// One resource's file, declaring its own item and the shared refusal, as each file of a split
    /// contract does. The refusal is the body of a 404 and of the create's 200, so every response
    /// model puts it in the service's signature.
    /// </summary>
    private static string Resource(string tag) =>
        $$"""
            openapi: "3.0.0"
            info: { title: {{tag}}, version: "1.0" }
            paths:
              /{{tag}}/{id}:
                get:
                  tags: [{{tag}}]
                  operationId: get{{tag}}
                  parameters:
                    - name: id
                      in: path
                      required: true
                      schema: { type: string }
                  responses:
                    '200':
                      description: found
                      content:
                        application/json:
                          schema: { $ref: '#/components/schemas/{{tag}}Item' }
                    '404':
                      description: missing
                      content:
                        application/json:
                          schema: { $ref: '#/components/schemas/Refusal' }
              /{{tag}}/{id}/check:
                post:
                  tags: [{{tag}}]
                  operationId: check{{tag}}
                  parameters:
                    - name: id
                      in: path
                      required: true
                      schema: { type: string }
                  responses:
                    '200':
                      description: the refusal this item would get
                      content:
                        application/json:
                          schema: { $ref: '#/components/schemas/Refusal' }
            components:
              schemas:
                {{tag}}Item:
                  type: object
                  properties:
                    id: { type: string }
                Refusal:
                  type: object
                  required: [code]
                  properties:
                    code: { type: string, minLength: 1 }
                    detail: { type: string }
            """;
}
