using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Testing;
using Hardened.Requests.Testing.Conformance;
using Microsoft.Extensions.Primitives;

namespace Hardened.Web.Testing.Tests.Conformance;

/// <summary>
/// The pipeline handler's half of <see cref="ExecutionResponseConformanceTests"/>: what an
/// <see cref="HttpClient"/> receives once the handler has turned the response into a message.
/// </summary>
public class PipelineExecutionResponseConformanceTests : ExecutionResponseConformanceTests {
    protected override IExecutionResponseConformanceAdapter Adapter { get; } = new PipelineAdapter();

    private sealed class PipelineAdapter : IExecutionResponseConformanceAdapter {
        public string TransportName => "PipelineHttpMessageHandler";

        public IExecutionResponse CreateResponse() =>
            new TestExecutionResponse(new MemoryStream()) {
                Headers = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase)
            };

        public async Task<ObservedResponse> Complete(IExecutionResponse response) {
            var message = PipelineHttpMessageHandler.ToResponse(response, (MemoryStream)response.Body, null);

            var headers = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var header in message.Headers.Concat(message.Content.Headers)) {
                headers[header.Key] = header.Value.ToList();
            }

            var cookies = message.Headers.TryGetValues("Set-Cookie", out var values)
                ? values.ToList()
                : new List<string>();

            return new ObservedResponse(
                (int)message.StatusCode,
                headers,
                cookies,
                await message.Content.ReadAsByteArrayAsync());
        }
    }
}
