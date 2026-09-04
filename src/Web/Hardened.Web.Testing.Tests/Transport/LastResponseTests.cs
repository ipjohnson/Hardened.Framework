using Hardened.Requests.Testing;
using Hardened.Web.Testing.Tests.Conformance;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace Hardened.Web.Testing.Tests.Transport;

/// <summary>
/// What <see cref="LastResponse"/> does at the edges: before anything was answered, and outside a
/// running test altogether.
/// </summary>
public class LastResponseTests {

    [Fact]
    public async Task ARequestThroughTheHandlerMakesItAvailable() {
        var host = new PipelineHost(context => {
            context.Response.Status = 204;

            return Task.CompletedTask;
        });

        Assert.False(LastResponse.IsAvailable);

        using var client = host.Client();
        using var response = await client.DeleteAsync("/things/1", TestContext.Current.CancellationToken);

        Assert.True(LastResponse.IsAvailable);
        Assert.Equal(204, LastResponse.Status);
        Assert.Empty(LastResponse.Body);
    }

    /// <summary>
    /// With the execution context suppressed, the task below runs with no xUnit test in scope,
    /// which is what a request answered from a thread of the harness's own would look like.
    /// </summary>
    [Fact]
    public async Task OutsideARunningTestItSaysSoAndRecordsNothing() {
        Task<(string Message, bool Available)> outside;

        using (ExecutionContext.SuppressFlow()) {
            outside = Task.Run(() => {
                var response = new TestExecutionResponse(new MemoryStream()) {
                    Headers = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase),
                    Status = 200
                };

                LastResponse.Record(response, Array.Empty<byte>());

                var failure = Assert.Throws<InvalidOperationException>(() => LastResponse.Status);

                return (failure.Message, LastResponse.IsAvailable);
            });
        }

        var (message, available) = await outside;

        Assert.Contains("there is no test running", message);
        Assert.False(available);
        Assert.False(LastResponse.IsAvailable);
    }
}
