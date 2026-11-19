using System.Text.RegularExpressions;
using Xunit;

namespace Hardened.OpenApi.SourceGenerator.Tests;

/// <summary>
/// Descriptions of one project that name a schema of a file of shared shapes through a
/// <c>$ref</c> to that file.
/// </summary>
/// <remarks>
/// The diagnostics for a reference that does not resolve are asserted in
/// Hardened.OpenApi.BuildTask.Tests, which runs the task that reports them.
/// </remarks>
public class CrossFileReferenceTests
{
    private static readonly Dictionary<string, string> SharedLast = new()
    {
        ["drafts.yaml"] = Resource("drafts"),
        ["runs.yaml"] = Resource("runs"),
        ["shared.yaml"] = Shared,
    };

    private static readonly Dictionary<string, string> SharedFirst = new()
    {
        ["shared.yaml"] = Shared,
        ["drafts.yaml"] = Resource("drafts"),
        ["runs.yaml"] = Resource("runs"),
    };

    /// <remarks>
    /// Not Union: it writes the same case types as Response under a C# 15 <c>union</c>, which the
    /// Roslyn this harness compiles with cannot parse.
    /// </remarks>
    [Theory]
    [InlineData("Throws", true)]
    [InlineData("Throws", false)]
    [InlineData("Response", true)]
    [InlineData("Response", false)]
    public void FilesNamingASchemaOfAnotherFileCompile(string responseModel, bool sharedFirst)
    {
        var result = OpenApiGenerator
            .Run(
                sharedFirst ? SharedFirst : SharedLast,
                OpenApiGenerator.MinimalEntryPoint,
                new Dictionary<string, string> { ["HardenedResponseModel"] = responseModel }
            )
            .AssertNoErrors();

        var emitted = string.Concat(result.GeneratedSources.Values);

        Assert.Single(Regex.Matches(emitted, @"record Refusal\("));
        Assert.Single(Regex.Matches(emitted, @"record RefusalDetail\("));
    }

    [Fact]
    public void EachFilesOperationsAndPropertiesUseTheOneType()
    {
        var result = OpenApiGenerator
            .Run(SharedLast, OpenApiGenerator.MinimalEntryPoint)
            .AssertNoErrors();

        var drafts = result.GeneratedSources["drafts.g.cs"];
        var runs = result.GeneratedSources["runs.g.cs"];

        Assert.Contains("global::TestNamespace.Models.Refusal", ServiceOf(drafts));
        Assert.Contains("global::TestNamespace.Models.Refusal", ServiceOf(runs));
        Assert.Matches(@"global::TestNamespace\.Models\.Refusal\??\s+Refusal\b", drafts);
    }

    [Fact]
    public void TheSharedTypesValidatorIsWrittenOnce()
    {
        var result = OpenApiGenerator
            .Run(
                SharedLast,
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

    /// <summary>A file of shared shapes alone: its types, and no service to implement.</summary>
    [Fact]
    public void AFileOfComponentsCompilesWithNoService()
    {
        var result = OpenApiGenerator.Run(Shared, specFileName: "shared.yaml").AssertNoErrors();

        var shared = result.GeneratedSources["shared.g.cs"];

        Assert.Contains("record Refusal(", shared);
        Assert.DoesNotContain("interface I", shared);
    }

    private static string ServiceOf(string emitted)
    {
        var start = emitted.IndexOf("interface I", StringComparison.Ordinal);

        Assert.True(start >= 0, "No service interface in:\n" + emitted);

        return emitted.Substring(start);
    }

    /// <summary>
    /// Components and no paths. Refusal reaches RefusalDetail through a reference local to this
    /// file, and carries a constraint so ValidationModules writes a validator for it.
    /// </summary>
    private const string Shared = """
        openapi: "3.1.0"
        info: { title: shared, version: "1.0" }
        components:
          schemas:
            Refusal:
              type: object
              required: [code]
              properties:
                code: { type: string, minLength: 1 }
                detail: { $ref: '#/components/schemas/RefusalDetail' }
            RefusalDetail:
              type: object
              properties:
                hint: { type: string }
        """;

    /// <summary>
    /// One resource's file. The refusal is the body of a 404 and of the check's 200, and the type
    /// of a property of the resource's item.
    /// </summary>
    private static string Resource(string tag) =>
        $$"""
            openapi: "3.1.0"
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
                          schema: { $ref: 'shared.yaml#/components/schemas/Refusal' }
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
                          schema: { $ref: 'shared.yaml#/components/schemas/Refusal' }
            components:
              schemas:
                {{tag}}Item:
                  type: object
                  properties:
                    id: { type: string }
                    refusal: { $ref: 'shared.yaml#/components/schemas/Refusal' }
            """;
}
