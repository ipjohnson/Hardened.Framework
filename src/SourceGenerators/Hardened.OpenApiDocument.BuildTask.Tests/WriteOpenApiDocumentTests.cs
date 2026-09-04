using System.Text;
using Hardened.Generation.Document;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;
using Microsoft.OpenApi.YamlReader;
using Xunit;

namespace Hardened.OpenApiDocument.BuildTask.Tests;

/// <summary>
/// The task end to end: the three integration applications' assemblies in, files out, and every
/// diagnostic with the fix in its message.
/// </summary>
public class WriteOpenApiDocumentTests : IDisposable {

    private readonly TaskHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    private static string ServedDocumentOf(string assembly) {
        var document = Assert.Single(ServedDocumentReader.Read(TaskHarness.Fixture(assembly)));

        return Encoding.UTF8.GetString(ServedDocumentReader.Inflate(document.Compressed));
    }

    private string Fixture(string name, params PeFixture.Document[] documents) {
        var path = _harness.Under(name + ".dll");

        PeFixture.Write(path, documents);

        return path;
    }

    /// <summary>
    /// The file is the served document, indented and nothing else.
    /// </summary>
    [Theory]
    [InlineData(TaskHarness.WebApp)]
    [InlineData(TaskHarness.OpenApiApp)]
    [InlineData(TaskHarness.SmithyApp)]
    public void TheJsonExportIsTheServedDocumentIndented(string assembly) {
        var result = _harness.Run(TaskHarness.Fixture(assembly), "openapi/document.json");

        Assert.True(result.Succeeded, result.ErrorText);
        Assert.Empty(result.Errors);
        Assert.True(result.Changed);

        // The Smithy application's bank service repeats an operation key, which the export reports
        // under 031 and which is not what this test is about. Everything else says nothing.
        Assert.DoesNotContain(result.Warnings, warning => !warning.Code!.EndsWith(
            WriteOpenApiDocument.RepeatedOperationKeyCode, StringComparison.Ordinal));

        var expected = JsonTreeWriter.WriteIndented(JsonTree.Parse(ServedDocumentOf(assembly)));

        Assert.Equal(expected, File.ReadAllText(_harness.Under("openapi/document.json")));
    }

    /// <summary>
    /// A YAML export and a JSON export of the same assembly are one document to the reader the
    /// specification-first build task uses. Asserted by serialising both back through it, the way
    /// OpenApiRoundTripTests holds the generator's output to an independent reader.
    /// </summary>
    [Theory]
    [InlineData(TaskHarness.WebApp)]
    [InlineData(TaskHarness.OpenApiApp)]
    public void TheYamlExportIsTheSameDocumentAsTheJsonExport(string assembly) {
        Assert.True(_harness.Run(TaskHarness.Fixture(assembly), "out.json").Succeeded);
        Assert.True(_harness.Run(TaskHarness.Fixture(assembly), "out.yaml").Succeeded);

        var fromJson = Parse(File.ReadAllText(_harness.Under("out.json")), "json");
        var fromYaml = Parse(File.ReadAllText(_harness.Under("out.yaml")), "yaml");

        Assert.Equal(Canonical(fromJson), Canonical(fromYaml));
    }

    /// <summary>
    /// The Smithy application is not in the theory above, and this is why. Its bank service speaks
    /// the AWS JSON protocol, which puts every operation at <c>POST /</c> and tells them apart by a
    /// header, so the document it serves repeats the <c>post</c> key under one path. That is the
    /// protocol's shape rather than a defect, and the tree this export writes carries it faithfully
    /// in both formats - but a reader that keys operations by name refuses the document, in JSON
    /// and in YAML alike, so there is nothing independent to compare the two exports through. Both
    /// exports still succeed, and the day the served document stops repeating the key this test
    /// fails and the application joins the theory.
    /// </summary>
    /// <remarks>
    /// The export says so now, under <c>031</c>, rather than leaving a file to be discovered
    /// unreadable by whatever was pointed at it. A warning, so the file is still written.
    /// </remarks>
    [Fact]
    public void TheSmithyApplicationRepeatsAnOperationKeyAndStillExportsBothFormats() {
        Assert.True(_harness.Run(TaskHarness.Fixture(TaskHarness.SmithyApp), "out.json").Succeeded);
        Assert.True(_harness.Run(TaskHarness.Fixture(TaskHarness.SmithyApp), "out.yaml").Succeeded);

        var root = (JsonObject)JsonTree.Parse(ServedDocumentOf(TaskHarness.SmithyApp));
        var paths = (JsonObject)root.Get("paths")!;
        var repeated = paths.Members
            .Where(path => ((JsonObject)path.Value).Members.Count(operation => operation.Key == "post") > 1)
            .Select(path => path.Key)
            .ToArray();

        Assert.Equal(new[] { "/" }, repeated);
        Assert.Throws<ArgumentException>(() => Parse(File.ReadAllText(_harness.Under("out.json")), "json"));

        // Every post the tree holds reaches the YAML, the repeated one included.
        var posts = paths.Members.Sum(path => ((JsonObject)path.Value).Members.Count(operation => operation.Key == "post"));
        var yaml = File.ReadAllText(_harness.Under("out.yaml"));

        Assert.Equal(posts, yaml.Split('\n').Count(line => line == "    post:"));
    }

    /// <summary>
    /// And the export says which path, once, rather than leaving the file to be found unreadable
    /// by whatever was pointed at it.
    /// </summary>
    /// <remarks>
    /// One report per path however many operations collide there: the reader's answer is the same
    /// after the second, and the fix - give each operation its own method and path - is one edit to
    /// the model rather than one per operation.
    /// </remarks>
    [Fact]
    public void ARepeatedOperationKeyIsReported() {
        var result = _harness.Run(TaskHarness.Fixture(TaskHarness.SmithyApp), "reported.json");

        Assert.True(result.Succeeded, result.ErrorText);

        var warning = Assert.Single(
            result.Warnings,
            candidate => candidate.Code.EndsWith(
                WriteOpenApiDocument.RepeatedOperationKeyCode, StringComparison.Ordinal));

        Assert.Contains("'/'", warning.Message);
        Assert.Contains("@http", warning.Message);
        Assert.Contains("no client can be generated", warning.Message);

        // The file is still there. The warning is about what a reader will make of it, not about
        // whether writing it was the right thing to do.
        Assert.True(File.Exists(_harness.Under("reported.json")));
    }

    /// <summary>
    /// The applications whose documents key their operations the way OpenAPI does say nothing.
    /// </summary>
    [Theory]
    [InlineData(TaskHarness.WebApp)]
    [InlineData(TaskHarness.OpenApiApp)]
    public void ADocumentWithNoRepeatedKeyIsSilent(string assembly) {
        var result = _harness.Run(TaskHarness.Fixture(assembly), "quiet.json");

        Assert.True(result.Succeeded, result.ErrorText);
        Assert.DoesNotContain(WriteOpenApiDocument.RepeatedOperationKeyCode, result.WarningText);
    }

    [Fact]
    public void ASecondRunLeavesAnUnchangedFileAlone() {
        var assembly = TaskHarness.Fixture(TaskHarness.OpenApiApp);

        var first = _harness.Run(assembly, "same.json");
        var written = File.GetLastWriteTimeUtc(_harness.Under("same.json"));

        Assert.True(first.Changed);

        var second = _harness.Run(assembly, "same.json");

        Assert.True(second.Succeeded, second.ErrorText);
        Assert.False(second.Changed);
        Assert.Equal(written, File.GetLastWriteTimeUtc(_harness.Under("same.json")));
    }

    [Fact]
    public void AChangedDocumentIsRewritten() {
        var assembly = TaskHarness.Fixture(TaskHarness.OpenApiApp);

        _harness.Run(assembly, "changed.json");
        File.WriteAllText(_harness.Under("changed.json"), "stale");

        var result = _harness.Run(assembly, "changed.json");

        Assert.True(result.Changed);
        Assert.StartsWith("{\n", File.ReadAllText(_harness.Under("changed.json")));
    }

    [Fact]
    public void AnUnknownExtensionIsRefusedNamingTheThree() {
        var result = _harness.Run(TaskHarness.Fixture(TaskHarness.OpenApiApp), "openapi/document.txt");

        Assert.False(result.Succeeded);
        Assert.True(result.HasError("HRDOA" + WriteOpenApiDocument.UnknownExtensionCode), result.ErrorText);
        Assert.Contains(".json", result.ErrorText);
        Assert.Contains(".yaml", result.ErrorText);
        Assert.Contains(".yml", result.ErrorText);
        Assert.False(File.Exists(_harness.Under("openapi/document.txt")));
    }

    [Fact]
    public void AnUnknownVersionIsRefusedNamingTheTwo() {
        var result = _harness.Run(TaskHarness.Fixture(TaskHarness.OpenApiApp), "document.json", version: "3.3");

        Assert.False(result.Succeeded);
        Assert.True(result.HasError("HRDOA" + WriteOpenApiDocument.UnknownVersionCode), result.ErrorText);
        Assert.Contains("3.0.0", result.ErrorText);
        Assert.Contains("3.1.0", result.ErrorText);
    }

    /// <summary>
    /// The web application streams from several handlers; each is named once, and the file it
    /// wrote carries the lower banner with no item schemas.
    /// </summary>
    [Fact]
    public void LoweringWarnsOncePerStreamingOperation() {
        var result = _harness.Run(TaskHarness.Fixture(TaskHarness.WebApp), "lowered.json", version: "3.1.0");

        Assert.True(result.Succeeded, result.ErrorText);

        var warnings = result.Warnings
            .Where(warning => warning.Code == "HRDOA" + WriteOpenApiDocument.StreamLostItemSchemaCode)
            .ToArray();

        Assert.NotEmpty(warnings);
        Assert.Equal(warnings.Length, warnings.Select(warning => warning.Message).Distinct().Count());
        Assert.All(warnings, warning => Assert.Contains("HardenedOpenApiOutputVersion", warning.Message));

        var written = File.ReadAllText(_harness.Under("lowered.json"));

        Assert.Contains("\"openapi\": \"3.1.0\"", written);
        Assert.DoesNotContain("itemSchema", written);
    }

    /// <remarks>
    /// Nothing about <em>lowering</em>, which is what this measures. The Smithy application also
    /// repeats an operation key, and 031 says so on every export of it whatever the version.
    /// </remarks>
    [Fact]
    public void LoweringAnApplicationWithNoStreamingWarnsNothing() {
        var result = _harness.Run(TaskHarness.Fixture(TaskHarness.SmithyApp), "lowered.json", version: "3.0.0");

        Assert.True(result.Succeeded, result.ErrorText);
        Assert.DoesNotContain(WriteOpenApiDocument.StreamLostItemSchemaCode, result.WarningText);
        Assert.Contains("\"openapi\": \"3.0.0\"", File.ReadAllText(_harness.Under("lowered.json")));
    }

    /// <summary>The lowered file is still a document to the reader, at the version it declares.</summary>
    [Theory]
    [InlineData("3.0.0")]
    [InlineData("3.1.0")]
    public void ALoweredExportStillParses(string version) {
        var result = _harness.Run(TaskHarness.Fixture(TaskHarness.WebApp), "lowered.yaml", version: version);

        Assert.True(result.Succeeded, result.ErrorText);

        var parsed = Parse(File.ReadAllText(_harness.Under("lowered.yaml")), "yaml");

        Assert.NotEmpty(parsed.Paths);
    }

    [Fact]
    public void NoServedDocumentIsReportedWithTheCodeFirstFix() {
        var result = _harness.Run(Fixture("none"), "document.json");

        Assert.False(result.Succeeded);
        Assert.True(result.HasError("HRDOA" + WriteOpenApiDocument.NoDocumentCode), result.ErrorText);
        Assert.Contains("[Enable<OpenApiDocumentPublishing>]", result.ErrorText);
    }

    [Theory]
    [InlineData("HOAT")]
    [InlineData("HSMT")]
    public void NoServedDocumentIsReportedWithTheSpecFirstFix(string prefix) {
        var result = _harness.Run(Fixture("none"), "document.json", prefix: prefix);

        Assert.False(result.Succeeded);
        Assert.True(result.HasError(prefix + WriteOpenApiDocument.NoDocumentCode), result.ErrorText);
        Assert.Contains(prefix + "004", result.ErrorText);
        Assert.Contains(prefix + "005", result.ErrorText);
        Assert.DoesNotContain("[Enable<", result.ErrorText);
    }

    [Fact]
    public void AGetterTheExportCannotReadIsReportedWithTheFallback() {
        var result = _harness.Run(
            Fixture("nofield", new PeFixture.Document("Application", DocumentFixture.Compressed(), PeFixture.Lowering.NoField)),
            "document.json");

        Assert.False(result.Succeeded);
        Assert.True(result.HasError("HRDOA" + WriteOpenApiDocument.NoDocumentCode), result.ErrorText);
        Assert.Contains("Fixture.Application.OpenApiDocument.GZip", result.ErrorText);
        Assert.Contains("no data field", result.ErrorText);
        Assert.Contains("assembly attribute", result.ErrorText);
    }

    [Fact]
    public void BytesThatAreNotADocumentAreReported() {
        var result = _harness.Run(
            Fixture("notjson", new PeFixture.Document("Application", DocumentFixture.Compressed("hello"), PeFixture.Lowering.FieldAddress)),
            "document.json");

        Assert.False(result.Succeeded);
        Assert.True(result.HasError("HRDOA" + WriteOpenApiDocument.NoDocumentCode), result.ErrorText);
        Assert.Contains("do not inflate to an OpenAPI document", result.ErrorText);
    }

    [Fact]
    public void MoreThanOneServedDocumentIsReportedNamingBoth() {
        var result = _harness.Run(
            Fixture("two",
                new PeFixture.Document("First", DocumentFixture.Compressed(), PeFixture.Lowering.FieldAddress),
                new PeFixture.Document("Second", DocumentFixture.Compressed(), PeFixture.Lowering.FieldToken)),
            "document.json");

        Assert.False(result.Succeeded);
        Assert.True(result.HasError("HRDOA" + WriteOpenApiDocument.MoreThanOneDocumentCode), result.ErrorText);
        Assert.Contains("Fixture.First", result.ErrorText);
        Assert.Contains("Fixture.Second", result.ErrorText);
        Assert.Contains("one document per project", result.ErrorText);
    }

    [Fact]
    public void TheDocumentFixtureExportsThroughBothLoweringsIdentically() {
        var address = Fixture("address", new PeFixture.Document("Application", DocumentFixture.Compressed(), PeFixture.Lowering.FieldAddress));
        var token = Fixture("token", new PeFixture.Document("Application", DocumentFixture.Compressed(), PeFixture.Lowering.FieldToken));

        Assert.True(_harness.Run(address, "address.yaml").Succeeded);
        Assert.True(_harness.Run(token, "token.yaml").Succeeded);

        Assert.Equal(File.ReadAllText(_harness.Under("address.yaml")), File.ReadAllText(_harness.Under("token.yaml")));
        Assert.Equal(Canonical(Parse(File.ReadAllText(_harness.Under("address.yaml")), "yaml")), Canonical(Parse(JsonTreeWriter.WriteIndented(JsonTree.Parse(DocumentFixture.Compact)), "json")));
    }

    [Fact]
    public void ARelativeOutputIsResolvedAgainstTheProjectDirectory() {
        var result = _harness.Run(TaskHarness.Fixture(TaskHarness.OpenApiApp), "nested/deeper/document.yml");

        Assert.True(result.Succeeded, result.ErrorText);
        Assert.Equal(Path.GetFullPath(_harness.Under("nested/deeper/document.yml")), result.WrittenPath);
        Assert.True(File.Exists(result.WrittenPath));
    }

    private static Microsoft.OpenApi.OpenApiDocument Parse(string text, string format) {
        var settings = new OpenApiReaderSettings();
        settings.AddYamlReader();

        var read = Microsoft.OpenApi.OpenApiDocument.Parse(text, format, settings);

        Assert.NotNull(read.Document);
        Assert.Empty(read.Diagnostic!.Errors);

        return read.Document!;
    }

    private static string Canonical(Microsoft.OpenApi.OpenApiDocument document) {
        using var text = new StringWriter();
        var writer = new OpenApiJsonWriter(text);

        document.SerializeAsV32(writer);

        return text.ToString();
    }
}
