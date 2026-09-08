using System.Text.Json;
using Google.Events.Protobuf.Cloud.Firestore.V1;
using Google.Protobuf;
using Hardened.Gcp.CloudRun.Firestore;
using Hardened.Gcp.CloudRun.Runtime.Envelopes;
using Hardened.Gcp.CloudRun.Runtime.Tests.Envelopes;
using Hardened.Gcp.CloudRun.Testing;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Testing.Conformance;
using Microsoft.Extensions.Primitives;

namespace Hardened.Gcp.CloudRun.Runtime.Tests.Conformance;

/// <summary>
/// The request a change handler meets on Cloud Run, held to the payload-shaped profile.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="FirestoreChange"/> rather than the base request, because it is the one subclass and
/// it overrides <c>Clone</c>: the clone contract is what a subclass is most likely to get wrong.
/// </para>
/// <para>
/// The spec's body is marshalled into a document's typed fields and delivered as the protobuf
/// event Eventarc sends, so the adapter's value conversion runs for real. A document event has
/// no header-like channel of its own - its headers are the event's attributes and the document's
/// name - so the spec's headers, like its method and path, are applied through <c>Clone</c>, the
/// same door a filter uses.
/// </para>
/// </remarks>
public class FirestoreChangeConformanceTests : PayloadExecutionRequestConformanceTests {
    protected override IExecutionRequestConformanceAdapter Adapter { get; } = new FirestoreAdapter();

    private sealed class FirestoreAdapter : IExecutionRequestConformanceAdapter {
        private readonly FirestoreEnvelope _envelope = new();

        public string TransportName => "Cloud Run Firestore change";

        public IExecutionRequest CreateRequest(ConformanceRequestSpec spec) {
            var document = new Document { Name = "projects/p/databases/(default)/documents/conformance/c-1" };

            // A JSON object is marshalled into the document's fields, so the adapter's value
            // conversion is what produces the body the suite reads back. Anything else - the
            // suite's plain-text body - cannot be a document, and is handed on as bytes after the
            // unwrap so the body assertions hold to the request contract rather than the envelope.
            byte[]? raw = null;

            if (spec.Body != null) {
                try {
                    using var json = JsonDocument.Parse(spec.Body);

                    if (json.RootElement.ValueKind == JsonValueKind.Object) {
                        FirestoreValueWire.WriteFields(document.Fields, json.RootElement);
                    }
                    else {
                        raw = spec.Body;
                    }
                }
                catch (JsonException) {
                    raw = spec.Body;
                }
            }

            var data = new DocumentEventData { Value = document, OldValue = document }.ToByteArray();

            var delivery = Deliveries.CloudEvent(
                FirestoreEnvelope.DocumentTypePrefix + "updated",
                "//firestore.googleapis.com/projects/p/databases/(default)",
                "documents/conformance/c-1",
                "application/protobuf");

            var trigger = Deliveries.Unwrap(_envelope, delivery, data)
                          ?? throw new InvalidOperationException("The envelope declined the event built for it.");

            if (raw != null) {
                trigger.Body = new MemoryStream(raw, writable: false);
            }

            var headers = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);

            foreach (var header in spec.Headers) {
                headers[header.Key] = header.Value;
            }

            return trigger.Clone(method: spec.Method, path: spec.Path, headers: headers);
        }
    }
}
