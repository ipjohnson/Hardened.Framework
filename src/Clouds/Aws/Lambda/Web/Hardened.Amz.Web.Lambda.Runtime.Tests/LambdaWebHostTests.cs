using System.Text;
using System.Text.Json;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Hardened.Amz.Shared.Lambda.Runtime.Streaming;
using Hardened.Amz.Web.Lambda.Runtime.Impl;
using Hardened.Amz.Web.Lambda.Runtime.Tests.Infrastructure;
using Hardened.Shared.Runtime.Collections;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Hardened.Amz.Web.Lambda.Runtime.Tests;

/// <summary>
/// The handler the generated <c>Main</c> hands to the AWS bootstrap: the event read off the input
/// stream, the mode deciding which processor serves it, and the buffered response written back as
/// the payload JSON.
/// </summary>
public class LambdaWebHostTests {

    private const string Event = """
        {"rawPath":"/orders","requestContext":{"http":{"method":"GET","path":"/orders"}},"headers":{"accept":"*/*"}}
        """;

    private sealed class Harness {
        public Harness(LambdaResponseMode mode) {
            Configuration = new LambdaResponseModeConfiguration { Mode = mode };

            Buffered.Process(Arg.Any<APIGatewayHttpApiV2ProxyRequest>(), Arg.Any<ILambdaContext>())
                .Returns(callInfo => {
                    BufferedRequest = callInfo.Arg<APIGatewayHttpApiV2ProxyRequest>();

                    return Task.FromResult(new APIGatewayHttpApiV2ProxyResponse {
                        StatusCode = 201,
                        Headers = new Dictionary<string, string> { ["Content-Type"] = "application/json" },
                        Body = "{\"ok\":true}",
                        Cookies = ["session=abc"],
                    });
                });

            Streaming.Process(
                    Arg.Any<APIGatewayHttpApiV2ProxyRequest>(), Arg.Any<ILambdaContext>(), Arg.Any<IResponseStreamFactory>())
                .Returns(callInfo => {
                    StreamedRequest = callInfo.Arg<APIGatewayHttpApiV2ProxyRequest>();
                    StreamedWith = callInfo.Arg<IResponseStreamFactory>();

                    return Task.CompletedTask;
                });

            Host = new LambdaWebHost(
                Buffered,
                Streaming,
                Streams,
                new MemoryStreamPool(),
                Options.Create<ILambdaResponseModeConfiguration>(Configuration));
        }

        public LambdaResponseModeConfiguration Configuration { get; }

        public IApiGatewayEventProcessor Buffered { get; } = Substitute.For<IApiGatewayEventProcessor>();

        public IStreamingEventProcessor Streaming { get; } = Substitute.For<IStreamingEventProcessor>();

        public CapturingResponseStreamFactory Streams { get; } = new();

        public LambdaWebHost Host { get; }

        public APIGatewayHttpApiV2ProxyRequest? BufferedRequest { get; private set; }

        public APIGatewayHttpApiV2ProxyRequest? StreamedRequest { get; private set; }

        public IResponseStreamFactory? StreamedWith { get; private set; }

        public Task<Stream> Invoke(string payload) =>
            Host.Invoke(new MemoryStream(Encoding.UTF8.GetBytes(payload)), Substitute.For<ILambdaContext>());
    }

    [Fact]
    public async Task InBufferedModeTheEventIsReadFromTheInputAndTheResponseWrittenAsThePayload() {
        var harness = new Harness(LambdaResponseMode.Buffered);

        var output = await harness.Invoke(Event);

        Assert.Equal("/orders", harness.BufferedRequest?.RawPath);
        Assert.Equal("GET", harness.BufferedRequest?.RequestContext.Http.Method);
        Assert.Null(harness.StreamedRequest);

        using var document = JsonDocument.Parse(output);
        var root = document.RootElement;

        Assert.Equal(201, root.GetProperty("statusCode").GetInt32());
        Assert.Equal("application/json", root.GetProperty("headers").GetProperty("Content-Type").GetString());
        Assert.Equal("{\"ok\":true}", root.GetProperty("body").GetString());
        Assert.Equal("session=abc", root.GetProperty("cookies")[0].GetString());
        Assert.False(root.GetProperty("isBase64Encoded").GetBoolean());
    }

    /// <summary>
    /// The returned stream is rewound. The bootstrap copies it from wherever it stands, so a
    /// stream left at its write position sends an empty response body.
    /// </summary>
    [Fact]
    public async Task TheBufferedResponseStreamIsRewound() {
        var harness = new Harness(LambdaResponseMode.Buffered);

        var output = await harness.Invoke(Event);

        Assert.Equal(0, output.Position);
        Assert.True(output.Length > 0);
    }

    [Fact]
    public async Task InStreamModeTheStreamingProcessorServesTheEventThroughTheRuntimesStreams() {
        var harness = new Harness(LambdaResponseMode.Stream);

        var output = await harness.Invoke(Event);

        Assert.Equal("/orders", harness.StreamedRequest?.RawPath);
        Assert.Same(harness.Streams, harness.StreamedWith);
        Assert.Null(harness.BufferedRequest);
        Assert.Equal(0, output.Length);
    }

    [Fact]
    public async Task AnInputThatIsNotAnEventIsRefused() {
        var harness = new Harness(LambdaResponseMode.Buffered);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Invoke("null"));

        Assert.Contains("payload format 2.0", failure.Message);
    }

    /// <summary>
    /// The mode is read once, when the host is built. A deployment setting is not something that
    /// changes between invocations, and reading it per request would let a late amendment switch
    /// protocols under a front door that cannot follow.
    /// </summary>
    [Fact]
    public async Task TheModeIsReadOnceWhenTheHostIsBuilt() {
        var harness = new Harness(LambdaResponseMode.Buffered);

        harness.Configuration.Mode = LambdaResponseMode.Stream;

        await harness.Invoke(Event);

        Assert.NotNull(harness.BufferedRequest);
        Assert.Null(harness.StreamedRequest);
    }
}
