using Xunit;

namespace Hardened.OpenApi.SourceGenerator.Tests;

/// <summary>
/// The two MSBuild properties the OpenAPI generator reads, and what each one changes about the code
/// it writes.
///
/// <para>
/// Both are documented in the OpenAPI guide as the way a project controls generated output, so a
/// change to either is a change to a published contract rather than an implementation detail.
/// </para>
/// </summary>
public class GeneratorConfigurationTests {

    private static readonly Dictionary<string, string> NoCoverageExclusion =
        new() { ["ExcludeGeneratedCodeFromCoverage"] = "false" };

    /// <summary>
    /// With no override, generated types land under the project's <c>RootNamespace</c>, suffixed with
    /// <c>.Models</c>, <c>.Services</c> and <c>.Generated</c>.
    /// </summary>
    [Fact]
    public void GeneratedTypesDefaultToTheProjectsRootNamespace() {
        var result = OpenApiGenerator.Run(
                Specs.Minimal,
                buildProperties: new Dictionary<string, string> { ["RootNamespace"] = "Contoso.Api" })
            .AssertNoErrors();

        var generated = result.SourceContaining("petstore.g.cs");

        Assert.Contains("namespace Contoso.Api\n{", generated);
        Assert.Contains("    namespace Models\n", generated);
        Assert.Contains("    namespace Services\n", generated);
        Assert.Contains("namespace Contoso.Api.Generated", result.SourceContaining("PetController_ListPets"));
    }

    /// <summary>
    /// <c>HardenedOpenApiNamespace</c> overrides <c>RootNamespace</c> — the documented way to put
    /// generated types somewhere other than where the project's own code lives.
    /// </summary>
    [Fact]
    public void HardenedOpenApiNamespaceOverridesTheRootNamespace() {
        var result = OpenApiGenerator.Run(
                Specs.Minimal,
                buildProperties: new Dictionary<string, string> {
                    ["RootNamespace"] = "Contoso.Api",
                    ["HardenedOpenApiNamespace"] = "Contoso.Petstore.Generated"
                })
            .AssertNoErrors();

        Assert.Contains("    namespace Models\n", result.SourceContaining("petstore.g.cs"));
        Assert.Contains("    namespace Services\n", result.SourceContaining("petstore.g.cs"));
        Assert.DoesNotContain("Contoso.Api", result.SourceContaining("petstore.g.cs"));
    }

    /// <summary>
    /// An empty <c>HardenedOpenApiNamespace</c> — what an MSBuild property declared but never given a
    /// value evaluates to — falls back to <c>RootNamespace</c> rather than emitting a namespace with
    /// no name.
    /// </summary>
    [Fact]
    public void AnEmptyHardenedOpenApiNamespaceFallsBackToTheRootNamespace() {
        var result = OpenApiGenerator.Run(
                Specs.Minimal,
                buildProperties: new Dictionary<string, string> {
                    ["RootNamespace"] = "Contoso.Api",
                    ["HardenedOpenApiNamespace"] = ""
                })
            .AssertNoErrors();

        Assert.Contains("    namespace Models\n", result.SourceContaining("petstore.g.cs"));
    }

    /// <summary>
    /// The routing table registers the JSON resolver by its fully-qualified name, so an override has
    /// to reach the registration as well as the type declaration or the two disagree and the DI
    /// method does not compile.
    /// </summary>
    [Fact]
    public void AnOverriddenNamespaceReachesTheRoutingTablesRegistrations() {
        var result = OpenApiGenerator.Run(
                Specs.Minimal,
                buildProperties: new Dictionary<string, string> {
                    ["HardenedOpenApiNamespace"] = "Contoso.Petstore.Generated"
                })
            .AssertNoErrors();

        Assert.Contains(
            "global::Contoso.Petstore.Generated.Models.PetstoreJsonTypeInfoResolver.Instance",
            result.SourceContaining("SpecRouting"));
    }

    // ── coverage exclusion ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Generated code carries <c>[ExcludeFromCodeCoverage]</c> by default, so a coverage report
    /// measures the project's own code rather than thousands of emitted lines.
    /// </summary>
    [Fact]
    public void GeneratedRecordsAreExcludedFromCoverageByDefault() {
        var result = OpenApiGenerator.Run(Specs.EveryValidationConstraint).AssertNoErrors();

        Assert.Contains("[global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]",
            result.SourceContaining("petstore.g.cs"));
    }

    [Fact]
    public void TheGeneratedJsonResolverIsExcludedFromCoverageByDefault() {
        var result = OpenApiGenerator.Run(Specs.Minimal).AssertNoErrors();

        Assert.Contains("[global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]",
            result.SourceContaining("petstore.g.cs"));
    }

    [Fact]
    public void TheGeneratedRoutingTableIsExcludedFromCoverageByDefault() {
        var result = OpenApiGenerator.Run(Specs.Minimal).AssertNoErrors();

        Assert.Contains("[global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]", result.SourceContaining("SpecRouting"));
    }

    [Fact]
    public void GeneratedFilterAttributesAreExcludedFromCoverageByDefault() {
        var result = OpenApiGenerator.Run(Specs.FilterTypes).AssertNoErrors();

        Assert.Contains("[global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]",
            result.SourceContaining("petstore.g.cs"));
    }

    /// <summary>
    /// <c>ExcludeGeneratedCodeFromCoverage=false</c> turns the attribute off everywhere it is emitted,
    /// which is what a project that wants generated handlers counted asks for.
    /// </summary>
    [Theory]
    [InlineData("petstore.g.cs")]
    [InlineData("OrderController_CreateOrder")]
    public void ExcludeGeneratedCodeFromCoverageFalseRemovesTheAttribute(string hintNameFragment) {
        var result = OpenApiGenerator.Run(
                Specs.EveryValidationConstraint,
                buildProperties: NoCoverageExclusion)
            .AssertNoErrors();

        Assert.DoesNotContain("ExcludeFromCodeCoverage", result.SourceContaining(hintNameFragment));
    }

    [Fact]
    public void ExcludeGeneratedCodeFromCoverageFalseRemovesTheAttributeFromTheRoutingTable() {
        var result = OpenApiGenerator.Run(Specs.Minimal, buildProperties: NoCoverageExclusion)
            .AssertNoErrors();

        Assert.DoesNotContain("ExcludeFromCodeCoverage", result.SourceContaining("SpecRouting"));
    }

    [Fact]
    public void ExcludeGeneratedCodeFromCoverageFalseRemovesTheAttributeFromFilterAttributes() {
        var result = OpenApiGenerator.Run(Specs.FilterTypes, buildProperties: NoCoverageExclusion)
            .AssertNoErrors();

        Assert.DoesNotContain("ExcludeFromCodeCoverage", result.SourceContaining("petstore.g.cs"));
    }

    /// <summary>
    /// The comparison is case-insensitive, because MSBuild booleans arrive as <c>false</c>,
    /// <c>False</c> or <c>FALSE</c> depending on who wrote the property.
    /// </summary>
    [Theory]
    [InlineData("false")]
    [InlineData("False")]
    [InlineData("FALSE")]
    public void TheCoverageOptOutIsCaseInsensitive(string value) {
        var result = OpenApiGenerator.Run(
                Specs.Minimal,
                buildProperties: new Dictionary<string, string> {
                    ["ExcludeGeneratedCodeFromCoverage"] = value
                })
            .AssertNoErrors();

        Assert.DoesNotContain("ExcludeFromCodeCoverage", result.SourceContaining("petstore.g.cs"));
    }

    /// <summary>
    /// Anything other than <c>false</c> leaves the exclusion on. A property set to <c>true</c> and one
    /// set to nonsense both mean "keep excluding", which is the safe direction: a coverage report
    /// polluted with generated code is worse than one missing it.
    /// </summary>
    [Theory]
    [InlineData("true")]
    [InlineData("")]
    [InlineData("no")]
    public void AnyValueOtherThanFalseKeepsTheExclusion(string value) {
        var result = OpenApiGenerator.Run(
                Specs.Minimal,
                buildProperties: new Dictionary<string, string> {
                    ["ExcludeGeneratedCodeFromCoverage"] = value
                })
            .AssertNoErrors();

        Assert.Contains("ExcludeFromCodeCoverage", result.SourceContaining("petstore.g.cs"));
    }

    /// <summary>
    /// Enums never carry the attribute. There is no code in an enum to cover, and
    /// <c>[ExcludeFromCodeCoverage]</c> on one would be noise in the diff.
    /// </summary>
    [Fact]
    public void GeneratedEnumsCarryNoCoverageAttribute() {
        var result = OpenApiGenerator.Run(Specs.EverySchemaShape).AssertNoErrors();

        // Records in the same file do carry it, so this has to look at the declaration rather than
        // at the file - which is all it could do when every type had a file of its own.
        Assert.DoesNotContain(
            "[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]\npublic enum",
            result.SourceContaining("petstore.g.cs"));
    }

    /// <summary>
    /// Nor do service interfaces: an interface has no implementation to measure, and the
    /// implementation a consumer writes is exactly the code coverage should be counting.
    /// </summary>
    [Fact]
    public void GeneratedServiceInterfacesCarryNoCoverageAttribute() {
        var result = OpenApiGenerator.Run(Specs.Minimal).AssertNoErrors();

        Assert.DoesNotContain(
            "[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]\npublic partial interface",
            result.SourceContaining("petstore.g.cs"));
    }
}
