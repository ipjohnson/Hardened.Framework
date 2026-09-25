using Hardened.IntegrationTests.WebApp.SUT.Controllers;
using Hardened.Requests.Abstract.Headers;

namespace Hardened.IntegrationTests.WebApp.SUT.Tests.Controllers;

/// <summary>
/// A <c>byte[]</c> or <c>Stream</c> body reaching the handler as the bytes that were sent.
/// </summary>
/// <remarks>
/// The binder handed every body to the serialization service whatever the parameter's type, and
/// the JSON reader is the default one, so a blob was refused before the handler ran - under
/// <c>application/octet-stream</c>, under <c>application/json</c>, and under no
/// <c>Content-Type</c> at all. A code-first blob upload could not be written: the parameter bound,
/// the document published a <c>requestBody</c> for it, and no request could satisfy it.
/// </remarks>
public class RawBodyTests
{
    /// <summary>Bytes that are not valid JSON under any reading, and not valid UTF-8 either.</summary>
    private static readonly byte[] Payload = [0x7F, 0x00, 0xFF, 0x10, 0x91, 0xC3, 0x28, 0x42];

    private static Action<TestWebRequest> Sending(string? contentType) =>
        request =>
        {
            if (contentType != null)
            {
                request.Headers[KnownHeaders.ContentType] = contentType;
            }
        };

    /// <summary>
    /// The defect. Every <c>Content-Type</c> answered 400 with a field-level envelope naming the
    /// parameter, including the one the operation publishes.
    /// </summary>
    [ModuleTest]
    public async Task Bytes_ReachTheHandlerWhateverTheContentType(ITestWebApp testWebApp)
    {
        foreach (var contentType in new[] { "application/octet-stream", "application/json", null })
        {
            var response = await testWebApp.Put(Payload, "/raw-body/bytes", Sending(contentType));

            response.Assert.Ok();

            var received = response.Deserialize<RawBodyController.Received>();

            Assert.Equal(Payload.Length, received!.Length);
            Assert.Equal(0x7F, received.First);
            Assert.Equal(0x42, received.Last);
        }
    }

    /// <summary>
    /// A <c>Stream</c> parameter is the transport's own body, unread, which is the point of asking
    /// for one rather than a <c>byte[]</c>.
    /// </summary>
    [ModuleTest]
    public async Task AStreamParameterIsTheBody(ITestWebApp testWebApp)
    {
        var response = await testWebApp.Put(Payload, "/raw-body/stream", Sending(null));

        response.Assert.Ok();

        var received = response.Deserialize<RawBodyController.Received>();

        Assert.Equal(Payload.Length, received!.Length);
        Assert.Equal(0x7F, received.First);
    }

    /// <summary>
    /// A request carrying no bytes is a 400 naming the parameter, which is the refusal every other
    /// missing body gets. It is the only reading under which <c>required</c> means anything here: a
    /// transport delivers "no body" and <c>Content-Length: 0</c> as the same empty stream.
    /// </summary>
    [ModuleTest]
    public async Task ARequiredBlobWithNoBodyIsRefusedByName(ITestWebApp testWebApp)
    {
        var response = await testWebApp.Put(Array.Empty<byte>(), "/raw-body/bytes", Sending(null));

        response.Assert.BadRequest();

        Assert.Contains("payload", await response.ReadTextAsync());
    }

    /// <summary>And a nullable one reaches the handler instead, which is what declaring it said.</summary>
    [ModuleTest]
    public async Task AnOptionalBlobWithNoBodyReachesTheHandler(ITestWebApp testWebApp)
    {
        var response = await testWebApp.Put(
            Array.Empty<byte>(),
            "/raw-body/optional",
            Sending(null)
        );

        response.Assert.Ok();

        Assert.Equal(0, response.Deserialize<RawBodyController.Received>()!.Length);
    }
}
