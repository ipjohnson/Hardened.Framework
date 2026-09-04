using System.Text.Json;
using Hardened.Generation.Document;
using Xunit;

namespace Hardened.OpenApiDocument.BuildTask.Tests;

/// <summary>
/// The JSON tree, its two emitters and the version lowering, over <see cref="DocumentFixture"/>.
/// </summary>
public class DocumentFormatTests {

    private static JsonObject Parsed() => (JsonObject)JsonTree.Parse(DocumentFixture.Compact);

    [Fact]
    public void TheParserKeepsWhatTheGeneratorWrites() {
        var document = Parsed();

        var info = (JsonObject)document.Get("info")!;

        Assert.Equal("Fixture: yes", ((JsonString)info.Get("title")!).Value);
        Assert.Equal("Line one\nLine two \"quoted\" \\ back", ((JsonString)info.Get("description")!).Value);

        var paths = (JsonObject)document.Get("paths")!;

        Assert.Equal(new[] { "/things/{id}", "/events" }, paths.Members.Select(member => member.Key));

        var count = (JsonObject)((JsonObject)((JsonObject)((JsonObject)document.Get("components")!)
            .Get("schemas")!).Get("Thing")!).Get("properties")!;

        Assert.Equal("100.5", ((JsonNumber)((JsonObject)count.Get("count")!).Get("exclusiveMaximum")!).Text);
        Assert.Same(JsonNull.Instance, ((JsonObject)count.Get("count")!).Get("default"));
        Assert.Empty(((JsonObject)((JsonObject)count.Get("empty")!).Get("properties")!).Members);
        Assert.Empty(((JsonArray)((JsonObject)document.Get("components")!).Get("x-empty-list")!).Items);
    }

    /// <summary>
    /// Indented, then parsed again, then indented again: the same bytes. That is the property a
    /// tracked file needs, and it also proves the emitter writes what the parser reads.
    /// </summary>
    [Fact]
    public void IndentedJsonRoundTripsByteForByte() {
        var first = JsonTreeWriter.WriteIndented(Parsed());
        var second = JsonTreeWriter.WriteIndented(JsonTree.Parse(first));

        Assert.Equal(first, second);
        Assert.EndsWith("\n", first);
        Assert.StartsWith("{\n  \"openapi\": \"3.2.0\",\n  \"info\": {\n    \"title\": \"Fixture: yes\",", first);
    }

    /// <summary>
    /// And a reader that is not ours agrees the indented file says what the compact one said.
    /// </summary>
    [Fact]
    public void IndentedJsonIsTheSameDocumentToSystemTextJson() {
        using var compact = JsonDocument.Parse(DocumentFixture.Compact);
        using var indented = JsonDocument.Parse(JsonTreeWriter.WriteIndented(Parsed()));

        Assert.Equal(compact.RootElement.GetRawText().Length > 0, indented.RootElement.GetRawText().Length > 0);
        Assert.Equal(
            compact.RootElement.GetProperty("info").GetProperty("description").GetString(),
            indented.RootElement.GetProperty("info").GetProperty("description").GetString());
        Assert.Equal(
            compact.RootElement.GetProperty("paths").GetProperty("/things/{id}").GetProperty("get")
                .GetProperty("parameters")[0].GetProperty("schema").GetProperty("enum").GetArrayLength(),
            indented.RootElement.GetProperty("paths").GetProperty("/things/{id}").GetProperty("get")
                .GetProperty("parameters")[0].GetProperty("schema").GetProperty("enum").GetArrayLength());
        Assert.Equal("caf\u00e9 \u2615 \u00fcn\u00efcode",
            indented.RootElement.GetProperty("paths").GetProperty("/things/{id}").GetProperty("get")
                .GetProperty("responses").GetProperty("200").GetProperty("description").GetString());
    }

    [Fact]
    public void EmptyContainersAreWrittenInline() {
        var indented = JsonTreeWriter.WriteIndented(Parsed());

        Assert.Contains("\"properties\": {}", indented);
        Assert.Contains("\"items\": {}", indented);
        Assert.Contains("\"x-empty-list\": []", indented);
    }

    /// <summary>
    /// The scalars YAML would read as something else are quoted; the ones it reads as strings are
    /// not.
    /// </summary>
    [Fact]
    public void YamlQuotesEverythingOutsideTheSafePattern() {
        var yaml = YamlTreeWriter.Write(Parsed());

        Assert.Contains("\"/things/{id}\":\n", yaml);
        Assert.Contains("\"200\":\n", yaml);
        Assert.Contains("\"$ref\": \"#/components/schemas/Thing\"", yaml);
        Assert.Contains("title: \"Fixture: yes\"", yaml);
        Assert.Contains("description: \"Line one\\nLine two \\\"quoted\\\" \\\\ back\"", yaml);

        foreach (var quoted in new[] { "yes", "no", "null", "1e3", "007", "on", "true", "-1", "0x1F", ".inf", "caf\u00e9", "a b", "" }) {
            Assert.Contains("- \"" + quoted + "\"\n", yaml);
        }

        Assert.Contains("- plain-ok\n", yaml);
        Assert.Contains("- x/y.z\n", yaml);
        Assert.Contains("type: string\n", yaml);
        Assert.Contains("application/json:\n", yaml);
        Assert.Contains("required: true\n", yaml);
        Assert.Contains("default: null\n", yaml);
        Assert.Contains("exclusiveMaximum: 100.5\n", yaml);
        Assert.Contains("properties: {}\n", yaml);
        Assert.Contains("x-empty-list: []\n", yaml);
    }

    [Theory]
    [InlineData("string", true)]
    [InlineData("application/json", true)]
    [InlineData("x_y.z/w-v", true)]
    [InlineData("_private", true)]
    [InlineData("N0", true)]
    [InlineData("", false)]
    [InlineData("-dash", false)]
    [InlineData(".dot", false)]
    [InlineData("Yes", false)]
    [InlineData("OFF", false)]
    [InlineData("~", false)]
    [InlineData("12", false)]
    [InlineData("1.5", false)]
    [InlineData("1e3", false)]
    [InlineData("0o17", false)]
    [InlineData("a b", false)]
    [InlineData("a:b", false)]
    [InlineData("#tag", false)]
    [InlineData("caf\u00e9", false)]
    public void ThePlainScalarRuleIsStrict(string value, bool plain) {
        Assert.Equal(plain, YamlTreeWriter.IsPlainSafe(value));
    }

    [Fact]
    public void YamlLaysOutBlocksTheWayOpenApiIsUsuallyWritten() {
        var yaml = YamlTreeWriter.Write(Parsed());

        Assert.Contains(
            "      parameters:\n        - name: id\n          in: path\n          required: true\n          schema:\n            type: string\n",
            yaml);
        Assert.Contains("      tags:\n        - Things\n", yaml);
    }

    [Theory]
    [InlineData("3.0", "3.0.0")]
    [InlineData("3.0.0", "3.0.0")]
    [InlineData("3.1", "3.1.0")]
    [InlineData(" 3.1.0 ", "3.1.0")]
    [InlineData("3.2.0", null)]
    [InlineData("banana", null)]
    [InlineData("", null)]
    public void OnlyTheTwoLowerVersionsAreAccepted(string value, string? expected) {
        Assert.Equal(expected, OpenApiDocumentLowering.Normalise(value));
    }

    [Fact]
    public void LoweringToThreeOneDropsItemSchemaAndNamesTheOperation() {
        var document = Parsed();

        var lost = OpenApiDocumentLowering.Lower(document, "3.1.0");

        Assert.Equal(new[] { "GET /events" }, lost);
        Assert.Equal("3.1.0", ((JsonString)document.Get("openapi")!).Value);

        var written = JsonTreeWriter.WriteIndented(document);

        Assert.DoesNotContain("itemSchema", written);

        // 3.1 keeps the 2020-12 spellings.
        Assert.Contains("\"exclusiveMinimum\": 0", written);
        Assert.Contains("\"type\": [\n", written);
    }

    [Fact]
    public void LoweringToThreeZeroRewritesTheBoundsAndTheNullableType() {
        var document = Parsed();

        OpenApiDocumentLowering.Lower(document, "3.0.0");

        var count = (JsonObject)((JsonObject)((JsonObject)((JsonObject)((JsonObject)document.Get("components")!)
            .Get("schemas")!).Get("Thing")!).Get("properties")!).Get("count")!;

        // The bound and its flag, in that order, where the numeric exclusive bound was.
        Assert.Equal(
            new[] { "type", "nullable", "minimum", "exclusiveMinimum", "maximum", "exclusiveMaximum", "default" },
            count.Members.Select(member => member.Key));
        Assert.Equal("integer", ((JsonString)count.Get("type")!).Value);
        Assert.Same(JsonBoolean.True, count.Get("nullable"));
        Assert.Equal("0", ((JsonNumber)count.Get("minimum")!).Text);
        Assert.Same(JsonBoolean.True, count.Get("exclusiveMinimum"));
        Assert.Equal("100.5", ((JsonNumber)count.Get("maximum")!).Text);
        Assert.Same(JsonBoolean.True, count.Get("exclusiveMaximum"));

        var written = JsonTreeWriter.WriteIndented(document);

        Assert.DoesNotContain("itemSchema", written);
        Assert.DoesNotContain("\"type\": [", written);
        Assert.Equal("3.0.0", ((JsonString)document.Get("openapi")!).Value);
    }

    [Fact]
    public void ADocumentWithNoStreamingLosesNothing() {
        var document = (JsonObject)JsonTree.Parse("{\"openapi\":\"3.2.0\",\"paths\":{\"/a\":{\"get\":{\"responses\":{\"200\":{\"description\":\"ok\"}}}}}}");

        Assert.Empty(OpenApiDocumentLowering.Lower(document, "3.1.0"));
    }

    [Theory]
    [InlineData("{\"a\":1,}")]
    [InlineData("{a:1}")]
    [InlineData("[1,2")]
    [InlineData("{\"a\":\"\\x\"}")]
    [InlineData("{\"a\":01}")]
    [InlineData("{} {}")]
    public void MalformedJsonIsRefusedWithAnOffset(string text) {
        var failure = Assert.Throws<FormatException>(() => JsonTree.Parse(text));

        Assert.Contains("offset", failure.Message);
    }
}
