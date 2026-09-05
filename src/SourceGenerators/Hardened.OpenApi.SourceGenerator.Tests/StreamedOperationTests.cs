using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Hardened.SourceGeneration.Testing;
using Xunit;

namespace Hardened.OpenApi.SourceGenerator.Tests;

/// <summary>
/// An operation whose success is a stream, declared with OpenAPI 3.2's <c>itemSchema</c>.
/// </summary>
/// <remarks>
/// <para>
/// The interface half of this worked from the start: <c>ServiceInterfaceEmitter</c> read
/// <c>ItemSchemaRef</c> and returned <c>IAsyncEnumerable&lt;T&gt;</c>. The handler half did not,
/// because the field never crossed the intermediate file the build task hands the generator, so
/// the bridge described the operation as an ordinary awaited body: the generated invoke method
/// awaited the enumerable (CS9353 in generated code), the handler sat on the buffered filter, and
/// the published document had a 200 with no content. Every test here drives the same
/// parse-emit-serialise-generate path a build does, which is what makes them able to see that.
/// </para>
/// </remarks>
public class StreamedOperationTests {

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
                '404':
                  description: No pet with that id.
                  content:
                    application/json:
                      schema:
                        $ref: '#/components/schemas/Problem'
          /pets/{petId}/readings:
            get:
              tags: [Pet]
              operationId: petReadings
              parameters:
                - name: petId
                  in: path
                  required: true
                  schema: { type: string }
              responses:
                '200':
                  description: One reading per line.
                  content:
                    application/x-ndjson:
                      itemSchema:
                        $ref: '#/components/schemas/PetEvent'
        components:
          schemas:
            PetEvent:
              type: object
              required: [petId, kind]
              properties:
                petId: { type: string }
                kind: { type: string }
            Problem:
              type: object
              properties:
                detail: { type: string }
        """;

    private const string Implementation =
        """
        [Handler]
        public class PetServiceImpl : IPetService {
            public async IAsyncEnumerable<PetEvent> PetEvents(string petId) {
                await Task.Yield();

                yield return new PetEvent(petId, "adopted");
            }

            public async IAsyncEnumerable<PetEvent> PetReadings(string petId) {
                await Task.Yield();

                yield return new PetEvent(petId, "weighed");
            }
        }
        """;

    [Fact]
    public void TheInterfaceReturnsAStream() {
        var generated = OpenApiGenerator.Run(Spec).AssertNoErrors().SourceContaining("petstore.g.cs");

        Assert.Contains("IAsyncEnumerable<global::TestNamespace.Models.PetEvent> PetEvents(string petId)", generated);
    }

    /// <summary>
    /// The handler hands the enumerable to the streaming filter rather than awaiting it.
    /// </summary>
    [Fact]
    public void TheHandlerStreamsWithTheFramingTheContractNames() {
        var result = OpenApiGenerator.Run(Spec).AssertNoErrors();

        var events = result.SourceContaining("PetController_PetEvents");
        var readings = result.SourceContaining("PetController_PetReadings");

        Assert.Contains("AsyncEnumerableFilterWithParameters", events);
        Assert.Contains("SseFraming.Instance", events);
        Assert.Contains("context.Response.ResponseValue = controller.PetEvents(", events);
        Assert.DoesNotContain("await controller.PetEvents(", events);

        // Newline-delimited JSON is the filter's own default, so the code-first emitter names no
        // framing for it and this path emits exactly the same call.
        Assert.Contains("AsyncEnumerableFilterWithParameters", readings);
        Assert.DoesNotContain("SseFraming", readings);
    }

    /// <summary>
    /// The handler info says the operation streams, which is what the conditional-GET stage reads
    /// to stand down rather than buffer an event stream.
    /// </summary>
    [Fact]
    public void TheHandlerInfoSaysItStreams() {
        var events = OpenApiGenerator.Run(Spec).AssertNoErrors().SourceContaining("PetController_PetEvents");

        Assert.Contains("streamsResponse: true", events);
    }

    /// <summary>
    /// An implementation returning <c>IAsyncEnumerable&lt;T&gt;</c> compiles against what was
    /// generated - which is the assertion that failed before, in the generated invoke method.
    /// </summary>
    [Fact]
    public void AStreamingImplementationCompiles() {
        OpenApiGenerator.Run(Spec, OpenApiGenerator.EntryPointWithHandler(Implementation)).AssertNoErrors();
    }

    /// <summary>
    /// The served document describes the stream the way the code-first writer does: the item under
    /// <c>itemSchema</c>, the complete content as an array of it under <c>schema</c>, with the
    /// contract's own description, beside the 404 the contract declares.
    /// </summary>
    [Fact]
    public void TheDocumentDescribesTheStream() {
        var result = OpenApiGenerator.Run(Spec, OpenApiGenerator.EntryPointWithHandler(Implementation))
            .AssertNoErrors();

        using var document = JsonDocument.Parse(ServedDocument(result));

        var events = document.RootElement.GetProperty("paths").GetProperty("/pets/{petId}/events")
            .GetProperty("get").GetProperty("responses");

        var ok = events.GetProperty("200");
        var media = ok.GetProperty("content").GetProperty("text/event-stream");
        var item = media.GetProperty("itemSchema");
        var whole = media.GetProperty("schema");

        Assert.Equal("The pet's history, one event at a time.", ok.GetProperty("description").GetString());
        Assert.Equal("#/components/schemas/PetEvent", item.GetProperty("$ref").GetString());
        Assert.Equal("array", whole.GetProperty("type").GetString());
        Assert.Equal(item.GetRawText(), whole.GetProperty("items").GetRawText());
        Assert.True(events.TryGetProperty("404", out _), "the declared 404 is written beside the stream");

        var readings = document.RootElement.GetProperty("paths").GetProperty("/pets/{petId}/readings")
            .GetProperty("get").GetProperty("responses").GetProperty("200").GetProperty("content");

        Assert.True(readings.TryGetProperty("application/x-ndjson", out _), "the framing is the contract's media type");
    }

    /// <summary>The document the generator embeds, inflated the way CI's extractor does it.</summary>
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
