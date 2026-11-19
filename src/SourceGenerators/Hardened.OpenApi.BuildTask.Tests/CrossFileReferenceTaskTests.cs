using System.Text.RegularExpressions;
using Hardened.Generation;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Xunit;

namespace Hardened.OpenApi.BuildTask.Tests;

/// <summary>
/// A <c>$ref</c> from one description of a project to a component another declares.
/// </summary>
/// <remarks>
/// Whether the shared type then compiles, and whether ValidationModules writes its validator once,
/// is asserted in Hardened.OpenApi.SourceGenerator.Tests, which compiles what the task emits.
/// </remarks>
public class CrossFileReferenceTaskTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ASchemaOfAnotherFileIsWrittenOnce(bool referencingFileFirst)
    {
        using var harness = new TaskHarness();
        var drafts = harness.WriteSpec("drafts.yaml", Drafts("shared.yaml"));
        var shared = harness.WriteSpec("shared.yaml", Shared);

        var result = referencingFileFirst
            ? harness.Run(drafts, shared)
            : harness.Run(shared, drafts);

        Assert.True(result.Succeeded, result.ErrorText);

        var emitted =
            File.ReadAllText(harness.SourcePathFor("drafts.yaml"))
            + File.ReadAllText(harness.SourcePathFor("shared.yaml"));

        Assert.Single(Regex.Matches(emitted, @"record Refusal\("));
        Assert.Single(Regex.Matches(emitted, @"record RefusalDetail\("));
    }

    /// <summary>
    /// The reference on a property is the one the reader drops without a word when it cannot
    /// follow it, which would type the member as JsonElement.
    /// </summary>
    [Fact]
    public void APropertyReferenceToAnotherFileIsTypedByThatSchema()
    {
        using var harness = new TaskHarness();
        var drafts = harness.WriteSpec("drafts.yaml", Drafts("shared.yaml"));
        var shared = harness.WriteSpec("shared.yaml", Shared);

        var result = harness.Run(drafts, shared);

        Assert.True(result.Succeeded, result.ErrorText);

        var draft = Model(harness, "drafts.yaml").Schemas.Single(schema => schema.Name == "Draft");

        Assert.Equal(
            "#/components/schemas/Refusal",
            draft.Properties.Single(property => property.Name == "refusal").Ref
        );
    }

    [Fact]
    public void AParameterOfAnotherFileIsBound()
    {
        using var harness = new TaskHarness();
        var drafts = harness.WriteSpec("drafts.yaml", Drafts("shared.yaml"));
        var shared = harness.WriteSpec("shared.yaml", Shared);

        var result = harness.Run(drafts, shared);

        Assert.True(result.Succeeded, result.ErrorText);

        var operation = Model(harness, "drafts.yaml")
            .Services.SelectMany(service => service.Operations)
            .Single();

        Assert.Contains(
            operation.Parameters,
            parameter => parameter.Name == "workspace" && parameter.In == "path"
        );
    }

    /// <summary>The path before '#' is relative to the file that holds the reference.</summary>
    [Fact]
    public void TheFileIsRelativeToTheFileThatHoldsTheReference()
    {
        using var harness = new TaskHarness();
        Directory.CreateDirectory(Path.Combine(harness.SpecDirectory, "api"));
        Directory.CreateDirectory(Path.Combine(harness.SpecDirectory, "common"));
        var drafts = harness.WriteSpec(
            Path.Combine("api", "drafts.yaml"),
            Drafts("../common/shared.yaml")
        );
        var shared = harness.WriteSpec(Path.Combine("common", "shared.yaml"), Shared);

        var result = harness.Run(drafts, shared);

        Assert.True(result.Succeeded, result.ErrorText);
    }

    [Fact]
    public void AFileOfComponentsGeneratesItsTypesAndNoService()
    {
        using var harness = new TaskHarness();
        var shared = harness.WriteSpec("shared.yaml", Shared);

        var result = harness.Run(shared);

        Assert.True(result.Succeeded, result.ErrorText);

        var emitted = File.ReadAllText(harness.SourcePathFor("shared.yaml"));

        Assert.Contains("record Refusal(", emitted);
        Assert.Contains("record RefusalDetail(", emitted);
        Assert.DoesNotContain("interface I", emitted);
        Assert.Empty(Model(harness, "shared.yaml").Services);
    }

    [Fact]
    public void AFileTheProjectDoesNotNameIsAnErrorNamingTheFileAndTheSchema()
    {
        using var harness = new TaskHarness();
        var drafts = harness.WriteSpec("drafts.yaml", Drafts("elsewhere.yaml"));
        harness.WriteSpec("elsewhere.yaml", Shared);

        var result = harness.Run(drafts);

        Assert.False(result.Succeeded);

        var error = Assert.Single(
            result.Errors,
            error => error.Code == "HOAT037" && error.Message!.Contains("'Refusal'")
        );

        Assert.Equal(drafts, error.File);
        Assert.Contains(Path.Combine(harness.SpecDirectory, "elsewhere.yaml"), error.Message);
        Assert.Contains("schema 'Refusal'", error.Message);
        Assert.False(result.HasError("HOAT027"), result.ErrorText);
        Assert.Empty(result.GeneratedSources);
    }

    [Fact]
    public void ASchemaTheNamedFileLacksIsAnErrorNamingTheFileAndTheSchema()
    {
        using var harness = new TaskHarness();
        var drafts = harness.WriteSpec("drafts.yaml", Drafts("shared.yaml"));
        var shared = harness.WriteSpec(
            "shared.yaml",
            Shared.Replace("    Refusal:", "    Refused:")
        );

        var result = harness.Run(drafts, shared);

        Assert.False(result.Succeeded);

        var error = Assert.Single(result.Errors, error => error.Code == "HOAT037");

        Assert.Equal(drafts, error.File);
        Assert.Contains($"'{shared}' declares no schema 'Refusal'", error.Message);
        Assert.False(result.HasError("HOAT027"), result.ErrorText);
    }

    [Fact]
    public void AResponseOfAnotherFileIsAnErrorThatSaysItIsNotSupported()
    {
        using var harness = new TaskHarness();
        var drafts = harness.WriteSpec("drafts.yaml", Drafts("shared.yaml", sharedResponse: true));
        var shared = harness.WriteSpec("shared.yaml", Shared);

        var result = harness.Run(drafts, shared);

        Assert.False(result.Succeeded);

        var error = Assert.Single(result.Errors, error => error.Code == "HOAT037");

        Assert.Contains("shared.yaml#/components/responses/NotFound", error.Message);
        Assert.Contains("not supported", error.Message);
    }

    /// <summary>
    /// Copying a schema of another file over one the description declares differently would change
    /// what the description's own references mean.
    /// </summary>
    [Fact]
    public void ASchemaThatDiffersFromTheOneTheDescriptionDeclaresIsAnError()
    {
        using var harness = new TaskHarness();
        var drafts = harness.WriteSpec(
            "drafts.yaml",
            Drafts("shared.yaml")
                + """

                    Refusal:
                      type: object
                      properties:
                        reason: { type: string }
                """
        );
        var shared = harness.WriteSpec("shared.yaml", Shared);

        var result = harness.Run(drafts, shared);

        var error = Assert.Single(result.Errors, error => error.Code == "HOAT037");

        Assert.Contains("'Refusal'", error.Message);
        Assert.Contains(shared, error.Message);
        Assert.Contains(drafts, error.Message);
    }

    /// <summary>
    /// The embedded text is the file as written, so its reference names a file the application
    /// does not serve beside it.
    /// </summary>
    [Fact]
    public void AnEmbeddedDocumentNamingAnotherFileWarns()
    {
        using var harness = new TaskHarness();
        var drafts = harness.WriteSpec("drafts.yaml", Drafts("shared.yaml"));
        var shared = harness.WriteSpec("shared.yaml", Shared);

        var result = harness.Run(drafts, shared);

        Assert.True(result.Succeeded, result.ErrorText);

        var warning = Assert.Single(result.Warnings, warning => warning.Code == "HOAT038");

        Assert.Equal(drafts, warning.File);
        Assert.Contains(shared, warning.Message);
    }

    [Fact]
    public void ADocumentThatIsNotEmbeddedDoesNotWarn()
    {
        using var harness = new TaskHarness();
        var drafts = new TaskItem(harness.WriteSpec("drafts.yaml", Drafts("shared.yaml")));
        drafts.SetMetadata("EmbedDocument", "false");
        var shared = new TaskItem(harness.WriteSpec("shared.yaml", Shared));

        var result = harness.RunItems("", drafts, shared);

        Assert.True(result.Succeeded, result.ErrorText);
        Assert.Equal(0, result.WarningCount("HOAT038"));
    }

    private static Generation.Models.ServiceSpecModel Model(TaskHarness harness, string specFile) =>
        SpecModelSerializer.Read(File.ReadAllText(harness.ModelPathFor(specFile)));

    /// <summary>
    /// A file of shared shapes: components and no paths. Refusal reaches RefusalDetail through a
    /// reference local to this file, which the copy into another file has to follow.
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
          parameters:
            Workspace:
              name: workspace
              in: path
              required: true
              schema: { type: string }
          responses:
            NotFound:
              description: missing
              content:
                application/json:
                  schema: { $ref: '#/components/schemas/Refusal' }
        """;

    /// <summary>
    /// One resource's file, naming the shared refusal, its detail and a path parameter in
    /// <paramref name="sharedFile"/>. Ends inside <c>components/schemas</c>, so a test can append
    /// a schema.
    /// </summary>
    private static string Drafts(string sharedFile, bool sharedResponse = false) =>
        sharedResponse
            ? Drafts(sharedFile)
                .Replace(
                    "    '404':\n          description: missing\n          content:\n"
                        + "            application/json:\n"
                        + $"              schema: {{ $ref: '{sharedFile}#/components/schemas/Refusal' }}",
                    $"    '404': {{ $ref: '{sharedFile}#/components/responses/NotFound' }}"
                )
            : $$"""
                openapi: "3.1.0"
                info: { title: drafts, version: "1.0" }
                paths:
                  /{workspace}/drafts/{id}:
                    get:
                      tags: [drafts]
                      operationId: getDraft
                      parameters:
                        - $ref: '{{sharedFile}}#/components/parameters/Workspace'
                        - name: id
                          in: path
                          required: true
                          schema: { type: string }
                      responses:
                        '200':
                          description: found
                          content:
                            application/json:
                              schema: { $ref: '#/components/schemas/Draft' }
                        '404':
                          description: missing
                          content:
                            application/json:
                              schema: { $ref: '{{sharedFile}}#/components/schemas/Refusal' }
                components:
                  schemas:
                    Draft:
                      type: object
                      properties:
                        id: { type: string }
                        refusal: { $ref: '{{sharedFile}}#/components/schemas/Refusal' }
                """;
}
