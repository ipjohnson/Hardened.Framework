using System.Text.Json;

namespace Hardened.IntegrationTests.WebApp.SUT.Tests.Controllers;

/// <summary>
/// A handler that answers with bytes and with a declared refusal, driven through the real pipeline.
/// </summary>
/// <remarks>
/// Both halves have to hold at once, and that is the whole test: the success is the bytes under the
/// media type the operation declared, and the refusal is the JSON problem document every other
/// refusal is. Getting one right at the cost of the other is what this shape did in both directions.
/// </remarks>
public class BlobResponseTests
{
    private static readonly byte[] Elf = [0x7F, 0x45, 0x4C, 0x46];

    /// <summary>
    /// The bytes that went to the wire, rather than a deserialization of them. Reading this
    /// response as a string still produces something for a base64 body, which would agree with the
    /// defect rather than catch it.
    /// </summary>
    private static byte[] Sent(TestWebResponse response)
    {
        response.Body.Position = 0;

        var buffer = new MemoryStream();

        response.Body.CopyTo(buffer);

        return buffer.ToArray();
    }

    [HardenedTest]
    public async Task ABareBlobIsWrittenRaw(ITestWebApp app)
    {
        var response = await app.Get("/blob-response/bare/1");

        Assert.Equal(200, response.StatusCode);
        Assert.StartsWith("application/octet-stream", response.Headers["Content-Type"].ToString());
        Assert.Equal(Elf, Sent(response));
    }

    /// <remarks>
    /// The bytes themselves rather than <c>"f0VMRg=="</c>, which is what the JSON serializer makes
    /// of a <c>byte[]</c> it is handed.
    /// </remarks>
    [HardenedTest]
    public async Task ABlobInsideAResponseSetIsWrittenRaw(ITestWebApp app)
    {
        var response = await app.Get("/blob-response/blob/1");

        Assert.Equal(200, response.StatusCode);
        Assert.StartsWith("application/octet-stream", response.Headers["Content-Type"].ToString());
        Assert.Equal(Elf, Sent(response));
    }

    [HardenedTest]
    public async Task AStreamInsideAResponseSetIsWrittenRaw(ITestWebApp app)
    {
        var response = await app.Get("/blob-response/stream/1");

        Assert.Equal(200, response.StatusCode);
        Assert.Equal(Elf, Sent(response));
    }

    /// <summary>
    /// The refusal the set declares, answered as the JSON problem document. It was a 500 with an
    /// empty body: the operation's declared octet stream reached the locator carrying a model, and
    /// nothing can produce that.
    /// </summary>
    [HardenedTest]
    public async Task TheDeclaredRefusalAnswersJson(ITestWebApp app)
    {
        var response = await app.Get("/blob-response/blob/0");

        Assert.Equal(404, response.StatusCode);
        Assert.StartsWith("application/json", response.Headers["Content-Type"].ToString());

        using var document = JsonDocument.Parse(await response.ReadTextAsync());

        Assert.Equal(404, document.RootElement.GetProperty("status").GetInt32());
    }

    [HardenedTest]
    public async Task AStreamSetRefusesInJsonToo(ITestWebApp app)
    {
        var response = await app.Get("/blob-response/stream/0");

        Assert.Equal(404, response.StatusCode);
        Assert.StartsWith("application/json", response.Headers["Content-Type"].ToString());
    }

    /// <summary>
    /// And the document says both: the success is the declared media type carrying binary, the
    /// refusal is JSON. It published a 200 with no content at all, so a generated client had no
    /// return type for the one thing the operation exists to send.
    /// </summary>
    [HardenedTest]
    public async Task TheDocumentDeclaresBytesAndAJsonRefusal(ITestWebApp app)
    {
        using var served = JsonDocument.Parse(
            await (await app.Get("/openapi.json")).ReadTextAsync()
        );

        var responses = served
            .RootElement.GetProperty("paths")
            .GetProperty("/blob-response/blob/{id}")
            .GetProperty("get")
            .GetProperty("responses");

        var success = responses.GetProperty("200").GetProperty("content");

        Assert.Equal(
            "binary",
            success
                .GetProperty("application/octet-stream")
                .GetProperty("schema")
                .GetProperty("format")
                .GetString()
        );

        var refusal = responses.GetProperty("404").GetProperty("content");

        Assert.True(refusal.TryGetProperty("application/json", out _));
        Assert.False(refusal.TryGetProperty("application/octet-stream", out _));
    }
}
