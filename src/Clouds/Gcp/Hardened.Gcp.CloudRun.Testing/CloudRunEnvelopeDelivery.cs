using System.Runtime.ExceptionServices;
using System.Text.Json;
using Google.Events.Protobuf.Cloud.Firestore.V1;
using Google.Protobuf;
using Hardened.CloudEvents;
using Hardened.Functions.Testing;
using Hardened.Gcp.CloudRun.Firestore;
using Hardened.Gcp.CloudRun.Invoke;
using Hardened.Gcp.CloudRun.PubSub;
using Hardened.Gcp.CloudRun.Scheduler;
using Hardened.Gcp.CloudRun.Storage;
using Hardened.Requests.Abstract.Headers;
using Hardened.Shared.Testing.Impl;
using Hardened.Web.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;

namespace Hardened.Gcp.CloudRun.Testing;

/// <summary>
/// Turns a message and a route into the request its source would have sent, and posts it to the
/// test's host.
/// </summary>
/// <remarks>
/// <para>
/// The higher-fidelity half of <see cref="ITriggerDelivery"/>. A test says
/// <c>queues.Orders(order)</c> and what runs is the real envelope through the real front door:
/// the push is recognised, the data is decoded, the attributes and metadata become headers, the
/// chain is forked with the trigger request and the one dispatch a Cloud Run service runs routes
/// it. The neutral delivery covers everything from routing inwards; this adds the parts only the
/// provider knows: a push for a queue, an Eventarc CloudEvent for a topic, a bucket, a document or
/// a bus event, a Scheduler request for a timer, and a POST to the invoke route for a call.
/// </para>
/// <para>
/// One request per message, because that is what every source on Cloud Run does: a delivery
/// carries one message and every message is acknowledged on its own. Every message is delivered
/// before a failure is reported, so a test can assert that the messages after a refused one were
/// still handled - and the failure reported is the first one, rethrown with its own stack on the
/// pipeline host, where the handler's exception crosses no wire, and as the negative
/// acknowledgement the source would have seen over a socket, where only the status does.
/// </para>
/// </remarks>
public sealed class CloudRunEnvelopeDelivery : ITriggerDelivery {
    private readonly IServiceProvider _provider;

    /// <summary>The project every resource this delivery names belongs to.</summary>
    public const string Project = "test-project";

    /// <summary>A fixed time, so an envelope is the same from one run to the next.</summary>
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

    /// <summary>The resource name of <paramref name="topic"/>, as <c>ce-source</c> carries it.</summary>
    public static string TopicSource(string topic) => "//pubsub.googleapis.com/projects/" + Project + "/topics/" + topic;

    /// <summary>The resource name of <paramref name="bucket"/>, as <c>ce-source</c> carries it.</summary>
    public static string BucketSource(string bucket) => "//storage.googleapis.com/projects/_/buckets/" + bucket;

    /// <summary>The database every document lives in, as <c>ce-source</c> carries it.</summary>
    public static string DatabaseSource => "//firestore.googleapis.com/projects/" + Project + "/databases/(default)";

    /// <summary>Whether a source reads <paramref name="status"/> as an acknowledgement: every one on Cloud Run reads a 2xx as one, and Pub/Sub 102 too.</summary>
    public static bool Acknowledged(int status) => status is 102 or >= 200 and < 300;

    public async Task Deliver(IReadOnlyList<object> messages, string scheme, string path) {
        var host = Host();
        var name = path.TrimStart('/');
        var token = Token();

        Exception? failure = null;

        foreach (var request in Requests(messages, scheme, name, path)) {
            var response = await host.SendAsync(request, token);

            if (Acknowledged(response.StatusCode)) {
                continue;
            }

            failure ??= response.Failure ?? await NotAcknowledged(scheme, path, response);
        }

        if (failure != null) {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    /// <summary>
    /// One direct invocation: a POST to the invoke route, and the handler's answer read back out
    /// of the response the way a caller would read it.
    /// </summary>
    public async Task<object?> Call(object message, string scheme, string path, Type? responseType) {
        if (scheme != InvokeEnvelope.InvokeScheme) {
            throw new NotSupportedException($"A call is a direct invocation, and {scheme} is not one.");
        }

        var operation = path.TrimStart('/');
        var body = JsonSerializer.SerializeToUtf8Bytes(message, Wire);

        var response = await Host().SendAsync(
            Post(InvokeEnvelope.DefaultPrefix + operation, body, Json()), Token());

        if (!Acknowledged(response.StatusCode)) {
            ExceptionDispatchInfo.Capture(response.Failure ?? await NotAcknowledged(scheme, path, response)).Throw();
        }

        if (responseType == null) {
            return null;
        }

        response.Body.Position = 0;

        return await JsonSerializer.DeserializeAsync(response.Body, responseType, Wire);
    }

    private static IEnumerable<TestHostRequest> Requests(
        IReadOnlyList<object> messages, string scheme, string name, string path) {
        switch (scheme) {
            case PubSubPushEnvelope.QueueScheme:
                for (var index = 0; index < messages.Count; index++) {
                    yield return Post("/", PubSubPush.Body(
                        Subscription(name), Serialize(messages[index]), messageId: name + "-" + index, publishTime: PublishTime), Json());
                }

                break;

            case PubSubTopicEnvelope.TopicScheme:
                for (var index = 0; index < messages.Count; index++) {
                    var push = PubSubPush.Body(
                        Subscription("eventarc-" + name), Serialize(messages[index]), messageId: name + "-" + index, publishTime: PublishTime);

                    yield return Post("/", push, CloudEvent(
                        PubSubTopicEnvelope.MessagePublishedType, TopicSource(name), subject: null, name + "-" + index));
                }

                break;

            case SchedulerEnvelope.TimerScheme:
                // A schedule carries no message and the façade sends none; a test that sent some
                // gets one run per message with that message as the body a job was configured with.
                if (messages.Count == 0) {
                    yield return new TestHostRequest(
                        "POST", SchedulerEnvelope.DefaultPrefix + name, Scheduler(name), Stream.Null, null);
                }

                foreach (var message in messages) {
                    var headers = Scheduler(name);

                    headers[KnownHeaders.ContentType] = "application/json";

                    yield return new TestHostRequest(
                        "POST", SchedulerEnvelope.DefaultPrefix + name, headers,
                        new MemoryStream(Serialize(message), writable: false), null);
                }

                break;

            case StorageEnvelope.BlobScheme:
                for (var index = 0; index < messages.Count; index++) {
                    var objectName = ObjectName(messages[index], index);

                    yield return Post("/", StorageObject(name, objectName, messages[index]), CloudEvent(
                        StorageEnvelope.ObjectTypePrefix + "finalized", BucketSource(name), "objects/" + objectName, name + "-" + index));
                }

                break;

            case FirestoreEnvelope.ChangeScheme:
                for (var index = 0; index < messages.Count; index++) {
                    var id = DocumentId(messages[index], index);
                    var document = FirestoreDocument(name, id, messages[index]);

                    // The same document as both values, because a test that wanted a create or a
                    // delete is asserting on the event type, and this delivery exists to exercise
                    // the envelope rather than to model a collection's history.
                    var data = new DocumentEventData { Value = document, OldValue = document }.ToByteArray();

                    var headers = CloudEvent(
                        FirestoreEnvelope.DocumentTypePrefix + "updated", DatabaseSource,
                        "documents/" + name + "/" + id, name + "-" + index);

                    headers[KnownHeaders.ContentType] = "application/protobuf";

                    yield return new TestHostRequest("POST", "/", headers, new MemoryStream(data, writable: false), null);
                }

                break;

            case CloudEventRoutes.EventScheme:
                // The route is /{source}/{type}; a type has no slash in it, so the last segment
                // is the type and everything before it is the source, slashes and all.
                var split = path.LastIndexOf('/');
                var source = path.Substring(1, Math.Max(split - 1, 0));
                var type = path.Substring(split + 1);

                for (var index = 0; index < messages.Count; index++) {
                    yield return Post("/", Serialize(messages[index]), CloudEvent(type, source, null, type + "-" + index));
                }

                break;

            default:
                throw new NotSupportedException(
                    $"No Cloud Run envelope is built for the {scheme} scheme. Queues, topics, timers, " +
                    "changes, blobs and events have one; a stream is not bound on Google.");
        }
    }

    private static byte[] Serialize(object message) => JsonSerializer.SerializeToUtf8Bytes(message, Wire);

    private static TestHostRequest Post(string path, byte[] body, Dictionary<string, StringValues> headers) =>
        new("POST", path, headers, new MemoryStream(body, writable: false), null);

    private static Dictionary<string, StringValues> Json() =>
        new(StringComparer.OrdinalIgnoreCase) { [KnownHeaders.ContentType] = "application/json" };

    /// <summary>The binary-mode headers of one CloudEvent, with a JSON body's content type.</summary>
    private static Dictionary<string, StringValues> CloudEvent(string type, string source, string? subject, string id) {
        var headers = Json();

        headers[CloudEventHeaders.SpecVersion] = "1.0";
        headers[CloudEventHeaders.Id] = id;
        headers[CloudEventHeaders.Source] = source;
        headers[CloudEventHeaders.Type] = type;
        headers[CloudEventHeaders.Time] = PublishTime;

        if (subject != null) {
            headers[CloudEventHeaders.Subject] = subject;
        }

        return headers;
    }

    /// <summary>The headers Cloud Scheduler adds to a job's request.</summary>
    private static Dictionary<string, StringValues> Scheduler(string job) =>
        new(StringComparer.OrdinalIgnoreCase) {
            [SchedulerEnvelope.MarkerHeader] = "true",
            [SchedulerEnvelope.JobNameHeader] = job,
            [SchedulerEnvelope.ScheduleTimeHeader] = PublishTime,
            ["User-Agent"] = "Google-Cloud-Scheduler"
        };

    /// <summary>
    /// The object's metadata as Cloud Storage writes it, from what the test said about the object.
    /// </summary>
    /// <remarks>
    /// The message is the notification, not the object: a test writing
    /// <c>blobs.Uploads(new Upload { Name = "a.txt", Size = 12 })</c> is describing what Storage
    /// would say about an object. <c>size</c> and <c>generation</c> are written as strings, which
    /// is how proto JSON carries an int64 and what Eventarc actually sends.
    /// </remarks>
    private static byte[] StorageObject(string bucket, string name, object message) {
        using var document = JsonDocument.Parse(Serialize(message));

        var root = document.RootElement;
        var buffer = new MemoryStream();

        using (var writer = new Utf8JsonWriter(buffer)) {
            writer.WriteStartObject();
            writer.WriteString("kind", "storage#object");
            writer.WriteString("id", bucket + "/" + name + "/1");
            writer.WriteString("bucket", bucket);
            writer.WriteString("name", name);
            writer.WriteString("size", Number(root, "size") ?? "0");
            writer.WriteString("contentType", String(root, "contentType") ?? "application/octet-stream");
            writer.WriteString("generation", "1");
            writer.WriteString("metageneration", "1");
            writer.WriteString("etag", "CAE=");
            writer.WriteString("timeCreated", PublishTime);
            writer.WriteString("updated", PublishTime);
            writer.WriteEndObject();
        }

        return buffer.ToArray();
    }

    private static string ObjectName(object message, int index) {
        using var document = JsonDocument.Parse(Serialize(message));

        return String(document.RootElement, "name") ?? String(document.RootElement, "key") ?? "object-" + index;
    }

    private static string DocumentId(object message, int index) {
        using var document = JsonDocument.Parse(Serialize(message));

        return String(document.RootElement, "id") ?? "document-" + index;
    }

    /// <summary>The message as a Firestore document, in the typed form a document event carries.</summary>
    private static Document FirestoreDocument(string collection, string id, object message) {
        using var document = JsonDocument.Parse(Serialize(message));

        var firestore = new Document {
            Name = "projects/" + Project + "/databases/(default)/documents/" + collection + "/" + id
        };

        FirestoreValueWire.WriteFields(firestore.Fields, document.RootElement);

        return firestore;
    }

    private static string? String(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? Number(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.Number
            ? value.GetRawText()
            : null;

    private static async Task<Exception> NotAcknowledged(string scheme, string path, TestWebResponse response) {
        var answer = await response.ReadTextAsync();

        return new InvalidOperationException(
            $"The source would not read the answer to {scheme} {path} as an acknowledgement: the " +
            $"service answered {response.StatusCode}. It said: {answer}");
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
