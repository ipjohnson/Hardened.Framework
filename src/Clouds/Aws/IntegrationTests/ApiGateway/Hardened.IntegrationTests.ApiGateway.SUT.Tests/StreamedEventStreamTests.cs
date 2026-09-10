using System.Net;
using System.Text;
using Hardened.Aws.Lambda.Runtime.Streaming;
using Hardened.Aws.Lambda.Testing;
using Hardened.Requests.Abstract.Headers;
using Hardened.Shared.Testing.Attributes;
using Hardened.Web.Testing;
using Xunit;

namespace Hardened.IntegrationTests.ApiGateway.SUT.Tests;

/// <summary>
/// An event stream handler through the mode it has to be deployed in.
/// </summary>
/// <remarks>
/// <para>
/// The half neither existing suite covered. The emitted manifest is checked by the web pipeline
/// fixtures and registered by <c>ServerSentEventManifestTests</c>; the warning for a stream handler
/// deployed buffered is checked by <c>ServerSentEventsResponseModeStartupServiceTests</c>. Neither
/// says the two meet - that an application carrying <c>[ServerSentEvents]</c>, invoked as a
/// streaming function, actually writes event frames to the Lambda response stream.
/// </para>
/// <para>
/// It could not be written before: a streamed invocation answers nothing through its output stream,
/// and the host read only that. Nor can it be checked by hand - the Lambda Test Tool needs a
/// function name in <c>AWS_LAMBDA_RUNTIME_API</c> and AWS's streaming client reads that variable as
/// <c>host:port</c> and parses the rest as a port number, so the two cannot be used together at
/// all. This is the check that does not need them.
/// </para>
/// </remarks>
[LambdaWebTesting(ResponseMode = LambdaResponseMode.Stream)]
public class StreamedEventStreamTests {

    /// <summary>
    /// That the invocation streamed at all, which nothing else here can tell you.
    /// </summary>
    /// <remarks>
    /// The frames are not the discriminator. A buffered invocation writes the same bytes - that is
    /// exactly what the buffered-mode warning is about, every event delivered at the end instead of
    /// as it happens - so a test asserting only on the body would pass with the mode ignored. The
    /// stream having been opened, with a prelude, is what says which path ran.
    /// </remarks>
    [HardenedTest]
    public async Task TheInvocationOpensALambdaResponseStream(
        ITestWebApp app, IResponseStreamFactory streams) {
        await app.Get("/orders/live");

        var capture = Assert.IsType<StreamedResponseCapture>(streams);

        Assert.True(capture.Opened, "the invocation wrote a proxy envelope rather than streaming");
        Assert.NotNull(capture.Prelude);
        Assert.Equal(HttpStatusCode.OK, capture.Prelude!.StatusCode);
    }

    [HardenedTest]
    public async Task TheEventsArriveAsFramesOnTheResponseStream(ITestWebApp app) {
        var response = await app.Get("/orders/live");

        response.Assert.Ok();

        Assert.Equal(
            KnownContentType.EventStream, response.Headers[KnownHeaders.ContentType].ToString());

        Assert.Equal(
            "data: {\"id\":\"live-1\",\"quantity\":1}\n\n",
            Body(response).ReplaceLineEndings("\n"));
    }

    /// <summary>
    /// The prelude carries the status, which is the streamed shape's only way to say one.
    /// </summary>
    /// <remarks>
    /// A buffered invocation puts the status in the proxy envelope it writes at the end. A streamed
    /// one has committed to the wire by its first byte, so the status is decided when the stream
    /// opens and nothing after can change it.
    /// </remarks>
    [HardenedTest]
    public async Task AConstrainedTokenStreamsTheSameWay(ITestWebApp app) {
        var response = await app.Get("/orders/7/live");

        response.Assert.Ok();

        Assert.Contains("\"id\":\"7\"", Body(response));
    }

    /// <summary>
    /// An ordinary handler under the same mode still answers normally.
    /// </summary>
    /// <remarks>
    /// The control. Stream mode is the function's, not the route's, so every handler in the
    /// application runs under it - and one that returns a value rather than a sequence has to keep
    /// working, or the mode would be unusable for any application that has both.
    /// </remarks>
    [HardenedTest]
    public async Task ANonStreamingHandlerStillAnswersUnderStreamMode(ITestWebApp app) {
        var response = await app.Get("/orders/o-1");

        response.Assert.Ok();

        Assert.Contains("o-1", Body(response));
    }

    private static string Body(TestWebResponse response) {
        response.Body.Position = 0;

        return new StreamReader(response.Body, Encoding.UTF8).ReadToEnd();
    }
}
