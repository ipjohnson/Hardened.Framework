using System.Text.Json;
using Hardened.IntegrationTests.WebApp.SUT.Controllers;

namespace Hardened.IntegrationTests.WebApp.SUT.Tests;

/// <summary>
/// What a declaration says the operation writes and reads, in the document it generates.
/// </summary>
/// <remarks>
/// <para>
/// Three things the document could not say. A handler that writes a header itself had no way to
/// declare it, so a throws-mode 201 published no <c>Location</c> and a generated client had
/// nothing to read the new resource's address from. <c>[ConditionalGet]</c> published nothing at
/// all - no 304, no <c>ETag</c>, no <c>If-None-Match</c> - so no client generated from the
/// document could make the request the filter exists to answer. And the statuses a guard
/// synthesized were appended after the declared ones, so one <c>responses</c> object was sorted
/// in its first half and arbitrary in its second.
/// </para>
/// <para>
/// Over the served document rather than the exported file, because the served one is what a client
/// generator is pointed at. <c>ExportedDocumentTests</c> holds the two to each other.
/// </para>
/// </remarks>
public class DeclaredHeaderDocumentTests {

    private static async Task<JsonElement> Operation(
        ITestWebApp app, string path, string method) {
        var response = await app.Get("/openapi.json");

        response.Assert.Ok();

        using var document = JsonDocument.Parse(await response.ReadTextAsync());

        return document.RootElement
            .GetProperty("paths").GetProperty(path).GetProperty(method).Clone();
    }

    private static IEnumerable<string> ParameterNames(JsonElement operation) {
        if (!operation.TryGetProperty("parameters", out var parameters)) {
            yield break;
        }

        foreach (var parameter in parameters.EnumerateArray()) {
            yield return parameter.GetProperty("name").GetString()!;
        }
    }

    /// <summary>
    /// A header the handler writes on the context, declared with <c>[AnswersHeader]</c>.
    /// </summary>
    /// <remarks>
    /// The response-mode <c>Created&lt;T&gt;</c> has always published this, because the type
    /// carries the header. A handler returning a plain value carries nothing, and the trial's
    /// throws-mode arm found no way to say it.
    /// </remarks>
    [HardenedTest]
    public async Task AHandlerDeclaresTheHeaderItWrites(ITestWebApp app) {
        var created = (await Operation(app, "/declared-header/notes", "post"))
            .GetProperty("responses").GetProperty("201");

        Assert.Equal(
            "Where the note was created.",
            created.GetProperty("headers").GetProperty("Location")
                .GetProperty("description").GetString());

        Assert.Equal(
            "string",
            created.GetProperty("headers").GetProperty("Location")
                .GetProperty("schema").GetProperty("type").GetString());
    }

    /// <summary>And the value reaches the wire, so the document is describing something real.</summary>
    [HardenedTest]
    public async Task TheDeclaredHeaderIsTheOneTheHandlerSends(ITestWebApp app) {
        var response = await app.Post(new { }, "/declared-header/notes");

        Assert.Equal(201, response.StatusCode);
        Assert.Equal(
            DeclaredHeaderController.CreatedAt, response.Headers["Location"].ToString());
    }

    /// <summary>
    /// <c>[ConditionalGet]</c> publishes the 304 it answers, the <c>ETag</c> it writes and the
    /// conditional headers it reads.
    /// </summary>
    [HardenedTest]
    public async Task AConditionalGetPublishesWhatItAnswersAndReads(ITestWebApp app) {
        var read = await Operation(app, "/declared-header/notes", "get");
        var responses = read.GetProperty("responses");

        Assert.Contains("current", responses.GetProperty("304").GetProperty("description").GetString());

        // The tag on both, because a client needs it from the 200 to send back and gets it again
        // on the 304.
        Assert.True(responses.GetProperty("200").GetProperty("headers").TryGetProperty("ETag", out _));
        Assert.True(responses.GetProperty("304").GetProperty("headers").TryGetProperty("ETag", out _));

        // And no body on the 304, which is what stops a generated client waiting for one.
        Assert.False(responses.GetProperty("304").TryGetProperty("content", out _));

        Assert.Contains("If-None-Match", ParameterNames(read));
        Assert.Contains("If-Modified-Since", ParameterNames(read));
    }

    /// <summary>
    /// The write beside it publishes none of that, because the filter installs on neither of them.
    /// </summary>
    /// <remarks>
    /// The declaration is on the class. Without the reach it states, every operation under a
    /// class-level <c>[ConditionalGet]</c> would publish a 304 it cannot answer and a header
    /// nothing reads - which is the same defect as publishing nothing, with the sign flipped.
    /// </remarks>
    [HardenedTest]
    public async Task AWriteUnderTheSameClassPublishesNoConditionalRequest(ITestWebApp app) {
        var write = await Operation(app, "/declared-header/notes", "post");

        Assert.False(write.GetProperty("responses").TryGetProperty("304", out _));
        Assert.DoesNotContain("If-None-Match", ParameterNames(write));
        Assert.DoesNotContain("If-Modified-Since", ParameterNames(write));
    }

    /// <summary>
    /// Every status in one <c>responses</c> object is in status order, declared and synthesized
    /// alike.
    /// </summary>
    /// <remarks>
    /// The synthesized ones were appended after the declared block, so a bounded read answering
    /// 200, 304 and 504 published <c>200, 504, 304</c>. The declared half has always been sorted;
    /// this is the other half agreeing with it.
    /// </remarks>
    [HardenedTest]
    public async Task ResponsesAreInStatusOrder(ITestWebApp app) {
        var response = await app.Get("/openapi.json");

        response.Assert.Ok();

        using var document = JsonDocument.Parse(await response.ReadTextAsync());
        var checkedAny = false;

        foreach (var path in document.RootElement.GetProperty("paths").EnumerateObject()) {
            foreach (var operation in path.Value.EnumerateObject()) {
                if (operation.Value.ValueKind != JsonValueKind.Object ||
                    !operation.Value.TryGetProperty("responses", out var responses)) {
                    continue;
                }

                var statuses = responses.EnumerateObject()
                    .Select(status => int.Parse(status.Name)).ToList();

                Assert.Equal(statuses.OrderBy(status => status).ToList(), statuses);

                checkedAny |= statuses.Count > 1;
            }
        }

        Assert.True(checkedAny, "No operation declared more than one status.");
    }
}
