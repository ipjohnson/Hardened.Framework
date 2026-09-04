using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Hardened.Requests.Abstract.Attributes;
using Hardened.Requests.Runtime.Validation;
using Hardened.Shared.Runtime.Attributes;
using Hardened.SourceGeneration.Testing;
using Hardened.Validation.SourceGenerator;
using Hardened.Web.Runtime.Attributes;
using Hardened.Web.Runtime.OpenApi;
using Microsoft.CodeAnalysis;
using ValidationModules;
using Xunit;

namespace Hardened.Web.SourceGenerator.Tests;

/// <summary>
/// The document half of a constraint on a hand-written handler's parameter: the same attribute
/// the parameters validator enforces is published as the facet it declares.
/// </summary>
/// <remarks>
/// In this project rather than beside the other document tests because the constraint vocabulary
/// has to resolve, and this is the test project that references it.
/// </remarks>
public class ParameterConstraintDocumentTests {

    private static readonly Type[] Anchors = [
        typeof(GetAttribute),                    // Hardened.Web.Runtime
        typeof(FromBodyAttribute),               // Hardened.Requests.Abstract
        typeof(ValidationFilterProvider<object>),// Hardened.Requests.Runtime
        typeof(IValidatorFor<object>),           // ValidationModules.Runtime
        typeof(EnableAttribute<>),               // Hardened.Shared.Runtime
        typeof(OpenApiDocumentPublishing)        // the marker
    ];

    private const string Source = """
        using Hardened.Shared.Runtime.Attributes;
        using Hardened.Web.Runtime.Attributes;
        using Hardened.Web.Runtime.OpenApi;
        using ValidationModules.Constraints;

        namespace TestApp;

        [HardenedModule]
        [Enable<OpenApiDocumentPublishing>]
        public partial class Application { }

        public class RateController {
            [Get("/rates/{count:int}")]
            public string Read(
                [Range(Min = 1, Max = 100)] int count,
                [FromQueryString] [Range(Min = 2, Max = 8)] int precision,
                [FromHeader("X-Region")] [StringLength(2, 2)] string region,
                [FromQueryString] [Required] [Pattern("^[a-z]+$")] string? tag) => region;
        }
        """;

    private static JsonElement Document() {
        var result = GeneratorTestHarness.Run(
            new Dictionary<string, string> { ["Test.cs"] = Source },
            new IIncrementalGenerator[] {
                new WebLibrarySourceGenerator(), new HardenedValidationGenerator()
            },
            Anchors).AssertNoErrors();

        var match = Regex.Match(
            result.SourceContaining("OpenApiDocument"),
            @"new byte\[\]\s*\{(.*?)\}\s*;", RegexOptions.Singleline);

        Assert.True(match.Success, "No document byte array in the generated source.");

        var bytes = match.Groups[1].Value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(byte.Parse)
            .ToArray();

        using var source = new MemoryStream(bytes, writable: false);
        using var gzip = new GZipStream(source, CompressionMode.Decompress);
        using var inflated = new MemoryStream();

        gzip.CopyTo(inflated);

        return JsonDocument.Parse(inflated.ToArray()).RootElement.Clone();
    }

    private static JsonElement Parameter(JsonElement document, string name) {
        var operation = document.GetProperty("paths").GetProperty("/rates/{count}").GetProperty("get");

        foreach (var parameter in operation.GetProperty("parameters").EnumerateArray()) {
            if (parameter.GetProperty("name").GetString() == name) {
                return parameter;
            }
        }

        throw new Xunit.Sdk.XunitException(
            $"No parameter named '{name}'. The operation was: {operation.GetRawText()}");
    }

    [Fact]
    public void ABoundOnAPathTokenIsPublishedAsMinimumAndMaximum() {
        var schema = Parameter(Document(), "count").GetProperty("schema");

        Assert.Equal("integer", schema.GetProperty("type").GetString());
        Assert.Equal(1, schema.GetProperty("minimum").GetInt32());
        Assert.Equal(100, schema.GetProperty("maximum").GetInt32());
    }

    [Fact]
    public void ABoundOnAQueryValueIsPublishedAsMinimumAndMaximum() {
        var schema = Parameter(Document(), "precision").GetProperty("schema");

        Assert.Equal(2, schema.GetProperty("minimum").GetInt32());
        Assert.Equal(8, schema.GetProperty("maximum").GetInt32());
    }

    [Fact]
    public void ALengthOnAHeaderIsPublishedAsMinLengthAndMaxLength() {
        var schema = Parameter(Document(), "X-Region").GetProperty("schema");

        Assert.Equal("string", schema.GetProperty("type").GetString());
        Assert.Equal(2, schema.GetProperty("minLength").GetInt32());
        Assert.Equal(2, schema.GetProperty("maxLength").GetInt32());
    }

    /// <summary>
    /// A nullable parameter is optional by its type, and <c>[Required]</c> is what says the caller
    /// must send it anyway - so the document says so too, beside the pattern.
    /// </summary>
    [Fact]
    public void ARequiredNullableQueryValueIsPublishedAsRequiredWithItsPattern() {
        var parameter = Parameter(Document(), "tag");

        Assert.True(parameter.GetProperty("required").GetBoolean());
        Assert.Equal("^[a-z]+$", parameter.GetProperty("schema").GetProperty("pattern").GetString());
    }
}
