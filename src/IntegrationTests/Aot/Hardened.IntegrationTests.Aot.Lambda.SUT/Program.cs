using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Serialization.Metadata;
using Hardened.Aws.Lambda.Runtime.Hosting;
using Hardened.IntegrationTests.Aot.Lambda.SUT;
using Hardened.Shared.Runtime.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// A Hardened Lambda published with Native AOT. The Kestrel sibling proves a native binary can serve
// an HTTP request; this proves one can serve an invocation, which is a different path end to end -
// the adapter recognises the payload, the batch filter forks it, the binder reads a source-
// generated context and the adapter writes the answer back to the Runtime API.
//
// The Runtime API is stubbed in-process rather than mocked away, because the thing under test is
// the real bootstrap talking real HTTP. AWS_LAMBDA_DOTNET_DEBUG_RUN_ONCE makes it serve one
// invocation and return, so the binary exits on its own and CI can read what it printed.

var stub = new RuntimeApiStub("""
    {"Records":[{
      "messageId":"aot-1","receiptHandle":"receipt-1",
      "body":"{\"id\":\"a-1\",\"quantity\":7}",
      "eventSource":"aws:sqs",
      "eventSourceARN":"arn:aws:sqs:us-east-1:123456789012:orders-new",
      "awsRegion":"us-east-1"}]}
    """);

Environment.SetEnvironmentVariable("AWS_LAMBDA_RUNTIME_API", stub.Address);
Environment.SetEnvironmentVariable("AWS_LAMBDA_DOTNET_DEBUG_RUN_ONCE", "true");
Environment.SetEnvironmentVariable("AWS_LAMBDA_FUNCTION_NAME", "orders-function");

var services = new ServiceCollection();

services.AddLogging(builder => builder.AddSimpleConsole().SetMinimumLevel(LogLevel.Warning));
services.AddTransient<IHardenedEnvironment>(_ => new EnvironmentImpl(arguments: args));

// What AotSerializerModule resolves models through. Without it the serializers throw rather than
// falling back to reflection, which is the difference this project exists to hold.
services.AddSingleton<IJsonTypeInfoResolver>(AotContext.Default);

var sink = new OrderSink();
services.AddSingleton<IOrderSink>(sink);

new Application().PopulateServiceCollection(services);

await HardenedLambdaBootstrap.Run(services.BuildServiceProvider());

// Printed rather than logged so the CI probe can read it without depending on log configuration.
// The handler's own view, not the stub's: it says the payload reached a bound Order.
Console.WriteLine(sink.Last is { } order
    ? $"HANDLED {order.Id} x{order.Quantity}"
    : "HANDLED nothing");

/// <summary>
/// The three routes of the Lambda Runtime API, over <see cref="HttpListener"/>.
/// </summary>
/// <remarks>
/// A copy of the stub the runtime loop tests use rather than a reference to it: this is a published
/// executable and that one lives in a test project, so linking them would put xunit in an AOT
/// binary. The protocol is small enough that a second copy is cheaper than the coupling.
/// </remarks>
internal sealed class RuntimeApiStub {
    private readonly HttpListener _listener = new();
    private readonly string _payload;

    public RuntimeApiStub(string payload) {
        _payload = payload;
        Address = "127.0.0.1:" + FreePort();

        _listener.Prefixes.Add("http://" + Address + "/");
        _listener.Start();

        _ = Task.Run(Serve);
    }

    /// <summary>What <c>AWS_LAMBDA_RUNTIME_API</c> is set to: host and port, no scheme.</summary>
    public string Address { get; }

    private async Task Serve() {
        while (true) {
            HttpListenerContext context;

            try {
                context = await _listener.GetContextAsync();
            }
            catch (HttpListenerException) {
                return;
            }
            catch (ObjectDisposedException) {
                return;
            }

            var path = context.Request.Url?.AbsolutePath ?? "";

            if (path.EndsWith("/next", StringComparison.Ordinal)) {
                Next(context.Response);
            }
            else {
                Accepted(context.Response);
            }
        }
    }

    /// <remarks>
    /// The headers are the invocation. <c>Lambda-Runtime-Deadline-Ms</c> is what the host turns
    /// into a cancellation token, so serving it wrong makes every invocation look out of time.
    /// </remarks>
    private void Next(HttpListenerResponse response) {
        response.Headers.Add("Lambda-Runtime-Aws-Request-Id", "8476a536-e9f4-11e8-9739-2dfe598c3fcd");
        response.Headers.Add(
            "Lambda-Runtime-Deadline-Ms",
            DateTimeOffset.UtcNow.AddSeconds(30).ToUnixTimeMilliseconds().ToString());
        response.Headers.Add(
            "Lambda-Runtime-Invoked-Function-Arn",
            "arn:aws:lambda:us-east-1:123456789012:function:orders-function");

        Write(response, _payload);
    }

    /// <remarks>
    /// The body matters. The runtime client deserializes the acknowledgement, so an empty 202 fails
    /// with "could not deserialize the response body".
    /// </remarks>
    private static void Accepted(HttpListenerResponse response) {
        response.StatusCode = 202;
        response.ContentType = "application/json";

        Write(response, """{"status":"OK"}""");
    }

    private static void Write(HttpListenerResponse response, string body) {
        var bytes = Encoding.UTF8.GetBytes(body);

        response.ContentLength64 = bytes.Length;
        response.OutputStream.Write(bytes, 0, bytes.Length);
        response.OutputStream.Close();
    }

    private static int FreePort() {
        var socket = new TcpListener(IPAddress.Loopback, 0);

        socket.Start();

        var port = ((IPEndPoint)socket.LocalEndpoint).Port;

        socket.Stop();

        return port;
    }
}
