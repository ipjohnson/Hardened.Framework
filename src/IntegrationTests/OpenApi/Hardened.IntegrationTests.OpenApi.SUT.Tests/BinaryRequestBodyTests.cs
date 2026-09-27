using System.Text.Json;
using Hardened.Requests.Abstract.Headers;

namespace Hardened.IntegrationTests.OpenApi.SUT.Tests;

/// <summary>
/// A request body declared <c>application/octet-stream</c> with <c>format: binary</c> is the bytes
/// themselves.
/// </summary>
/// <remarks>
/// The parser dropped the body's format, so the operation took a <c>string</c> and the bytes went to
/// the JSON reader, which answered 400. The same defect as the 0.41 trial's C-01 on the Smithy side.
/// </remarks>
public class BinaryRequestBodyTests
{
    /// <summary>Bytes that are not JSON and not valid UTF-8.</summary>
    private static readonly byte[] Photo = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x7F, 0x91, 0xC3];

    [ModuleTest]
    public async Task PutPetPhoto_ReadsTheRawBytes(ITestWebApp app)
    {
        var response = await app.Put(
            Photo,
            "/pets/1/photo",
            request => request.Headers[KnownHeaders.ContentType] = "application/octet-stream"
        );

        response.Assert.Ok();

        Assert.Equal(Photo.Length, response.Deserialize<PhotoReceipt>().ByteCount);
    }

    [ModuleTest]
    public async Task PutPetPhoto_IsPublishedAsABinaryBody(ITestWebApp app)
    {
        using var document = JsonDocument.Parse(
            await (await app.Get("/openapi.json")).ReadTextAsync()
        );

        var schema = document
            .RootElement.GetProperty("paths")
            .GetProperty("/pets/{petId}/photo")
            .GetProperty("put")
            .GetProperty("requestBody")
            .GetProperty("content")
            .GetProperty("application/octet-stream")
            .GetProperty("schema");

        Assert.Equal("string", schema.GetProperty("type").GetString());
        Assert.Equal("binary", schema.GetProperty("format").GetString());
    }
}
