using System.Net;
using System.Text;
using DependencyModules.xUnit.Attributes;
using Hardened.Aws.Lambda.Runtime.Streaming;
using Hardened.Aws.Lambda.Testing;
using Hardened.Requests.Abstract.Headers;
using Hardened.Web.Testing;
using Xunit;

namespace Hardened.IntegrationTests.LambdaHttp.SUT.Tests;

/// <summary>
/// One function answering an event stream as a Lambda response stream and everything else as one
/// payload, through the whole pipeline.
/// </summary>
/// <remarks>
/// Each test asserts on <see cref="StreamedResponseCapture.Opened"/>, because the body alone cannot
/// tell the two paths apart: an event stream answered as one payload carries the same frames.
/// </remarks>
[LambdaWebTesting(ResponseMode = LambdaResponseMode.Mixed)]
public class MixedResponseModeTests
{
    [ModuleTest]
    public async Task AnEventStreamOpensALambdaResponseStream(
        ITestWebApp app,
        IResponseStreamFactory streams
    )
    {
        var response = await app.Get("/orders/live");

        var capture = Assert.IsType<StreamedResponseCapture>(streams);

        Assert.True(capture.Opened, "the event stream was answered as one payload");
        Assert.Equal(HttpStatusCode.OK, capture.Prelude!.StatusCode);
        Assert.Equal(
            KnownContentType.EventStream,
            capture.Prelude.Headers[KnownHeaders.ContentType]
        );
        Assert.Equal(
            "data: {\"id\":\"live-1\",\"quantity\":1}\n\n",
            Body(response).ReplaceLineEndings("\n")
        );
    }

    [ModuleTest]
    public async Task AnOrderIsAnsweredAsOnePayload(ITestWebApp app, IResponseStreamFactory streams)
    {
        var response = await app.Get("/orders/o-1");

        response.Assert.Ok();

        Assert.False(Assert.IsType<StreamedResponseCapture>(streams).Opened);
        Assert.Contains("o-1", Body(response));
    }

    /// <summary>
    /// Stream mode answers this with a body of one newline.
    /// </summary>
    [ModuleTest]
    public async Task AnAnswerWithNoBodyHasNoBody(ITestWebApp app, IResponseStreamFactory streams)
    {
        var response = await app.Delete("/orders/o-1");

        Assert.Equal(200, response.StatusCode);
        Assert.False(Assert.IsType<StreamedResponseCapture>(streams).Opened);
        Assert.Equal("", Body(response));
    }

    /// <summary>
    /// A HEAD request runs the stream to its end and discards every byte, so nothing reaches the
    /// body and the answer is one payload carrying the length the GET's body has.
    /// </summary>
    [ModuleTest]
    public async Task AHeadRequestToAnEventStreamIsAnsweredAsOnePayload(
        ITestWebApp app,
        IResponseStreamFactory streams
    )
    {
        var length = Encoding.UTF8.GetByteCount(Body(await app.Get("/orders/live")));

        var response = await app.Request("HEAD", null, "/orders/live");

        Assert.False(Assert.IsType<StreamedResponseCapture>(streams).Opened);
        Assert.Equal(200, response.StatusCode);
        Assert.Equal(length.ToString(), response.Headers[KnownHeaders.ContentLength].ToString());
        Assert.Equal("", Body(response));
    }

    private static string Body(TestWebResponse response)
    {
        response.Body.Position = 0;

        return new StreamReader(response.Body, Encoding.UTF8).ReadToEnd();
    }
}
