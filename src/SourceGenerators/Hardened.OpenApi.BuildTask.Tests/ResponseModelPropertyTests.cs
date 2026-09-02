using Xunit;

namespace Hardened.OpenApi.BuildTask.Tests;

/// <summary>
/// How the task reads <c>$(HardenedResponseModel)</c> after the 0.19.0 rename: <c>Throws</c> is
/// the value, <c>Standard</c> is the value's old name, and absence still means throws mode.
/// </summary>
/// <remarks>
/// The rename cases assert output equality against an explicit <c>Throws</c> run rather than
/// asserting anything about the generated text, because the claim is "the same mode", not "a mode
/// that looks like this".
/// </remarks>
public class ResponseModelPropertyTests {

    private const string Spec = """
        openapi: 3.0.0
        info: { title: Pets, version: 1.0.0 }
        paths:
          /pets/{id}:
            get:
              operationId: getPet
              tags: [Pets]
              parameters:
                - name: id
                  in: path
                  required: true
                  schema: { type: string }
              responses:
                '200':
                  description: One pet.
                  content:
                    application/json:
                      schema: { $ref: '#/components/schemas/Pet' }
        components:
          schemas:
            Pet:
              type: object
              properties:
                name: { type: string }
        """;

    private static string Generated(string responseModel) {
        using var harness = new TaskHarness();
        var path = harness.WriteSpec("pets.yaml", Spec);

        var result = harness.RunWithResponseModel(responseModel, path);
        Assert.True(result.Succeeded, result.ErrorText);

        return File.ReadAllText(harness.SourcePathFor("pets.yaml"));
    }

    [Fact]
    public void TheRenamedStandardValue_SelectsThrowsMode() {
        Assert.Equal(Generated("Throws"), Generated("Standard"));
    }

    [Fact]
    public void AnAbsentValue_StillMeansThrowsMode() {
        Assert.Equal(Generated("Throws"), Generated(""));
    }

    [Fact]
    public void TheRenamedStandardValue_DrawsThe026RenameNotice() {
        using var harness = new TaskHarness();
        var path = harness.WriteSpec("pets.yaml", Spec);

        var result = harness.RunWithResponseModel("Standard", path);

        Assert.True(result.Succeeded, result.ErrorText);
        Assert.Equal(1, result.WarningCount("HOAT026"));
    }

    /// <summary>
    /// Once per project, not once per spec: the property is project-wide, and a project with four
    /// descriptions has one thing to fix.
    /// </summary>
    [Fact]
    public void TheRenameNotice_IsReportedOnceForManySpecs() {
        using var harness = new TaskHarness();
        var first = harness.WriteSpec("pets.yaml", Spec);
        var second = harness.WriteSpec("more-pets.yaml", Spec.Replace("getPet", "getMorePet"));

        var result = harness.RunWithResponseModel("Standard", first, second);

        Assert.True(result.Succeeded, result.ErrorText);
        Assert.Equal(1, result.WarningCount("HOAT026"));
    }

    [Fact]
    public void TheCurrentValues_DrawNoRenameNotice() {
        foreach (var value in new[] { "Throws", "Response", "Union", "" }) {
            using var harness = new TaskHarness();
            var path = harness.WriteSpec("pets.yaml", Spec);

            var result = harness.RunWithResponseModel(value, path);

            Assert.True(result.Succeeded, result.ErrorText);
            Assert.Equal(0, result.WarningCount("HOAT026"));
        }
    }
}
