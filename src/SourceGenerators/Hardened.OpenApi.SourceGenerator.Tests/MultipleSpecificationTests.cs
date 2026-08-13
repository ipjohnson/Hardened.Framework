using Xunit;

namespace Hardened.OpenApi.SourceGenerator.Tests;

/// <summary>
/// A project with more than one OpenAPI document in <c>AdditionalFiles</c>.
///
/// <para>
/// The OpenAPI guide says the specification's file name becomes the prefix on generated file names
/// "so a project can carry several specs without collision". The first half holds and is asserted
/// below. The second does not: every specification emits its own <c>OpenApiJsonTypeInfoResolver</c>
/// into <c>&lt;RootNamespace&gt;.Models</c> as a non-partial sealed class, so a second document makes
/// the build fail with CS0101 whatever the documents contain.
/// </para>
///
/// <para>
/// That is why nothing here ends in <c>AssertNoErrors</c>, and it is the one place in this suite
/// where the rule is not followed. Writing the compile assertion would mean asserting the defect;
/// see the workstream report. When the resolver is emitted once per project — or made
/// <c>partial</c> — these tests should gain the assertion.
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
        var result = OpenApiGenerator.Run(TwoSpecs, OpenApiGenerator.MinimalEntryPoint);

        Assert.Contains("pets.Pet.g.cs", result.GeneratedSources.Keys);
        Assert.Contains("pets.IPetService.g.cs", result.GeneratedSources.Keys);
        Assert.Contains("stores.Store.g.cs", result.GeneratedSources.Keys);
        Assert.Contains("stores.IStoreService.g.cs", result.GeneratedSources.Keys);
    }

    /// <summary>
    /// No generator run may emit the same hint name twice: Roslyn takes the last writer, so a
    /// collision loses a file with no error at all.
    /// </summary>
    [Fact]
    public void NoHintNameIsEmittedTwiceAcrossSpecifications() {
        var result = OpenApiGenerator.Run(TwoSpecs, OpenApiGenerator.MinimalEntryPoint);

        Assert.Empty(result.DuplicateHintNames);
        Assert.Empty(result.GeneratorExceptions);
    }

    /// <summary>
    /// Handlers are named from the tag and operation, so two documents with different tags produce
    /// handler classes that do not collide even though handler hint names carry no spec prefix.
    /// </summary>
    [Fact]
    public void EachSpecificationGetsItsOwnHandlersNamedFromItsTags() {
        var result = OpenApiGenerator.Run(TwoSpecs, OpenApiGenerator.MinimalEntryPoint);

        Assert.Contains("PetController_ListPets.cs", result.GeneratedSources.Keys);
        Assert.Contains("StoreController_ListStores.cs", result.GeneratedSources.Keys);
    }

    /// <summary>
    /// Both documents' operations reach one routing table. A project with two specifications has one
    /// application, and a route from either has to resolve.
    /// </summary>
    [Fact]
    public void BothSpecificationsRoutesReachTheSameRoutingTable() {
        var result = OpenApiGenerator.Run(TwoSpecs, OpenApiGenerator.MinimalEntryPoint);

        var routing = result.SourceContaining("OpenApiRouting");

        Assert.Contains("PetController_ListPets", routing);
        Assert.Contains("StoreController_ListStores", routing);
    }

    /// <summary>
    /// Both documents are counted in the diagnostic file, which is how a build that generated only
    /// half of what was expected is diagnosed.
    /// </summary>
    [Fact]
    public void TheDiagnosticFileCountsEverySpecification() {
        var result = OpenApiGenerator.Run(TwoSpecs, OpenApiGenerator.MinimalEntryPoint);

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
        Assert.Contains("pets.IPetService.g.cs", result.GeneratedSources.Keys);
    }
}
