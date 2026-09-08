using System.Text.Json;
using Hardened.Functions.Testing.Containers;
using Xunit;

namespace Hardened.IntegrationTests.Rie.Tests;

/// <summary>
/// The direct invoke fixture inside the Lambda base image: the caller's own payload in, the
/// handler's own answer out, routed on the function name the runtime was given.
/// </summary>
[Trait("Category", "Simulator")]
public sealed class DirectInvokeImageTests : IClassFixture<DirectInvokeImageTests.Function> {
    private readonly Function _function;

    public DirectInvokeImageTests(Function function) {
        _function = function;
    }

    [Fact]
    public async Task ThePayloadIsBoundAndTheReceiptIsTheResponse() {
        var result = await _function.Emulator.InvokeAsync(
            """{"id":"i-1","quantity":2}""", TestContext.Current.CancellationToken);

        Assert.False(result.Failed, result.Body);

        using var receipt = JsonDocument.Parse(result.Body);

        Assert.Equal("i-1", receipt.RootElement.GetProperty("id").GetString());
        Assert.Equal("placed", receipt.RootElement.GetProperty("status").GetString());

        var observed = await _function.Emulator.Observed.WaitFor(
            one => one.Has("id", "i-1"), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, observed.Fields.GetProperty("quantity").GetInt32());
    }

    public sealed class Function : IAsyncLifetime {
        // The function name is the route: [HardenedFunction] with no name registers under the
        // method's, and the invoke adapter routes on what AWS_LAMBDA_FUNCTION_NAME says.
        public LambdaRuntimeInterfaceEmulator Emulator { get; } = new(
            ApplicationOutput.Of("Hardened.IntegrationTests.RieInvoke.SUT"),
            "Hardened.IntegrationTests.RieInvoke.SUT",
            functionName: "Handle");

        public async ValueTask InitializeAsync() => await Emulator.StartAsync(TestContext.Current.CancellationToken);

        public async ValueTask DisposeAsync() => await Emulator.DisposeAsync();
    }
}
