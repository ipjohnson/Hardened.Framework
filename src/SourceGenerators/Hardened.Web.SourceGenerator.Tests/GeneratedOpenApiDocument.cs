using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace Hardened.Web.SourceGenerator.Tests;

/// <summary>
/// Reads the document back out of what the generator emitted.
/// </summary>
/// <remarks>
/// The document is emitted gzipped into a <c>ReadOnlySpan&lt;byte&gt;</c> rather than as a string
/// literal, because a C# string literal is stored as UTF-16 and costs two bytes per ASCII character.
/// Every test that asserts on the document has to undo that first, so it is undone in one place -
/// and the same reading is done by <c>scripts/extract-openapi.py</c> for the Spectral step in CI.
/// </remarks>
internal static class GeneratedOpenApiDocument {

    /// <summary>
    /// What a fixture application has to carry for a document to be emitted at all.
    /// </summary>
    /// <remarks>
    /// Emission is opt-in, so a fixture without this produces no document and every assertion about
    /// one fails with "no generated source contains OpenApiDocument" rather than anything about the
    /// document itself.
    /// </remarks>
    public const string EnableAttribute =
        "[Hardened.Shared.Runtime.Attributes.Enable<" +
        "Hardened.Web.Runtime.OpenApi.HardenedOpenApiDocument>]";

    /// <summary>The JSON the generated source carries.</summary>
    public static string Extract(string generatedSource) {
        var match = Regex.Match(
            generatedSource, @"new byte\[\]\s*\{(.*?)\}\s*;", RegexOptions.Singleline);

        Assert.True(match.Success, "No document byte array in the generated source.");

        var bytes = match.Groups[1].Value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(byte.Parse)
            .ToArray();

        using var source = new MemoryStream(bytes, writable: false);
        using var gzip = new GZipStream(source, CompressionMode.Decompress);
        using var inflated = new MemoryStream();

        gzip.CopyTo(inflated);

        return Encoding.UTF8.GetString(inflated.ToArray());
    }
}
