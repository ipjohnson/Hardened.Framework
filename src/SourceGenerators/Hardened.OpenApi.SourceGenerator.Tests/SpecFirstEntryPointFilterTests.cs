using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Hardened.SourceGeneration.Testing;
using Xunit;

namespace Hardened.OpenApi.SourceGenerator.Tests;

/// <summary>
/// A filter declared on the module class of a specification-first application.
/// </summary>
/// <remarks>
/// The rung is the same rung. A described application has nowhere to put a route attribute, but its
/// module class is ordinary C# in the same compilation - and both front ends reach the pipeline
/// through <c>RoutingTableGenerator.GenerateCSharpRouteFile</c> and the document through
/// <c>OpenApiDocumentGenerator.Write</c>, so neither has a copy of this to drift from.
/// </remarks>
public class SpecFirstEntryPointFilterTests {

    private const string Spec =
        """
        openapi: "3.0.0"
        info: { title: Pets, version: "1.0" }
        paths:
          /pets:
            get:
              operationId: listPets
              responses:
                '200':
                  description: The pets
                  content:
                    application/json:
                      schema: { type: array, items: { $ref: '#/components/schemas/Pet' } }
            post:
              operationId: addPet
              requestBody:
                content:
                  application/json:
                    schema: { $ref: '#/components/schemas/Pet' }
              responses:
                '201':
                  description: Added
        components:
          schemas:
            Pet:
              type: object
              required: [id]
              properties:
                id: { type: string }
        """;

    private const string EntryPointWithRung =
        """
        using Hardened.Shared.Runtime.Attributes;
        using Hardened.Web.Runtime.Conditional;

        namespace TestNamespace;

        [HardenedModule]
        [ConditionalGet]
        public partial class TestApp {
        }
        """;

    private static GeneratorResult Generated() {
        var result = OpenApiGenerator.Run(Spec, EntryPointWithRung);

        Assert.Empty(result.Errors);

        return result;
    }

    private static JsonElement Document(GeneratorResult result) {
        var source = result.GeneratedSources
            .First(pair => pair.Key.Contains("OpenApiDocument")).Value;

        var match = Regex.Match(
            source, @"new byte\[\]\s*\{(.*?)\}\s*;", RegexOptions.Singleline);

        Assert.True(match.Success, "No document byte array in the generated source.");

        var bytes = match.Groups[1].Value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(byte.Parse)
            .ToArray();

        using var compressed = new MemoryStream(bytes, writable: false);
        using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
        using var inflated = new MemoryStream();

        gzip.CopyTo(inflated);

        return JsonDocument.Parse(Encoding.UTF8.GetString(inflated.ToArray())).RootElement.Clone();
    }

    private static string[] Statuses(JsonElement document, string method) =>
        document.GetProperty("paths").GetProperty("/pets").GetProperty(method)
            .GetProperty("responses").EnumerateObject()
            .Select(response => response.Name)
            .OrderBy(status => status, StringComparer.Ordinal)
            .ToArray();

    [Fact]
    public void TheDescribedReadPublishesWhatTheModuleDeclares() {
        var document = Document(Generated());

        Assert.Contains("304", Statuses(document, "get"));
        Assert.DoesNotContain("304", Statuses(document, "post"));
    }

    [Fact]
    public void TheDeclarationReachesTheDescribedRoutingTable() {
        var routing = Generated().SourceContaining("SpecRouting");

        Assert.Contains("ApplicationFilters", routing);
        Assert.Contains("IApplicationFilterDeclarations", routing);
    }
}
