using System.Text.Json;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.Serializer;

namespace Hardened.IntegrationTests.OpenApi.SUT.Tests;

/// <summary>
/// A streamed operation declared in a contract with <c>itemSchema</c>, end to end: the wire, the
/// refusal before the first event, and the document the application serves for it.
/// </summary>
/// <remarks>
/// <para>
/// Before this the interface said <c>IAsyncEnumerable&lt;PetEvent&gt;</c> and nothing behind it
/// agreed: the generated handler awaited the enumerable and did not compile, and the served
/// document described the 200 with no content. The contract is <c>events.yaml</c>, a second
/// contract in the same project so it can carry the 3.2 banner <c>itemSchema</c> needs.
/// </para>
/// </remarks>
public class PetEventsTests {

    /// <summary>
    /// Each event is <c>data:</c> and a blank line under <c>text/event-stream</c>, exactly as a
    /// code-first <c>[ServerSentEvents]</c> handler answers.
    /// </summary>
    [HardenedTest]
    public async Task TheStreamIsFramedAsServerSentEvents(ITestWebApp app) {
        var response = await app.Get("/pets/1/events");

        response.Assert.Ok();

        Assert.Equal(
            KnownContentType.EventStream, response.Headers[KnownHeaders.ContentType].ToString());

        Assert.Equal(
            """
            data: {"petId":"1","kind":"adopted"}

            data: {"petId":"1","kind":"weighed"}


            """.ReplaceLineEndings("\n"),
            await BodyOf(response));
    }

    /// <summary>
    /// A refusal thrown before the first event is a 404 with its JSON body, not a stream that
    /// starts and breaks. The contract does not declare it; <c>events.yaml</c> says why.
    /// </summary>
    [HardenedTest]
    public async Task ARefusalBeforeTheFirstEventIsANotFound(ITestWebApp app) {
        var response = await app.Get("/pets/missing/events");

        Assert.Equal(404, response.StatusCode);
        Assert.StartsWith("application/json", response.Headers[KnownHeaders.ContentType].ToString());
        Assert.Contains("No pet has id missing.", await BodyOf(response));
    }

    /// <summary>
    /// The served document describes the stream the way the code-first writer does: the item under
    /// <c>itemSchema</c>, the complete content as an array of it under <c>schema</c>, the
    /// contract's own description.
    /// </summary>
    [HardenedTest]
    public async Task TheDocumentDescribesTheStream(ITestWebApp app) {
        var response = await app.Get("/openapi.json");

        response.Assert.Ok();

        using var document = JsonDocument.Parse(await response.ReadTextAsync());

        var responses = document.RootElement.GetProperty("paths").GetProperty("/pets/{petId}/events")
            .GetProperty("get").GetProperty("responses");

        var ok = responses.GetProperty("200");
        var media = ok.GetProperty("content").GetProperty("text/event-stream");
        var item = media.GetProperty("itemSchema");
        var whole = media.GetProperty("schema");

        Assert.Equal("The pet's history, one event at a time.", ok.GetProperty("description").GetString());
        Assert.Equal("#/components/schemas/PetEvent", item.GetProperty("$ref").GetString());
        Assert.Equal("array", whole.GetProperty("type").GetString());
        Assert.Equal(item.GetRawText(), whole.GetProperty("items").GetRawText());

        var components = document.RootElement.GetProperty("components").GetProperty("schemas");

        Assert.True(components.TryGetProperty("PetEvent", out _), "the item's schema survives the slicer");
    }

    private static async Task<string> BodyOf(TestWebResponse response) {
        response.Body.Position = 0;

        using var reader = new StreamReader(response.Body, leaveOpen: true);

        return await reader.ReadToEndAsync();
    }
}
