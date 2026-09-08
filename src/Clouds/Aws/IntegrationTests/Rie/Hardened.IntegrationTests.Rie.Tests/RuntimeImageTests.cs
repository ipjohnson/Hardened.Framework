using System.Text.Json;
using Hardened.Functions.Testing.Containers;
using Xunit;

namespace Hardened.IntegrationTests.Rie.Tests;

/// <summary>
/// The SQS fixture served by the real Lambda base image.
/// </summary>
/// <remarks>
/// <para>
/// One container per class rather than per test: the image starts the runtime and the application
/// on the first invocation, which is the cold start every later test then skips, and the tests
/// here read their own observations by order id.
/// </para>
/// <para>
/// <c>Simulator</c> is the trait the CI split reads: these need Docker and a pulled image, and they
/// fail rather than skip without either, as every Docker test in this repository does.
/// </para>
/// </remarks>
[Trait("Category", "Simulator")]
public sealed class RuntimeImageTests : IClassFixture<RuntimeImageTests.Function> {
    private readonly Function _function;

    public RuntimeImageTests(Function function) {
        _function = function;
    }

    private static string Envelope(string id, int quantity) => $$"""
        {"Records":[{
          "messageId":"m-{{id}}","receiptHandle":"r-{{id}}",
          "body":"{\"id\":\"{{id}}\",\"quantity\":{{quantity}}}",
          "eventSource":"aws:sqs",
          "eventSourceARN":"arn:aws:sqs:us-east-1:123456789012:orders-new",
          "awsRegion":"us-east-1"
        }]}
        """;

    /// <summary>
    /// The whole deployed path: the runtime client polls the emulator, the adapter recognises the
    /// envelope, the batch filter forks it, the binder binds the order, the handler runs and the
    /// batch report is what comes back. Nothing here is in-process.
    /// </summary>
    [Fact]
    public async Task AQueueMessageIsHandledInsideTheLambdaRuntimeImage() {
        var result = await _function.Emulator.InvokeAsync(Envelope("rie-1", 4), TestContext.Current.CancellationToken);

        Assert.False(result.Failed, result.Body);

        using var report = JsonDocument.Parse(result.Body);

        Assert.Empty(report.RootElement.GetProperty("batchItemFailures").EnumerateArray());

        var observed = await _function.Emulator.Observed.WaitFor(
            one => one.Has("id", "rie-1"), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("queue", observed.Get("kind"));
        Assert.Equal(4, observed.Fields.GetProperty("quantity").GetInt32());
    }

    /// <summary>
    /// A failed handler fails the invocation, which is what returns a message to the queue. The
    /// runtime has to post it to the Runtime API as an error, and the emulator has to report it the
    /// way the Invoke API does, or SQS would delete the message as handled.
    /// </summary>
    [Fact]
    public async Task AFailedHandlerFailsTheInvocation() {
        var result = await _function.Emulator.InvokeAsync(Envelope("rie-refused", -1), TestContext.Current.CancellationToken);

        Assert.True(result.Failed, result.Body);
        Assert.Contains("refused rie-refused", result.Body);

        var observed = await _function.Emulator.Observed.Current(TestContext.Current.CancellationToken);

        Assert.DoesNotContain(observed, one => one.Has("id", "rie-refused"));
    }

    public sealed class Function : IAsyncLifetime {
        public LambdaRuntimeInterfaceEmulator Emulator { get; } = new(
            ApplicationOutput.Of("Hardened.IntegrationTests.Rie.SUT"),
            "Hardened.IntegrationTests.Rie.SUT");

        public async ValueTask InitializeAsync() => await Emulator.StartAsync(TestContext.Current.CancellationToken);

        public async ValueTask DisposeAsync() => await Emulator.DisposeAsync();
    }
}
