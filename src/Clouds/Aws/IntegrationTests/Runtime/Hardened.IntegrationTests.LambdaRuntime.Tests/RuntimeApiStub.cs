using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Hardened.IntegrationTests.LambdaRuntime.Tests;

/// <summary>
/// The Lambda Runtime API, enough of it to serve one invocation.
/// </summary>
/// <remarks>
/// <para>
/// The contract a deployed function actually speaks: ask <c>/runtime/invocation/next</c> for work,
/// then post the answer to <c>/runtime/invocation/{id}/response</c> or the failure to
/// <c>.../error</c>. Standing it up is what makes an end-to-end test end-to-end - the alternative
/// is calling <c>LambdaInvocationHandler</c> directly, which is what every other fixture does and
/// which never exercises the bootstrap, the runtime client, or the deadline the headers carry.
/// </para>
/// <para>
/// <see cref="HttpListener"/> rather than a web framework, because the whole protocol is three
/// routes and this project should not need a server to test a client.
/// </para>
/// </remarks>
public sealed class RuntimeApiStub : IDisposable {
    private readonly HttpListener _listener = new();
    private readonly TaskCompletionSource<Answer> _answered = new();
    private readonly string _payload;
    private readonly CancellationTokenSource _stopping = new();

    public RuntimeApiStub(string payload) {
        _payload = payload;
        Address = "127.0.0.1:" + FreePort();

        _listener.Prefixes.Add("http://" + Address + "/");
        _listener.Start();

        _ = Task.Run(Serve);
    }

    /// <summary>What <c>AWS_LAMBDA_RUNTIME_API</c> is set to: host and port, no scheme.</summary>
    public string Address { get; }

    /// <summary>The request id this invocation is served under.</summary>
    public const string RequestId = "8476a536-e9f4-11e8-9739-2dfe598c3fcd";

    /// <summary>What the function posted back, once it has posted anything.</summary>
    public Task<Answer> Answered => _answered.Task;

    /// <param name="Failed">
    /// True when the function posted to <c>/error</c> rather than <c>/response</c>, which is how a
    /// failed invocation reaches AWS - and what makes a queue redeliver.
    /// </param>
    public record Answer(bool Failed, string Body);

    private async Task Serve() {
        while (!_stopping.IsCancellationRequested) {
            HttpListenerContext context;

            try {
                context = await _listener.GetContextAsync();
            }
            catch (Exception) {
                return;
            }

            var path = context.Request.Url!.AbsolutePath;

            if (path.EndsWith("/invocation/next", StringComparison.Ordinal)) {
                Next(context.Response);
            }
            else if (path.EndsWith("/response", StringComparison.Ordinal) ||
                     path.EndsWith("/error", StringComparison.Ordinal)) {
                using var reader = new StreamReader(context.Request.InputStream);

                _answered.TrySetResult(
                    new Answer(path.EndsWith("/error", StringComparison.Ordinal), await reader.ReadToEndAsync()));

                Accepted(context.Response);
            }
            else {
                Accepted(context.Response);
            }
        }
    }

    /// <remarks>
    /// The headers are the invocation. <c>Lambda-Runtime-Deadline-Ms</c> in particular is what the
    /// host turns into a cancellation token, so serving it wrong would make every invocation look
    /// out of time.
    /// </remarks>
    private void Next(HttpListenerResponse response) {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30).ToUnixTimeMilliseconds();

        response.Headers.Add("Lambda-Runtime-Aws-Request-Id", RequestId);
        response.Headers.Add("Lambda-Runtime-Deadline-Ms", deadline.ToString());
        response.Headers.Add(
            "Lambda-Runtime-Invoked-Function-Arn",
            "arn:aws:lambda:us-east-1:123456789012:function:orders-function");

        Write(response, _payload);
    }

    /// <remarks>
    /// The body matters. The runtime client deserializes the acknowledgement, so an empty 202 -
    /// which looks like a perfectly good "accepted" - fails with "could not deserialize the
    /// response body" and takes down the invocation that had just been reported as failed.
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

    /// <summary>
    /// A port nothing is listening on, found by asking the operating system for one and letting it
    /// go again. Racy in principle and reliable in practice, which is the usual trade for a test
    /// that needs a real socket.
    /// </summary>
    private static int FreePort() {
        var probe = new TcpListener(IPAddress.Loopback, 0);

        probe.Start();

        var port = ((IPEndPoint)probe.LocalEndpoint).Port;

        probe.Stop();

        return port;
    }

    public void Dispose() {
        _stopping.Cancel();
        _listener.Close();
        _stopping.Dispose();
    }
}
