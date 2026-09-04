using System.Text;
using Xunit;

namespace Hardened.OpenApiDocument.BuildTask.Tests;

/// <summary>
/// Reading the served document out of a compiled assembly, over both lowerings and over what the
/// pinned SDK actually built.
/// </summary>
public class ServedDocumentReaderTests : IDisposable {

    private readonly TaskHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    private string Fixture(string name, params PeFixture.Document[] documents) {
        var path = _harness.Under(name + ".dll");

        PeFixture.Write(path, documents);

        return path;
    }

    [Theory]
    [InlineData(PeFixture.Lowering.FieldAddress, "FieldAddress")]
    [InlineData(PeFixture.Lowering.FieldToken, "FieldToken")]
    public void ReadsEitherLowering(PeFixture.Lowering lowering, string expected) {
        var compressed = DocumentFixture.Compressed();
        var path = Fixture("either", new PeFixture.Document("Application", compressed, lowering));

        var document = Assert.Single(ServedDocumentReader.Read(path));

        Assert.Equal("Fixture.Application", document.EntryPoint);
        Assert.Equal(expected, document.Lowering);
        Assert.Equal(compressed, document.Compressed);
        Assert.Equal(DocumentFixture.Compact, Encoding.UTF8.GetString(ServedDocumentReader.Inflate(document.Compressed)));
    }

    /// <summary>
    /// The assemblies the pinned SDK built for the three front ends, which is the lowering a real
    /// build produces today.
    /// </summary>
    [Theory]
    [InlineData(TaskHarness.WebApp)]
    [InlineData(TaskHarness.OpenApiApp)]
    [InlineData(TaskHarness.SmithyApp)]
    public void ReadsWhatThePinnedSdkBuilt(string assembly) {
        var document = Assert.Single(ServedDocumentReader.Read(TaskHarness.Fixture(assembly)));

        Assert.NotEqual("None", document.Lowering);

        var inflated = Encoding.UTF8.GetString(ServedDocumentReader.Inflate(document.Compressed));

        Assert.StartsWith(ServedDocumentReader.ExpectedPrefix, inflated);
    }

    [Fact]
    public void AnAssemblyWithoutTheGetterCarriesNothing() {
        Assert.Empty(ServedDocumentReader.Read(Fixture("empty")));
    }

    [Fact]
    public void TwoEntryPointsAreTwoDocuments() {
        var path = Fixture("two",
            new PeFixture.Document("First", DocumentFixture.Compressed(), PeFixture.Lowering.FieldAddress),
            new PeFixture.Document("Second", DocumentFixture.Compressed("{\"openapi\":\"3.2.0\",\"paths\":{}}"), PeFixture.Lowering.FieldToken));

        var documents = ServedDocumentReader.Read(path);

        Assert.Equal(new[] { "Fixture.First", "Fixture.Second" }, documents.Select(document => document.EntryPoint));
    }

    [Fact]
    public void AGetterWithNoDataFieldFailsNamingTheEntryPoint() {
        var path = Fixture("nofield", new PeFixture.Document("Application", DocumentFixture.Compressed(), PeFixture.Lowering.NoField));

        var failure = Assert.Throws<ServedDocumentException>(() => ServedDocumentReader.Read(path));

        Assert.Equal("Fixture.Application", failure.EntryPoint);
        Assert.Contains("no data field", failure.Message);
    }

    [Fact]
    public void ADeclaredLengthThatDisagreesWithTheFieldFails() {
        var compressed = DocumentFixture.Compressed();
        var path = Fixture("short",
            new PeFixture.Document("Application", compressed, PeFixture.Lowering.FieldAddress, compressed.Length - 1));

        var failure = Assert.Throws<ServedDocumentException>(() => ServedDocumentReader.Read(path));

        Assert.Contains("declares a length", failure.Message);
    }
}
