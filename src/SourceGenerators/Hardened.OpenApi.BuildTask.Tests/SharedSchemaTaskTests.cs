using Xunit;

namespace Hardened.OpenApi.BuildTask.Tests;

/// <summary>
/// Two descriptions of one project that declare one schema name, alike and not.
/// </summary>
/// <remarks>
/// Whether the shared type then compiles, and whether ValidationModules writes its validator once,
/// is asserted in Hardened.OpenApi.SourceGenerator.Tests, which compiles what the task emits.
/// </remarks>
public class SharedSchemaTaskTests
{
    [Fact]
    public void ASchemaDeclaredAlikeIsWrittenByTheFirstFileOnly()
    {
        using var harness = new TaskHarness();
        var drafts = harness.WriteSpec("drafts.yaml", Resource("drafts"));
        var runs = harness.WriteSpec("runs.yaml", Resource("runs"));

        var result = harness.Run(drafts, runs);

        Assert.True(result.Succeeded, result.ErrorText);
        Assert.Contains("record Refusal(", File.ReadAllText(harness.SourcePathFor("drafts.yaml")));
        Assert.DoesNotContain(
            "record Refusal(",
            File.ReadAllText(harness.SourcePathFor("runs.yaml"))
        );
    }

    [Fact]
    public void ASchemaDeclaredDifferentlyIsAnErrorNamingBothFilesAndTheSchema()
    {
        using var harness = new TaskHarness();
        var drafts = harness.WriteSpec("drafts.yaml", Resource("drafts"));
        var runs = harness.WriteSpec(
            "runs.yaml",
            Resource("runs").Replace("minLength: 1", "minLength: 2")
        );

        var result = harness.Run(drafts, runs);

        Assert.False(result.Succeeded);

        var error = Assert.Single(result.Errors, error => error.Code == "HOAT036");

        Assert.Equal(runs, error.File);
        Assert.Contains("'Refusal'", error.Message);
        Assert.Contains(drafts, error.Message);
        Assert.Contains(runs, error.Message);
    }

    /// <summary>
    /// The refused file is not written, so the compiler is not handed a second record to report
    /// against a generated file instead of the description.
    /// </summary>
    [Fact]
    public void TheFileDeclaringItDifferentlyIsNotWritten()
    {
        using var harness = new TaskHarness();
        var drafts = harness.WriteSpec("drafts.yaml", Resource("drafts"));
        var runs = harness.WriteSpec(
            "runs.yaml",
            Resource("runs").Replace("detail: { type: string }", "detail: { type: integer }")
        );

        var result = harness.Run(drafts, runs);

        Assert.Equal([harness.SourcePathFor("drafts.yaml")], result.GeneratedSources);
    }

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
