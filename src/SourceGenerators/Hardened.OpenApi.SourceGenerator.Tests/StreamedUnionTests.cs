using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Hardened.SourceGeneration.Testing;
using Xunit;

namespace Hardened.OpenApi.SourceGenerator.Tests;

/// <summary>
/// A stream whose item is a choice of schemas, which is what a Smithy <c>@streaming</c> union
/// arrives as and what an OpenAPI <c>oneOf</c> under <c>itemSchema</c> spells directly.
/// </summary>
/// <remarks>
/// The union is emitted as an event as well as a union: it implements <c>ISseEvent</c> so the
/// framing writes the member it holds as <c>data:</c> and, where the description named the
/// member, that name as <c>event:</c>. An OpenAPI <c>oneOf</c> names nothing, so the event field
/// stays empty here; the Smithy fixture in the integration suite carries the named case.
/// </remarks>
public class StreamedUnionTests {

    private const string Spec =
        """
        openapi: "3.2.0"
        info: { title: Pets, version: "1.0" }
        paths:
          /pets/{petId}/events:
            get:
              tags: [Pet]
              operationId: petEvents
              parameters:
                - name: petId
                  in: path
                  required: true
                  schema: { type: string }
              responses:
                '200':
                  description: The pet's history, one event at a time.
                  content:
                    text/event-stream:
                      itemSchema:
                        $ref: '#/components/schemas/PetEvent'
        components:
          schemas:
            PetEvent:
              oneOf:
                - $ref: '#/components/schemas/PetAdopted'
                - $ref: '#/components/schemas/PetWeighed'
            PetAdopted:
              type: object
              required: [by]
              properties:
                by: { type: string }
            PetWeighed:
              type: object
              required: [grams]
              properties:
                grams: { type: integer }
        """;

    private const string Implementation =
        """
        [Handler]
        public class PetServiceImpl : IPetService {
            public async IAsyncEnumerable<PetEvent> PetEvents(string petId) {
                await Task.Yield();

                yield return new PetAdopted("pia");
                yield return new PetWeighed(4200);
            }
        }
        """;

    [Fact]
    public void TheStreamedUnionIsAnEvent() {
        var generated = OpenApiGenerator.Run(Spec).AssertNoErrors().SourceContaining("petstore.g.cs");

        Assert.Contains("partial struct PetEvent : global::Hardened.Requests.Abstract.Serializer.ISseEvent", generated);
        Assert.Contains("ISseEvent.Data => Value;", generated);
        Assert.Contains("ISseEvent.Event => Value switch { _ => null };", generated);
    }

    [Fact]
    public void AStreamingImplementationCompiles() {
        OpenApiGenerator.Run(Spec, OpenApiGenerator.EntryPointWithHandler(Implementation)).AssertNoErrors();
    }

    /// <summary>
    /// The union is published as the choice it is. Before, a union crossed to the document writer
    /// as a bare object, because its branches never crossed the intermediate file.
    /// </summary>
    [Fact]
    public void TheDocumentPublishesTheUnionAsAChoice() {
        var result = OpenApiGenerator.Run(Spec, OpenApiGenerator.EntryPointWithHandler(Implementation))
            .AssertNoErrors();

        using var document = JsonDocument.Parse(ServedDocument(result));

        var union = document.RootElement.GetProperty("components").GetProperty("schemas")
            .GetProperty("PetEvent").GetProperty("oneOf");

        Assert.Equal(
            ["#/components/schemas/PetAdopted", "#/components/schemas/PetWeighed"],
            union.EnumerateArray().Select(branch => branch.GetProperty("$ref").GetString()));
    }

    private static string ServedDocument(GeneratorResult result) {
        var source = result.SourceContaining("OpenApiDocument");

        var match = Regex.Match(source, @"new byte\[\]\s*\{(.*?)\}\s*;", RegexOptions.Singleline);

        Assert.True(match.Success, "No document byte array in the generated source.");

        var bytes = match.Groups[1].Value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(byte.Parse)
            .ToArray();

        using var compressed = new MemoryStream(bytes, writable: false);
        using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
        using var inflated = new MemoryStream();

        gzip.CopyTo(inflated);

        return Encoding.UTF8.GetString(inflated.ToArray());
    }
}
