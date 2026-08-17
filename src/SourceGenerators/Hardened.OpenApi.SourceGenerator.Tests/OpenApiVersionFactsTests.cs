using Hardened.SourceGenerator.OpenApiDocument;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Hardened.OpenApi.SourceGenerator.Tests;

/// <summary>
/// <c>OpenApiVersionFacts</c>, as this project compiles it.
/// </summary>
/// <remarks>
/// <para>
/// A second copy of assertions <c>Hardened.SourceGenerator.Tests.OpenApiVersionTests</c> already
/// makes, and deliberately so. <c>OpenApiDocument/**</c> is linked into this project as source
/// rather than referenced, so what runs here is a different compilation of the same file - and a
/// test over the other copy says nothing about this one. The coverage gate is what makes that
/// visible: linking fifty lines nothing in this project exercised dropped its line coverage by a
/// point without any code being less tested than before.
/// </para>
/// <para>
/// Kept small on purpose. The behaviour is covered in full where the code lives; this is here to
/// exercise the copy, not to restate the contract.
/// </para>
/// </remarks>
public class OpenApiVersionFactsTests {

    [Theory]
    [InlineData(null, OpenApiVersion.V3_2)]
    [InlineData("3.0.0", OpenApiVersion.V3_0)]
    [InlineData("3.1.0", OpenApiVersion.V3_1)]
    [InlineData("3.2.0", OpenApiVersion.V3_2)]
    public void ARecognisedVersionParses(string? configured, OpenApiVersion expected) {
        Assert.Equal(expected, OpenApiVersionFacts.Parse(configured));
    }

    [Fact]
    public void AnUnrecognisedVersionHasNoAnswer() {
        Assert.Null(OpenApiVersionFacts.Parse("3.9.9"));
    }

    [Theory]
    [InlineData(OpenApiVersion.V3_0, "3.0.0")]
    [InlineData(OpenApiVersion.V3_1, "3.1.0")]
    [InlineData(OpenApiVersion.V3_2, "3.2.0")]
    public void TheDocumentDeclaresThePatchVersion(OpenApiVersion version, string expected) {
        Assert.Equal(expected, OpenApiVersionFacts.VersionString(version));
    }

    [Fact]
    public void OnlyThreeTwoCanDescribeAStream() {
        Assert.False(OpenApiVersionFacts.SupportsItemSchema(OpenApiVersion.V3_0));
        Assert.False(OpenApiVersionFacts.SupportsItemSchema(OpenApiVersion.V3_1));
        Assert.True(OpenApiVersionFacts.SupportsItemSchema(OpenApiVersion.V3_2));
    }

    [Fact]
    public void ExclusiveBoundsBecomeNumericAtThreeOne() {
        Assert.False(OpenApiVersionFacts.ExclusiveBoundsAreNumeric(OpenApiVersion.V3_0));
        Assert.True(OpenApiVersionFacts.ExclusiveBoundsAreNumeric(OpenApiVersion.V3_1));
        Assert.True(OpenApiVersionFacts.ExclusiveBoundsAreNumeric(OpenApiVersion.V3_2));
    }

    /// <summary>
    /// The diagnostic descriptors build, and carry the ids and severities the build reports.
    /// </summary>
    /// <remarks>
    /// Both are constructed per call rather than held in a static field, because RS2008 looks for
    /// the field and these projects set <c>EnforceExtendedAnalyzerRules</c>. That makes them worth
    /// constructing once here: a descriptor that threw would fail every build that reported one,
    /// and no generator in this project reports either.
    /// </remarks>
    [Fact]
    public void TheDiagnosticIdsAreStable() {
        Assert.Equal("HRDOA001", OpenApiVersionDiagnostics.UnknownVersionId);
        Assert.Equal("HRDOA002", OpenApiVersionDiagnostics.StreamNeedsItemSchemaId);
    }

    /// <summary>
    /// An unrecognised version is an error, and a stream it cannot describe is a warning.
    /// </summary>
    /// <remarks>
    /// The severities are the design and not an implementation detail. Emitting a version other
    /// than the one asked for cannot be allowed to pass, because the consumer whose toolchain
    /// needed it would find out from their generator rather than from this build. A stream the
    /// document cannot describe still builds and still streams correctly - someone pinned to 3.0
    /// for a reader that needs it has made a trade, and what they must not do is believe the
    /// document describes the operation.
    /// </remarks>
    [Fact]
    public void TheSeveritiesSayWhichOneStopsABuild() {
        var unknown = OpenApiVersionDiagnostics.UnknownVersionDescriptor();
        var stream = OpenApiVersionDiagnostics.StreamNeedsItemSchemaDescriptor();

        Assert.Equal(DiagnosticSeverity.Error, unknown.DefaultSeverity);
        Assert.Equal(DiagnosticSeverity.Warning, stream.DefaultSeverity);

        Assert.Equal(OpenApiVersionDiagnostics.UnknownVersionId, unknown.Id);
        Assert.Equal(OpenApiVersionDiagnostics.StreamNeedsItemSchemaId, stream.Id);

        Assert.True(unknown.IsEnabledByDefault);
        Assert.True(stream.IsEnabledByDefault);

        // The message names the property, because the reader of it has to know what to change.
        Assert.Contains(OpenApiVersionFacts.PropertyName, string.Format(
            unknown.MessageFormat.ToString(),
            OpenApiVersionFacts.PropertyName, "3.9.9", "3.2.0"));
    }
}
