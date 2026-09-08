using System.Text;
using Hardened.Gcp.CloudRun.Runtime.Envelopes;
using Hardened.Gcp.CloudRun.Runtime.Execution;
using Hardened.Requests.Runtime.QueryString;
using Hardened.Requests.Testing;
using Microsoft.Extensions.Primitives;

namespace Hardened.Gcp.CloudRun.Runtime.Tests.Envelopes;

/// <summary>
/// The requests the envelope tests hand an envelope: a delivery as Kestrel would have built it,
/// and the buffered body the front door would have handed on.
/// </summary>
internal static class Deliveries {
    public static TestExecutionRequest Request(
        string method, string path, string? contentType, params (string Name, string Value)[] headers) {
        var request = new TestExecutionRequest(method, path, null, EmptyQueryStringCollection.Instance) {
            Headers = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase)
        };

        if (contentType != null) {
            request.Headers["Content-Type"] = contentType;
        }

        foreach (var (name, value) in headers) {
            request.Headers[name] = value;
        }

        return request;
    }

    /// <summary>A POST with a JSON content type, the shape most envelopes look for.</summary>
    public static TestExecutionRequest Post(string path = "/", params (string Name, string Value)[] headers) =>
        Request("POST", path, "application/json", headers);

    /// <summary>The binary-mode headers of one CloudEvent on a POST.</summary>
    public static TestExecutionRequest CloudEvent(
        string type, string source, string? subject = null, string contentType = "application/json",
        string id = "evt-1", params (string Name, string Value)[] more) {
        var headers = new List<(string, string)> {
            ("ce-specversion", "1.0"), ("ce-id", id), ("ce-source", source), ("ce-type", type),
            ("ce-time", "2026-09-07T10:00:00Z")
        };

        if (subject != null) {
            headers.Add(("ce-subject", subject));
        }

        headers.AddRange(more);

        return Request("POST", "/", contentType, headers.ToArray());
    }

    /// <summary>Runs <paramref name="envelope"/> over <paramref name="body"/>, the way the front door does.</summary>
    public static CloudRunTriggerRequest? Unwrap(ITriggerEnvelope envelope, TestExecutionRequest request, string body) =>
        Unwrap(envelope, request, Encoding.UTF8.GetBytes(body));

    public static CloudRunTriggerRequest? Unwrap(ITriggerEnvelope envelope, TestExecutionRequest request, byte[] body) {
        request.Body = new MemoryStream(body, writable: false);

        using var payload = new TriggerPayload(body);

        return envelope.Unwrap(request, payload);
    }

    public static string Text(Stream body) {
        body.Position = 0;

        using var reader = new StreamReader(body, Encoding.UTF8, leaveOpen: true);

        return reader.ReadToEnd();
    }

    public static string Base64(string text) => Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
}
