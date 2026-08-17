using Hardened.SourceGenerator.OpenApiDocument;
using Xunit;

namespace Hardened.SourceGenerator.Tests;

/// <summary>
/// <c>&lt;HardenedOpenApiVersion&gt;</c>, and what each version changes about the document.
/// </summary>
/// <remarks>
/// <para>
/// The property exists because a consumer's toolchain may not read the newest document, and the
/// client story points them at the served document and their own generator. So the failure that
/// matters is not "the wrong version was emitted" but "a version other than the one asked for was
/// emitted, quietly" - which is why an unrecognised value is an error rather than a fallback, and
/// why that is asserted here rather than left to a reviewer.
/// </para>
/// <para>
/// Here rather than beside the generator tests because this is where the code lives:
/// <c>OpenApiVersionFacts</c> is in <c>Hardened.SourceGenerator</c>, which ships as source and is
/// compiled into several generator projects. A test in one consumer covers the copy in that
/// consumer and nothing else, which is a coverage gate away from being noticed.
/// </para>
/// </remarks>
public class OpenApiVersionTests {

    /// <summary>
    /// Unset is 3.2.0.
    /// </summary>
    /// <remarks>
    /// Decided on Kiota, which has parsed 3.2 since <c>v1.30.0</c> in January 2026, and because
    /// <c>itemSchema</c> is the only spelling in any version that can describe a streamed response.
    /// A default that could not describe handlers the application declares would be the wrong one.
    /// </remarks>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AnUnsetVersionIsTheDefault(string? configured) {
        Assert.Equal(OpenApiVersion.V3_2, OpenApiVersionFacts.Parse(configured));
        Assert.Equal(OpenApiVersion.V3_2, OpenApiVersionFacts.Default);
    }

    [Theory]
    [InlineData("3.0.0", OpenApiVersion.V3_0)]
    [InlineData("3.0", OpenApiVersion.V3_0)]
    [InlineData("3.1.0", OpenApiVersion.V3_1)]
    [InlineData("3.1", OpenApiVersion.V3_1)]
    [InlineData("3.2.0", OpenApiVersion.V3_2)]
    [InlineData("3.2", OpenApiVersion.V3_2)]
    [InlineData("  3.1.0  ", OpenApiVersion.V3_1)]
    public void ARecognisedVersionParses(string configured, OpenApiVersion expected) {
        Assert.Equal(expected, OpenApiVersionFacts.Parse(configured));
    }

    /// <summary>
    /// Anything else is <c>null</c>, which the caller reports as a build error.
    /// </summary>
    /// <remarks>
    /// The case worth pinning is <c>"3.3.0"</c> and <c>"3"</c>: both look like versions, and a
    /// <c>Parse</c> that fell back to the default for them would emit 3.2.0 to a build that asked
    /// for something else and say nothing. The property is set precisely by people who cannot
    /// afford that.
    /// </remarks>
    [Theory]
    [InlineData("3.3.0")]
    [InlineData("3")]
    [InlineData("2.0")]
    [InlineData("v3.1")]
    [InlineData("latest")]
    public void AnUnrecognisedVersionHasNoAnswer(string configured) {
        Assert.Null(OpenApiVersionFacts.Parse(configured));
    }

    [Theory]
    [InlineData(OpenApiVersion.V3_0, "3.0.0")]
    [InlineData(OpenApiVersion.V3_1, "3.1.0")]
    [InlineData(OpenApiVersion.V3_2, "3.2.0")]
    public void TheDocumentDeclaresThePatchVersion(OpenApiVersion version, string expected) {
        Assert.Equal(expected, OpenApiVersionFacts.VersionString(version));
    }

    /// <summary>
    /// Only 3.2 can describe a streamed response.
    /// </summary>
    /// <remarks>
    /// <c>itemSchema</c> arrived in 3.2. Before it there is no way to say "many of these, one after
    /// another": putting the item's schema under <c>schema</c> claims the response is a single one
    /// of them, which is what the document said before the property existed.
    /// </remarks>
    [Theory]
    [InlineData(OpenApiVersion.V3_0, false)]
    [InlineData(OpenApiVersion.V3_1, false)]
    [InlineData(OpenApiVersion.V3_2, true)]
    public void OnlyThreeTwoCanDescribeAStream(OpenApiVersion version, bool supported) {
        Assert.Equal(supported, OpenApiVersionFacts.SupportsItemSchema(version));
    }

    /// <summary>
    /// 3.0 writes an exclusive bound as a flag; 3.1 onward writes it as the number.
    /// </summary>
    /// <remarks>
    /// Asserted even though <c>SchemaConstraintWriter</c> does not yet branch on it, because the
    /// fact is what the follow-up work needs and stating it here is what makes the gap visible
    /// rather than forgotten. See that type's remarks and STREAMING-PLAN.md item 10.
    /// </remarks>
    [Theory]
    [InlineData(OpenApiVersion.V3_0, false)]
    [InlineData(OpenApiVersion.V3_1, true)]
    [InlineData(OpenApiVersion.V3_2, true)]
    public void ExclusiveBoundsBecomeNumericAtThreeOne(OpenApiVersion version, bool numeric) {
        Assert.Equal(numeric, OpenApiVersionFacts.ExclusiveBoundsAreNumeric(version));
    }
}
