using System.Net;
using System.Text;
using System.Text.Json;
using Amazon.Lambda.Core.ResponseStreaming;
using Hardened.Amz.Shared.Lambda.Runtime.Streaming;
using Hardened.Amz.Shared.Lambda.Testing;
using Hardened.Amz.Web.Lambda.Runtime.Impl;
using Hardened.Shared.Runtime.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace LambdaWebTest.Tests;

/// <summary>
/// The one host, end to end through the real container: a generated <c>Application</c>, the real
/// middleware chain and routing table, and the handler the generated <c>Main</c> hands to the
/// bootstrap - driven with the input stream the bootstrap would hand it.
///
/// <para>
/// The seam is substituted through the constructor's override delegate, which the generated
/// provider factory applies after the modules, so what is under test is everything but the
/// socket.
/// </para>
/// </summary>
public class ResponseModeTests {

    private sealed class CapturingResponseStreamFactory : IResponseStreamFactory {
        public List<HttpResponseStreamPrelude> Preludes { get; } = [];

        public MemoryStream Target { get; } = new();

        public Stream CreateStream() => Target;

        public Stream CreateHttpStream(HttpResponseStreamPrelude prelude) {
            Preludes.Add(prelude);

            return Target;
        }
    }

    private static string Event(string path, string method = "GET") =>
        JsonSerializer.Serialize(new {
            rawPath = path,
            requestContext = new { http = new { method, path } },
            headers = new Dictionary<string, string> { ["accept"] = "application/json" },
        });

    private static (Application Application, CapturingResponseStreamFactory Streams) Build(string? mode) {
        var streams = new CapturingResponseStreamFactory();
        var environment = new EnvironmentImpl(environmentValues: mode == null
            ? new Dictionary<string, string>()
            : new Dictionary<string, string> { [LambdaResponseModeConfiguration.EnvironmentVariable] = mode });

        var application = new Application(environment, (_, services) => services.AddSingleton<IResponseStreamFactory>(streams));

        return (application, streams);
    }

    private static Task<Stream> Invoke(Application application, string payload) =>
        application.Provider.GetRequiredService<ILambdaWebHost>()
            .Invoke(new MemoryStream(Encoding.UTF8.GetBytes(payload)), TestLambdaContext.FromName("LambdaWebTest"));

    [Fact]
    public void TheModeDefaultsToBuffered() {
        var (application, _) = Build(mode: null);

        Assert.Equal(LambdaResponseMode.Buffered,
            application.Provider.GetRequiredService<IOptions<ILambdaResponseModeConfiguration>>().Value.Mode);
    }

    [Fact]
    public void TheEnvironmentVariableSelectsStreamMode() {
        var (application, _) = Build("stream");

        Assert.Equal(LambdaResponseMode.Stream,
            application.Provider.GetRequiredService<IOptions<ILambdaResponseModeConfiguration>>().Value.Mode);
    }

    /// <summary>
    /// A misspelt setting fails the host rather than running buffered behind a front door that
    /// expects the prelude.
    /// </summary>
    [Fact]
    public void AnUnrecognisedModeFailsWhenTheHostIsResolved() {
        var (application, _) = Build("streaming");

        var failure = Assert.Throws<InvalidOperationException>(
            () => application.Provider.GetRequiredService<ILambdaWebHost>());

        Assert.Contains(LambdaResponseModeConfiguration.EnvironmentVariable, failure.Message);
    }

    [Fact]
    public async Task InBufferedModeTheHostReturnsThePayloadForTheRoute() {
        var (application, streams) = Build(mode: null);

        using var output = await Invoke(application, Event("/some-author/some-name"));
        using var document = await JsonDocument.ParseAsync(output, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(200, document.RootElement.GetProperty("statusCode").GetInt32());
        Assert.Empty(streams.Preludes);
    }

    [Fact]
    public async Task InStreamModeAMatchedRouteOpensTheStreamWith200AndWritesTheBody() {
        var (application, streams) = Build("stream");

        await Invoke(application, Event("/some-author/some-name"));

        var prelude = Assert.Single(streams.Preludes);

        Assert.Equal(HttpStatusCode.OK, prelude.StatusCode);
        Assert.Equal("application/json", prelude.Headers["Content-Type"]);
        Assert.Equal("{}", Encoding.UTF8.GetString(streams.Target.ToArray()));
    }

    /// <summary>
    /// An unmatched route is a 404 with nothing to say, and a streamed response with nothing in
    /// it hangs CloudFront - so the stream opens at the end, with the 404, and carries a newline.
    /// </summary>
    [Fact]
    public async Task InStreamModeAnUnmatchedRouteOpensTheStreamWith404AndANewline() {
        var (application, streams) = Build("stream");

        await Invoke(application, Event("/nope"));

        Assert.Equal(HttpStatusCode.NotFound, Assert.Single(streams.Preludes).StatusCode);
        Assert.Equal("\n", Encoding.UTF8.GetString(streams.Target.ToArray()));
    }

    /// <summary>
    /// The buffered <c>Invoke</c> the tests and the harness drive is unchanged by the mode: a
    /// stream-mode application still answers it with the payload.
    /// </summary>
    [Fact]
    public async Task TheBufferedInvokeStillAnswersInStreamMode() {
        var (application, streams) = Build("stream");

        var response = await application.Invoke(
            RoutingStatusTests.Request("/some-author/some-name"), TestLambdaContext.FromName("LambdaWebTest"));

        Assert.Equal(200, response.StatusCode);
        Assert.Empty(streams.Preludes);
    }
}
