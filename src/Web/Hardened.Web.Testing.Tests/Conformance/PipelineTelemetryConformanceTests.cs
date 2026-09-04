using Hardened.Requests.Testing.Conformance;

namespace Hardened.Web.Testing.Tests.Conformance;

/// <summary>
/// The pipeline handler's half of <see cref="RequestTelemetryConformanceTests"/>: a request sent
/// through an <see cref="HttpClient"/> over the handler begins and ends the way a host's does.
/// </summary>
public class PipelineTelemetryConformanceTests : RequestTelemetryConformanceTests {
    protected override IRequestTelemetryConformanceAdapter Adapter { get; } = new PipelineAdapter();

    private sealed class PipelineAdapter : IRequestTelemetryConformanceAdapter {
        public string TransportName => "PipelineHttpMessageHandler";

        public async Task Dispatch(TelemetryConformanceRequest request) {
            var host = new PipelineHost(context => {
                request.Handler?.Invoke(context);

                return Task.CompletedTask;
            });

            using var client = host.Client();
            using var message = new HttpRequestMessage(new HttpMethod(request.Method), request.Path);

            foreach (var header in request.Headers) {
                message.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
            }

            using var response = await client.SendAsync(message);
        }
    }
}
