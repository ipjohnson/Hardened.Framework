using Xunit;

namespace Hardened.OpenApi.SourceGenerator.Tests;

/// <summary>
/// A project with more than one OpenAPI document.
///
/// <para>
/// The specification's file name becomes the prefix on generated file names, so a project can carry
/// several specs without collision. Both halves of that hold now. They did not: every specification
/// emitted its own <c>OpenApiJsonTypeInfoResolver</c> into <c>&lt;RootNamespace&gt;.Models</c> as a
/// non-partial sealed class, so a second document failed the build with CS0101 whatever the
/// documents contained - finding 3.1 of OPENAPI-GENERATOR-FINDINGS.md.
/// </para>
///
/// <para>
/// The resolver is named after its spec now, and named in one place - the build task - which then
/// tells the generator what to register. So these tests assert compilation rather than describing
/// why they could not, which is what this file said until the task took over naming.
/// </para>
/// </summary>
public class MultipleSpecificationTests {

    private static readonly Dictionary<string, string> TwoSpecs = new() {
        ["pets.yaml"] = Specs.Minimal,
        ["stores.yaml"] = Specs.SecondSpecWithADifferentTag
    };

    /// <summary>
    /// Every per-schema file carries the name of the document it came from, so two documents each
    /// declaring a <c>Pet</c> produce two distinct hint names rather than one silently overwriting
    /// the other.
    /// </summary>
    [Fact]
    public void EachSpecificationsFilesCarryItsOwnNameAsAPrefix() {
        var result = OpenApiGenerator.Run(TwoSpecs, OpenApiGenerator.MinimalEntryPoint).AssertNoErrors();

        Assert.Contains("pets.g.cs", result.GeneratedSources.Keys);
        Assert.Contains("stores.g.cs", result.GeneratedSources.Keys);

        // Each document's types land in its own file, so two documents declaring a Pet do not
        // overwrite one another.
        Assert.Contains("record Pet", result.GeneratedSources["pets.g.cs"]);
        Assert.Contains("IPetService", result.GeneratedSources["pets.g.cs"]);
        Assert.Contains("record Store", result.GeneratedSources["stores.g.cs"]);
        Assert.Contains("IStoreService", result.GeneratedSources["stores.g.cs"]);
    }

    /// <summary>
    /// No generator run may emit the same hint name twice: Roslyn takes the last writer, so a
    /// collision loses a file with no error at all.
    /// </summary>
    [Fact]
    public void NoHintNameIsEmittedTwiceAcrossSpecifications() {
        var result = OpenApiGenerator.Run(TwoSpecs, OpenApiGenerator.MinimalEntryPoint).AssertNoErrors();

        Assert.Empty(result.DuplicateHintNames);
        Assert.Empty(result.GeneratorExceptions);
    }

    /// <summary>
    /// Handlers are named from the tag and operation, so two documents with different tags produce
    /// handler classes that do not collide even though handler hint names carry no spec prefix.
    /// </summary>
    [Fact]
    public void EachSpecificationGetsItsOwnHandlersNamedFromItsTags() {
        var result = OpenApiGenerator.Run(TwoSpecs, OpenApiGenerator.MinimalEntryPoint).AssertNoErrors();

        Assert.Contains("PetController_ListPets.cs", result.GeneratedSources.Keys);
        Assert.Contains("StoreController_ListStores.cs", result.GeneratedSources.Keys);
    }

    /// <summary>
    /// Both documents' operations reach one routing table. A project with two specifications has one
    /// application, and a route from either has to resolve.
    /// </summary>
    [Fact]
    public void BothSpecificationsRoutesReachTheSameRoutingTable() {
        var result = OpenApiGenerator.Run(TwoSpecs, OpenApiGenerator.MinimalEntryPoint).AssertNoErrors();

        var routing = result.SourceContaining("SpecRouting");

        Assert.Contains("PetController_ListPets", routing);
        Assert.Contains("StoreController_ListStores", routing);
    }

    /// <summary>
    /// Both documents are counted in the diagnostic file, which is how a build that generated only
    /// half of what was expected is diagnosed.
    /// </summary>
    [Fact]
    public void TheDiagnosticFileCountsEverySpecification() {
        var result = OpenApiGenerator.Run(TwoSpecs, OpenApiGenerator.MinimalEntryPoint).AssertNoErrors();

        var diagnosticFile = result.GeneratedSources[OpenApiGenerator.DiagnosticHintName];

        Assert.Contains("Total AdditionalTexts: 2", diagnosticFile);
        Assert.Contains("OpenAPI files parsed: 2", diagnosticFile);

        // The paths are the models the build task wrote, not the specs it read from.
        Assert.Contains("pets.openapi-model.txt", diagnosticFile);
        Assert.Contains("stores.openapi-model.txt", diagnosticFile);
    }

    /// <summary>
    /// A YAML document and a JSON document side by side: the extension decides how the file is read,
    /// not which generator claims it.
    /// </summary>
    [Fact]
    public void AYamlAndAJsonSpecificationAreBothParsed() {
        var result = OpenApiGenerator.Run(
            new Dictionary<string, string> {
                ["pets.yaml"] = Specs.Minimal,
                ["items.json"] = Specs.MinimalJson
            },
            OpenApiGenerator.MinimalEntryPoint);

        Assert.Contains("OpenAPI files parsed: 2",
            result.GeneratedSources[OpenApiGenerator.DiagnosticHintName]);
        Assert.Contains("IPetService", result.GeneratedSources["pets.g.cs"]);
    }

    /// <summary>
    /// A document whose null return writes a body, twice.
    /// </summary>
    /// <remarks>
    /// The holder every generated instance is a field on was called <c>DefaultErrorBodies</c>
    /// whatever document produced it, so two contracts that each declared a fillable 404 emitted
    /// the class twice into one namespace: CS0101 on the second document, and a CS0229 on every
    /// handler naming a field. Every other per-document holder this emitter writes already carried
    /// the file name.
    /// </remarks>
    [Fact]
    public void TwoDocumentsThatEachWriteAnErrorBodyCompile() {
        var result = OpenApiGenerator.Run(
            new Dictionary<string, string> {
                ["pets.yaml"] = ErrorBodySpec("pets", "Pet", "PetProblem"),
                ["stores.yaml"] = ErrorBodySpec("stores", "Store", "StoreProblem")
            },
            OpenApiGenerator.MinimalEntryPoint);

        Assert.Empty(result.Errors);
    }

    /// <summary>And each holder is named after the document that produced it.</summary>
    [Fact]
    public void EachDocumentsErrorBodyHolderIsNamedAfterIt() {
        var result = OpenApiGenerator.Run(
            new Dictionary<string, string> {
                ["pets.yaml"] = ErrorBodySpec("pets", "Pet", "PetProblem"),
                ["stores.yaml"] = ErrorBodySpec("stores", "Store", "StoreProblem")
            },
            OpenApiGenerator.MinimalEntryPoint).AssertNoErrors();

        Assert.Contains("class PetsErrorBodies", result.GeneratedSources["pets.g.cs"]);
        Assert.Contains("class StoresErrorBodies", result.GeneratedSources["stores.g.cs"]);
        Assert.DoesNotContain("DefaultErrorBodies", result.GeneratedSources["pets.g.cs"]);
    }

    /// <summary>
    /// A GET declaring a 404 over a schema whose members can all be filled, which is what puts an
    /// instance in the holder.
    /// </summary>
    private static string ErrorBodySpec(string tag, string model, string problem) =>
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
                      schema: { $ref: '#/components/schemas/{{model}}' }
                '404':
                  description: missing
                  content:
                    application/json:
                      schema: { $ref: '#/components/schemas/{{problem}}' }
        components:
          schemas:
            {{model}}:
              type: object
              properties:
                id: { type: string }
            {{problem}}:
              type: object
              properties:
                type: { type: string }
                title: { type: string }
                status: { type: integer }
        """;
}
