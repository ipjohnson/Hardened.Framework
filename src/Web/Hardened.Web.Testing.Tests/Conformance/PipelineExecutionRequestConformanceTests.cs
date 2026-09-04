using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Testing.Conformance;

namespace Hardened.Web.Testing.Tests.Conformance;

/// <summary>
/// The pipeline handler's half of <see cref="ExecutionRequestConformanceTests"/>: an
/// <see cref="HttpRequestMessage"/> built the way a client library builds one, translated by the
/// handler and nothing else.
/// </summary>
public class PipelineExecutionRequestConformanceTests : ExecutionRequestConformanceTests {
    protected override IExecutionRequestConformanceAdapter Adapter { get; } = new PipelineAdapter();

    private sealed class PipelineAdapter : IExecutionRequestConformanceAdapter {
        public string TransportName => "PipelineHttpMessageHandler";

        public IExecutionRequest CreateRequest(ConformanceRequestSpec spec) {
            var query = string.Join("&", spec.QueryString.Select(pair =>
                Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value)));

            var uri = new Uri("http://harness" + spec.Path + (query.Length > 0 ? "?" + query : ""));
            var message = new HttpRequestMessage(new HttpMethod(spec.Method), uri);

            if (spec.Body != null) {
                message.Content = new ByteArrayContent(spec.Body);
            }

            foreach (var header in spec.Headers) {
                if (!message.Headers.TryAddWithoutValidation(header.Key, header.Value)) {
                    // A content header with no content to carry it: an empty body, as a client
                    // sending only Content-Type would.
                    message.Content ??= new ByteArrayContent(Array.Empty<byte>());
                    message.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            if (spec.Cookies.Count > 0) {
                message.Headers.TryAddWithoutValidation("Cookie", string.Join("; ", spec.Cookies));
            }

            return PipelineHttpMessageHandler.CreateRequestAsync(message, null, CancellationToken.None)
                .GetAwaiter().GetResult();
        }
    }
}
