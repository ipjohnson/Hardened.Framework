using Hardened.Azure.Functions.CosmosDb;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Testing.Conformance;
using Microsoft.Extensions.Primitives;

namespace Hardened.Azure.Functions.Runtime.Tests.Conformance;

/// <summary>
/// The request a change handler actually meets, held to the payload-shaped profile.
/// </summary>
/// <remarks>
/// <para>
/// The per-document fork rather than the batch request, for the reason the Service Bus enrolment
/// gives. The request is built over a document holding the spec's bytes rather than through the
/// adapter's split, because the split reads a JSON array and the profile's bodies are bytes that
/// need not be JSON - a document that is not one cannot exist in a container, so there is nothing
/// for the split to be handed. The split has its own tests.
/// </para>
/// <para>
/// The spec's headers are applied through <c>Clone</c>, because a document has no header-like
/// channel at all: what the fork puts in headers is the document's own system properties, and
/// there is nowhere on a document to carry a header the spec chose. That is a larger concession
/// than the Service Bus enrolment makes, and it is the honest one.
/// </para>
/// </remarks>
public class CosmosDbRequestConformanceTests : PayloadExecutionRequestConformanceTests {
    protected override IExecutionRequestConformanceAdapter Adapter { get; } = new CosmosDbAdapter_();

    private sealed class CosmosDbAdapter_ : IExecutionRequestConformanceAdapter {
        public string TransportName => "Azure Functions Cosmos DB";

        public IExecutionRequest CreateRequest(ConformanceRequestSpec spec) {
            var document = new CosmosDbDocument(spec.Body ?? Array.Empty<byte>(), "conformance", "1", null, null);

            var batch = new CosmosDbRequest(
                "CHANGE",
                "/conformance",
                Stream.Null,
                new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase),
                new[] { document });

            var fork = batch.ForDocument(document);

            var headers = new Dictionary<string, StringValues>(fork.Headers, StringComparer.OrdinalIgnoreCase);

            foreach (var header in spec.Headers) {
                headers[header.Key] = header.Value;
            }

            return fork.Clone(method: spec.Method, path: spec.Path, headers: headers);
        }
    }
}
