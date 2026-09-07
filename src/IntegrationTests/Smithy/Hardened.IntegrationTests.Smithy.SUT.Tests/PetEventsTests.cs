using System.Text.Json;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.Serializer;
using Hardened.Web.Runtime.Responses;

namespace Hardened.IntegrationTests.Smithy.SUT.Tests;

/// <summary>
/// An event stream declared with Smithy's own spelling, a union under <c>@streaming</c>, end to
/// end: the wire, the refusal before the first event, and the document.
/// </summary>
/// <remarks>
/// The union is the item. The generated <c>PetEventStream</c> implements <c>ISseEvent</c>, so the
/// framing writes the member's name as the <c>event:</c> field and the member's own JSON as
/// <c>data:</c>, which is what a browser's <c>EventSource</c> dispatches on.
/// </remarks>
public class PetEventsTests {

    [HardenedTest]
    public async Task EachMemberIsAnEventNamedForIt(ITestWebApp app) {
        var response = await app.Get("/pets/1/events");

        response.Assert.Ok();

        Assert.Equal(
            KnownContentType.EventStream, response.Headers[KnownHeaders.ContentType].ToString());

        Assert.Equal(
            """
            event: adopted
            data: {"petId":"1","by":"pia"}

            event: weighed
            data: {"petId":"1","grams":4200}


            """.ReplaceLineEndings("\n"),
            await BodyOf(response));
    }

    [HardenedTest]
    public async Task ARefusalBeforeTheFirstEventIsANotFound(ITestWebApp app) {
        var response = await app.Get("/pets/missing/events");

        Assert.Equal(404, response.StatusCode);
        Assert.StartsWith("application/json", response.Headers[KnownHeaders.ContentType].ToString());
        Assert.Contains("No pet has id missing.", await BodyOf(response));
    }

    /// <summary>
    /// The document says what the wire does: the item is the union, published as the choice of
    /// its members, under <c>itemSchema</c> and as an array under <c>schema</c>.
    /// </summary>
    [HardenedTest]
    public async Task TheDocumentDescribesTheStreamAsTheChoiceOfItsMembers(ITestWebApp app) {
        var response = await app.Get("/openapi.json");

        response.Assert.Ok();

        using var document = JsonDocument.Parse(await response.ReadTextAsync());

        var media = document.RootElement.GetProperty("paths").GetProperty("/pets/{petId}/events")
            .GetProperty("get").GetProperty("responses").GetProperty("200")
            .GetProperty("content").GetProperty("text/event-stream");

        var item = media.GetProperty("itemSchema");
        var whole = media.GetProperty("schema");

        Assert.Equal("#/components/schemas/PetEventStream", item.GetProperty("$ref").GetString());
        Assert.Equal("array", whole.GetProperty("type").GetString());
        Assert.Equal(item.GetRawText(), whole.GetProperty("items").GetRawText());

        var union = document.RootElement.GetProperty("components").GetProperty("schemas")
            .GetProperty("PetEventStream").GetProperty("oneOf");

        Assert.Equal(
            ["#/components/schemas/PetAdopted", "#/components/schemas/PetWeighed"],
            union.EnumerateArray().Select(branch => branch.GetProperty("$ref").GetString()));
    }

    private static async Task<string> BodyOf(TestWebResponse response) {
        response.Body.Position = 0;

        using var reader = new StreamReader(response.Body, leaveOpen: true);

        return await reader.ReadToEndAsync();
    }
}
