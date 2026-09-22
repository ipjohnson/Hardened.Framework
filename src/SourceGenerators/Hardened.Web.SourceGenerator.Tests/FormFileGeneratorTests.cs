using System.Text.Json;
using System.Text.RegularExpressions;
using Hardened.Requests.Abstract.Attributes;
using Hardened.SourceGeneration.Testing;
using Hardened.SourceGenerator.Requests;
using Hardened.Web.Runtime.Attributes;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Hardened.Web.SourceGenerator.Tests;

/// <summary>
/// What the generator does with an <c>IFormFile</c> parameter or model member.
/// </summary>
public class FormFileGeneratorTests
{
    private static readonly Type[] Anchors = [typeof(GetAttribute), typeof(FromBodyAttribute)];

    private static GeneratorResult Generate(string handlers, string types = "") =>
        GeneratorTestHarness.Run(
            new Dictionary<string, string>
            {
                ["Test.cs"] = $$"""
                using System.Collections.Generic;
                using Hardened.Requests.Abstract.Attributes;
                using Hardened.Requests.Abstract.Forms;
                using Hardened.Shared.Runtime.Attributes;
                using Hardened.Web.Runtime.Attributes;

                namespace TestApp;

                [HardenedModule]
                {{GeneratedOpenApiDocument.EnableAttribute}}
                public partial class TestApplication { }

                {{types}}

                public class UploadController {
                {{handlers}}
                }
                """,
            },
            new IIncrementalGenerator[] { new WebLibrarySourceGenerator() },
            Anchors
        );

    private static string Compact(string source) => Regex.Replace(source, @"\s+", "");

    private const string Binding = "global::Hardened.Requests.Runtime.Forms.FormFileBinding";

    /// <summary>
    /// A required file is checked the way a required field is, and an optional one is whatever
    /// the form carried.
    /// </summary>
    [Fact]
    public void AFileBindsFromItsPart()
    {
        var result = Generate(
            """
                [Post("/upload")]
                public long Upload([FromForm] IFormFile file, [FromForm] IFormFile? thumbnail) =>
                    file.Length;
            """
        );

        result.AssertNoErrors();

        var source = Compact(result.SourceContaining("Upload_"));

        Assert.Equal(1, source.Split("FormReader.ReadForm").Length - 1);
        Assert.Contains(
            $"parameters.file={Binding}.Required(form.GetFile(\"file\"),\"file\");",
            source
        );
        Assert.Contains("parameters.thumbnail=form.GetFile(\"thumbnail\");", source);
    }

    /// <summary>Every file sent under one name, copied only where the declared type needs it.</summary>
    [Fact]
    public void SeveralFilesBindFromEveryPartOfThatName()
    {
        var result = Generate(
            """
                [Post("/upload")]
                public int Upload(
                    [FromForm] IReadOnlyList<IFormFile> photos,
                    [FromForm] IFormFile[]? scans,
                    [FromForm] List<IFormFile> documents) => photos.Count;
            """
        );

        result.AssertNoErrors();

        var source = Compact(result.SourceContaining("Upload_"));

        Assert.Contains(
            $"parameters.photos={Binding}.RequiredMany(form.GetFiles(\"photos\"),\"photos\");",
            source
        );
        Assert.Contains(
            $"parameters.scans={Binding}.OptionalMany(form.GetFiles(\"scans\"))?.ToArray();",
            source
        );
        Assert.Contains(
            $"{Binding}.RequiredMany(form.GetFiles(\"documents\"),\"documents\").ToList();",
            source
        );
    }

    /// <summary>A form model's file member binds from its part, beside the fields.</summary>
    [Fact]
    public void AModelsFileMemberBindsFromItsPart()
    {
        var result = Generate(
            """
                [Post("/upload")]
                public long Upload([FromForm] Upload upload) => upload.File.Length;
            """,
            "public record Upload(string Tenant, IFormFile File, IReadOnlyList<IFormFile>? Extras = null);"
        );

        result.AssertNoErrors();

        var source = Compact(result.SourceContaining("Upload_"));

        Assert.Contains("ParseRequired<string>(form.Get(\"tenant\")!,\"tenant\")", source);
        Assert.Contains($"{Binding}.Required(form.GetFile(\"file\"),\"file\")", source);
        Assert.Contains($"{Binding}.OptionalMany(form.GetFiles(\"extras\"))", source);
    }

    /// <summary>A query string carries no files, so a query string model cannot have one.</summary>
    [Fact]
    public void AQueryStringModelWithAFileIsABuildError()
    {
        var result = Generate(
            """
                [Get("/upload")]
                public string Upload([FromQueryString] Upload upload) => upload.Tenant;
            """,
            "public record Upload(string Tenant, IFormFile File);"
        );

        var reported = Assert.Single(
            result.GeneratorDiagnostics,
            diagnostic => diagnostic.Id == BoundModelDiagnostics.DiagnosticId
        );

        Assert.Contains("its member 'File' is a file", reported.GetMessage());
    }

    /// <summary>A file bound from anywhere but the form is a build error, naming where.</summary>
    [Theory]
    [InlineData("IFormFile file", "container")]
    [InlineData("[FromBody] IFormFile file", "request body")]
    [InlineData("[FromQueryString] IFormFile file", "query string")]
    [InlineData("[FromHeader(\"X-File\")] IFormFile file", "headers")]
    [InlineData("[FromCookie] IReadOnlyList<IFormFile> file", "cookies")]
    public void AFileBoundFromAnywhereButTheFormIsABuildError(string parameter, string source)
    {
        var result = Generate(
            $$"""
                [Post("/upload")]
                public string Upload({{parameter}}) => "";
            """
        );

        var reported = Assert.Single(
            result.GeneratorDiagnostics,
            diagnostic => diagnostic.Id == FormFileDiagnostics.DiagnosticId
        );

        Assert.Equal(DiagnosticSeverity.Error, reported.Severity);
        Assert.Contains("'file', a file, from the " + source, reported.GetMessage());
    }

    /// <summary>A file bound with <c>[FromForm]</c> is not reported.</summary>
    [Fact]
    public void AFileBoundFromTheFormIsNotReported()
    {
        var result = Generate(
            """
                [Post("/upload")]
                public long Upload([FromForm] IFormFile file) => file.Length;
            """
        );

        Assert.DoesNotContain(
            result.GeneratorDiagnostics,
            diagnostic => diagnostic.Id == FormFileDiagnostics.DiagnosticId
        );
    }

    private static JsonElement RequestBody(GeneratorResult result, string path)
    {
        var source = result
            .AssertNoErrors()
            .GeneratedSources.First(pair => pair.Key.Contains("OpenApiDocument"))
            .Value;

        return JsonDocument
            .Parse(GeneratedOpenApiDocument.Extract(source))
            .RootElement.GetProperty("paths")
            .GetProperty(path)
            .GetProperty("post")
            .GetProperty("requestBody")
            .GetProperty("content");
    }

    /// <summary>
    /// A form with a file in it is published as <c>multipart/form-data</c>, and the file as
    /// <c>format: binary</c>.
    /// </summary>
    [Fact]
    public void AFormWithAFileIsPublishedAsMultipart()
    {
        var content = RequestBody(
            Generate(
                """
                    [Post("/upload")]
                    public long Upload(
                        [FromForm] string tenant,
                        [FromForm] IFormFile file,
                        [FromForm] IReadOnlyList<IFormFile>? extras) => file.Length;
                """
            ),
            "/upload"
        );

        var media = Assert.Single(content.EnumerateObject());

        Assert.Equal("multipart/form-data", media.Name);

        var schema = media.Value.GetProperty("schema");
        var properties = schema.GetProperty("properties");

        Assert.Equal("binary", properties.GetProperty("file").GetProperty("format").GetString());
        Assert.Equal(
            "binary",
            properties.GetProperty("extras").GetProperty("items").GetProperty("format").GetString()
        );
        Assert.Equal(
            ["tenant", "file"],
            schema.GetProperty("required").EnumerateArray().Select(name => name.GetString())
        );
    }

    /// <summary>
    /// A model with a file member is published as multipart too, and its schema says the member is
    /// bytes rather than the interface's properties.
    /// </summary>
    [Fact]
    public void AModelWithAFileIsPublishedAsMultipart()
    {
        var result = Generate(
            """
                [Post("/upload")]
                public long Upload([FromForm] Upload upload) => upload.File.Length;
            """,
            "public record Upload(string Tenant, IFormFile File);"
        );

        var content = RequestBody(result, "/upload");

        Assert.True(content.TryGetProperty("multipart/form-data", out _));

        var document = GeneratedOpenApiDocument.Extract(
            result.GeneratedSources.First(pair => pair.Key.Contains("OpenApiDocument")).Value
        );
        var upload = JsonDocument
            .Parse(document)
            .RootElement.GetProperty("components")
            .GetProperty("schemas")
            .GetProperty("Upload");

        Assert.Equal(
            "binary",
            upload.GetProperty("properties").GetProperty("file").GetProperty("format").GetString()
        );
        Assert.DoesNotContain("IFormFile", document);
    }
}
