using System.Net;
using System.Text;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Amazon.Lambda.Core.ResponseStreaming;
using Hardened.Amz.Shared.Lambda.Runtime.Streaming;
using Hardened.Amz.Web.Lambda.Harness;
using Hardened.Amz.Web.Lambda.Runtime.Impl;
using Hardened.Shared.Runtime.Application;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Hardened.Amz.Web.Lambda.Harness.Tests;

/// <summary>
/// The harness in stream mode: the application's own response mode decides, the stream opens onto
/// the ASP.NET response, and the prelude becomes its status and headers.
///
/// <para>
/// The application here is a stand-in with a container of its own, because the harness reads the
/// mode and the streaming processor out of the application's container rather than being told.
/// </para>
/// </summary>
public class StreamingHarnessTests {

    /// <summary>
    /// What the streaming processor was handed, and a body it writes through the seam it was given.
    /// </summary>
    public sealed class RecordingStreamingProcessor : IStreamingEventProcessor {
        public APIGatewayHttpApiV2ProxyRequest? Request { get; private set; }

        public Func<IResponseStreamFactory, Task> Respond { get; set; } = _ => Task.CompletedTask;

        public Task Process(APIGatewayHttpApiV2ProxyRequest request, ILambdaContext context, IResponseStreamFactory streams) {
            Request = request;

            return Respond(streams);
        }
    }

    /// <summary>
    /// A generated application's shape: an <see cref="IApiGatewayV2Handler"/> that is also an
    /// <see cref="IApplicationRoot"/>. The buffered <c>Invoke</c> records that it was reached.
    /// </summary>
    public class StandInApplication : IApiGatewayV2Handler, IApplicationRoot {
        public static LambdaResponseMode Mode { get; set; } = LambdaResponseMode.Buffered;

        public static RecordingStreamingProcessor Streaming { get; private set; } = new();

        public static bool BufferedInvoked { get; private set; }

        public static void Reset(LambdaResponseMode mode) {
            Mode = mode;
            Streaming = new RecordingStreamingProcessor();
            BufferedInvoked = false;
        }

        public StandInApplication() {
            var services = new ServiceCollection();
            services.AddSingleton(Options.Create<ILambdaResponseModeConfiguration>(
                new LambdaResponseModeConfiguration { Mode = Mode }));
            services.AddSingleton<IStreamingEventProcessor>(Streaming);

            Provider = services.BuildServiceProvider();
        }

        public IServiceProvider Provider { get; }

        public Task<APIGatewayHttpApiV2ProxyResponse> Invoke(APIGatewayHttpApiV2ProxyRequest request, ILambdaContext context) {
            BufferedInvoked = true;

            return Task.FromResult(new APIGatewayHttpApiV2ProxyResponse {
                StatusCode = 200, Body = "buffered", Headers = new Dictionary<string, string>()
            });
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static async Task<HttpContext> Run(LambdaResponseMode mode, Func<IResponseStreamFactory, Task>? respond = null) {
        StandInApplication.Reset(mode);

        if (respond != null) {
            StandInApplication.Streaming.Respond = respond;
        }

        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.Request.Method = "GET";
        context.Request.Path = "/feed";

        var service = new RequestToLambdaService<StandInApplication>();

        await service.HandleRequest(context, () => Task.CompletedTask);

        return context;
    }

    private static string Body(HttpContext context) {
        context.Response.Body.Position = 0;

        return new StreamReader(context.Response.Body, Encoding.UTF8).ReadToEnd();
    }

    [Fact]
    public async Task ABufferedApplicationIsServedThroughItsInvokeMethod() {
        var context = await Run(LambdaResponseMode.Buffered);

        Assert.True(StandInApplication.BufferedInvoked);
        Assert.Null(StandInApplication.Streaming.Request);
        Assert.Equal("buffered", Body(context));
    }

    [Fact]
    public async Task AStreamModeApplicationIsServedThroughTheStreamingProcessor() {
        var context = await Run(LambdaResponseMode.Stream, async streams => {
            var stream = streams.CreateHttpStream(new HttpResponseStreamPrelude { StatusCode = HttpStatusCode.OK });

            await stream.WriteAsync(Encoding.UTF8.GetBytes("data: 1\n\n"));
        });

        Assert.False(StandInApplication.BufferedInvoked);
        Assert.Equal("/feed", StandInApplication.Streaming.Request?.RawPath);
        Assert.Equal("data: 1\n\n", Body(context));
    }

    /// <summary>
    /// The prelude is the HTTP response's status, headers and cookies - the translation a function
    /// URL performs, done here so a browser against the harness sees what it would see deployed.
    /// </summary>
    [Fact]
    public async Task ThePreludeBecomesTheHttpStatusHeadersAndCookies() {
        var context = await Run(LambdaResponseMode.Stream, async streams => {
            var stream = streams.CreateHttpStream(new HttpResponseStreamPrelude {
                StatusCode = HttpStatusCode.Created,
                Headers = new Dictionary<string, string> { ["Content-Type"] = "text/event-stream", ["X-Id"] = "7" },
                Cookies = ["session=abc; Path=/", "theme=dark"],
            });

            await stream.WriteAsync(Encoding.UTF8.GetBytes("body"));
        });

        Assert.Equal(201, context.Response.StatusCode);
        Assert.Equal("text/event-stream", context.Response.Headers["Content-Type"]);
        Assert.Equal("7", context.Response.Headers["X-Id"]);
        Assert.Equal("session=abc; Path=/,theme=dark", context.Response.Headers.SetCookie.ToString());
        Assert.Equal("body", Body(context));
    }

    [Fact]
    public async Task APreludeWithoutAStatusIs200() {
        var context = await Run(LambdaResponseMode.Stream, streams => {
            streams.CreateHttpStream(new HttpResponseStreamPrelude());

            return Task.CompletedTask;
        });

        Assert.Equal(200, context.Response.StatusCode);
    }

    /// <summary>
    /// A stream with no prelude is the function host's shape; on the harness it is the response
    /// body as it stands.
    /// </summary>
    [Fact]
    public async Task APlainStreamIsTheResponseBody() {
        var context = await Run(LambdaResponseMode.Stream, async streams => {
            await streams.CreateStream().WriteAsync(Encoding.UTF8.GetBytes("plain"));
        });

        Assert.Equal("plain", Body(context));
    }
}
