using System.Runtime.ExceptionServices;
using System.Text.Json;
using Hardened.Functions.Testing;
using Hardened.Requests.Abstract.Headers;
using Hardened.Shared.Testing.Impl;
using Hardened.Web.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;

namespace Hardened.Gcp.CloudRun.Testing;

/// <summary>
/// Turns a message and a route into the push Pub/Sub would have sent, and posts it to the test's
/// host.
/// </summary>
/// <remarks>
/// <para>
/// The higher-fidelity half of <see cref="ITriggerDelivery"/>. A test says
/// <c>queues.Orders(order)</c> and what runs is the real envelope through the real front door:
/// the push is recognised, the data is decoded, the attributes and metadata become headers, the
/// chain is forked with the trigger request and the one dispatch a Cloud Run service runs routes
/// it. The neutral delivery covers everything from routing inwards; this adds the parts only the
/// provider knows.
/// </para>
/// <para>
/// One push per message, because that is what Pub/Sub does: a push carries one message and every
/// message is acknowledged on its own. Every message is delivered before a failure is reported,
/// so a test can assert that the messages after a refused one were still handled - and the
/// failure reported is the first one, rethrown with its own stack on the pipeline host, where the
/// handler's exception crosses no wire, and as the negative acknowledgement Pub/Sub would have
/// seen over a socket, where only the status does.
/// </para>
/// </remarks>
public sealed class CloudRunEnvelopeDelivery : ITriggerDelivery {
    private readonly IServiceProvider _provider;

    /// <summary>The project every subscription this delivery names belongs to.</summary>
    public const string Project = "test-project";

    /// <summary>A fixed publish time, so an envelope is the same from one run to the next.</summary>
    public const string PublishTime = "2026-01-01T00:00:00.000Z";

    /// <summary>
    /// camelCase, which is what a publisher sends and what the handler's binder is set up to read.
    /// </summary>
    private static readonly JsonSerializerOptions Wire =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public CloudRunEnvelopeDelivery(IServiceProvider provider) {
        _provider = provider;
    }

    /// <summary>The resource name of the subscription serving <paramref name="queue"/>.</summary>
    public static string Subscription(string queue) => "projects/" + Project + "/subscriptions/" + queue;

    /// <summary>Whether Pub/Sub reads <paramref name="status"/> as an acknowledgement.</summary>
    public static bool Acknowledged(int status) => status is 102 or 200 or 201 or 202 or 204;

    public async Task Deliver(IReadOnlyList<object> messages, string scheme, string path) {
        if (scheme != "QUEUE") {
            throw new NotSupportedException(
                $"No Cloud Run envelope is built for the {scheme} scheme yet. A queue is a Pub/Sub " +
                "push; a topic, an event, a timer and a blob each arrive with their own adapter.");
        }

        var host = Host();
        var name = path.TrimStart('/');
        var token = Token();

        Exception? failure = null;

        for (var index = 0; index < messages.Count; index++) {
            var data = JsonSerializer.SerializeToUtf8Bytes(messages[index], Wire);

            var body = PubSubPush.Body(
                Subscription(name), data, messageId: name + "-" + index, publishTime: PublishTime);

            var response = await Push(host, body, token);

            if (Acknowledged(response.StatusCode)) {
                continue;
            }

            failure ??= response.Failure ?? await NotAcknowledged(path, response);
        }

        if (failure != null) {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    /// <remarks>
    /// A direct invocation on Cloud Run is <c>POST /_triggers/invoke/{operation}</c> through the
    /// invoke adapter, which Phase 2 brings; until then a <c>[HardenedFunction]</c> has no envelope
    /// here to be called through.
    /// </remarks>
    public Task<object?> Call(object message, string scheme, string path, Type? responseType) =>
        throw new NotSupportedException(
            "Direct invocation has no Cloud Run envelope yet: the invoke adapter arrives in Phase 2.");

    private static Task<TestWebResponse> Push(ITestHost host, byte[] body, CancellationToken token) {
        var headers = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase) {
            [KnownHeaders.ContentType] = "application/json"
        };

        // The path is whatever the push subscription's endpoint was given; the front door reads
        // the body, not the path, so the root is as good as any.
        return host.SendAsync(
            new TestHostRequest("POST", "/", headers, new MemoryStream(body, writable: false), null), token);
    }

    private static async Task<Exception> NotAcknowledged(string path, TestWebResponse response) {
        var answer = await response.ReadTextAsync();

        return new InvalidOperationException(
            $"Pub/Sub would not acknowledge the push to QUEUE {path}: the service answered " +
            $"{response.StatusCode}, and only 102, 200, 201, 202 and 204 acknowledge. It said: {answer}");
    }

    private ITestHost Host() =>
        _provider.GetService<ITestHost>()
        ?? throw new InvalidOperationException(
            "[CloudRunTesting] delivers through the web test host, and none is registered. Add " +
            "[assembly: WebTesting] beside it; [KestrelRuntime] on a class under " +
            "[assembly: KestrelTesting] then runs that class over a socket.");

    private CancellationToken Token() =>
        _provider.GetService<TestCancellationToken>()?.Token ?? CancellationToken.None;
}
