using System.Net;
using System.Net.Sockets;
using System.Text;
using Amazon.Lambda.Core;
using Amazon.Lambda.RuntimeSupport;
using Hardened.Amz.Shared.Lambda.Runtime.Development;
using Xunit;

namespace Hardened.Amz.Shared.Lambda.Runtime.Tests.Development;

/// <summary>
/// The tool started for real, from the manifest at the repository root, and a bootstrap driven
/// through it. This is the whole local story in one place: start, serve, reuse, stop.
/// </summary>
/// <remarks>
/// Needs <c>dotnet tool restore</c> to have run, which CI does before the tests. Without the tool
/// the start fails naming the manifest, and the test fails with it rather than skipping, per
/// docs/testing-conventions.md.
/// </remarks>
public class LambdaEmulatorTests {

    private const string Payload =
        "{\"statusCode\":200,\"headers\":{\"Content-Type\":\"text/plain\"}," +
        "\"body\":\"hello from the bootstrap\",\"isBase64Encoded\":false}";

    [Fact]
    public async Task TheSessionIsPassiveWhenSomethingAlreadyRunsTheFunction() {
        var before = System.Environment.GetEnvironmentVariable(LambdaEmulator.RuntimeApiVariable);
        System.Environment.SetEnvironmentVariable(LambdaEmulator.RuntimeApiVariable, "127.0.0.1:9001");

        try {
            using var session = await LambdaEmulator.StartIfLocal(typeof(LambdaEmulatorTests), apiGateway: true);

            Assert.False(session.IsLocal);
            Assert.False(session.StartedTheTool);
            Assert.Null(session.RuntimeApiEndpoint);
            Assert.Null(session.Plan);
        }
        finally {
            System.Environment.SetEnvironmentVariable(LambdaEmulator.RuntimeApiVariable, before);
        }
    }

    /// <summary>
    /// A request to the gateway emulator reaches a bootstrap polling the runtime emulator and its
    /// response comes back as HTTP. Then a second start finds the tool listening and attaches
    /// instead of starting another, which is what happens after the debugger's stop button. Then
    /// disposing the session that started the tool takes it down.
    /// </summary>
    [Fact]
    public async Task AWebFunctionIsServedThroughTheGatewayThenTheToolIsReusedAndStopped() {
        var emulatorPort = FreePort();
        var gatewayPort = FreePort();
        var plan = LambdaEmulatorPlan.From("Hardened.Amz.Shared.Lambda.Runtime.Tests", true, key => key switch {
            LambdaEmulator.EmulatorPortVariable => emulatorPort.ToString(),
            LambdaEmulator.GatewayPortVariable => gatewayPort.ToString(),
            _ => null
        })!;

        var session = await LambdaEmulator.Start(plan);
        Task? bootstrapRun = null;

        try {
            Assert.True(session.StartedTheTool);
            Assert.Equal(plan.RuntimeApiEndpoint, session.RuntimeApiEndpoint);

            using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            using var bootstrap = LambdaBootstrapBuilder
                .Create((Func<Stream, ILambdaContext, Task<Stream>>)Handler)
                .ConfigureOptions(options => options.RuntimeApiEndpoint = session.RuntimeApiEndpoint)
                .Build();

            bootstrapRun = bootstrap.RunAsync(stop.Token);

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            var response = await http.GetAsync($"{plan.GatewayUrl}/anything/at/all", TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("hello from the bootstrap", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

            using var second = await LambdaEmulator.Start(plan);

            Assert.False(second.StartedTheTool);
            Assert.Equal(session.RuntimeApiEndpoint, second.RuntimeApiEndpoint);

            stop.Cancel();
        }
        finally {
            session.Dispose();
        }

        Assert.True(await StopsListening(emulatorPort), "the tool kept listening after the session that started it was disposed");

        if (bootstrapRun != null) {
            // The tool is gone, so the poll fails and the loop ends. Which way it ends is the AWS
            // bootstrap's business, not this test's.
            await Task.WhenAny(bootstrapRun, Task.Delay(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        }
    }

    private static Task<Stream> Handler(Stream input, ILambdaContext context) =>
        Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(Payload)));

    private static int FreePort() {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        return port;
    }

    private static async Task<bool> StopsListening(int port) {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);

        while (DateTime.UtcNow < deadline) {
            using var client = new TcpClient();

            try {
                await client.ConnectAsync(IPAddress.Loopback, port, TestContext.Current.CancellationToken);
            }
            catch (SocketException) {
                return true;
            }

            await Task.Delay(200, TestContext.Current.CancellationToken);
        }

        return false;
    }
}
