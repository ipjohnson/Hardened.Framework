using System.Text;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using Hardened.Functions.Testing.Containers;

namespace Hardened.IntegrationTests.Rie.Tests;

/// <summary>
/// A function in the Lambda base image, invoked through the Runtime Interface Emulator it ships.
/// </summary>
/// <remarks>
/// <para>
/// The image is the managed dotnet8 runtime AWS runs, with the emulator in front of it as a stand-in
/// for the Lambda service: <c>POST /2015-03-31/functions/function/invocations</c> is one invocation,
/// and the response is what the function posted back, or the error document when it failed. The
/// command is the handler string a deployment would register - the assembly name alone, which the
/// runtime starts as an executable.
/// </para>
/// <para>
/// This is the AWS column of the simulator matrix, and it is honest about what it is: the envelope
/// is the recorded wire shape, because SQS itself is not in the loop. What the tier adds over the
/// envelope tests is the real image, the real runtime client and the real Runtime API exchange.
/// </para>
/// </remarks>
public sealed class LambdaRuntimeInterfaceEmulator : IAsyncDisposable {
    public const string Image = "public.ecr.aws/lambda/dotnet:8";

    private const int Port = 8080;

    private readonly IContainer _container;

    /// <param name="outputDirectory">The function's build output, mounted at <c>/var/task</c>.</param>
    /// <param name="handler">The handler string a deployment would register: the assembly name.</param>
    /// <param name="functionName">
    /// What <c>AWS_LAMBDA_FUNCTION_NAME</c> says, which the invoke adapter routes on. The emulator's
    /// own default is <c>test_function</c>, which no handler in this repository is named.
    /// </param>
    public LambdaRuntimeInterfaceEmulator(string outputDirectory, string handler, string functionName = "orders-function") {
        _container = new ContainerBuilder(Image)
            .WithBindMount(outputDirectory, "/var/task", AccessMode.ReadOnly)
            .WithCommand(handler)
            .WithEnvironment("AWS_LAMBDA_FUNCTION_NAME", functionName)
            .WithPortBinding(Port, assignRandomHostPort: true)
            // From the host rather than inside the container: the Lambda image is minimal and an
            // in-container probe depends on tools it may not carry.
            .WithWaitStrategy(Wait.ForUnixContainer().UntilExternalTcpPortIsAvailable(Port))
            .Build();
    }

    public ObservedInvocations Observed => new(_container);

    public Task StartAsync(CancellationToken cancellationToken = default) =>
        _container.StartAsync(cancellationToken);

    /// <summary>
    /// One invocation, as the Invoke API would report it.
    /// </summary>
    public async Task<InvocationResult> InvokeAsync(string payload, CancellationToken cancellationToken = default) {
        var url = $"http://{_container.Hostname}:{_container.GetMappedPublicPort(Port)}/2015-03-31/functions/function/invocations";

        // Generous, because the first invocation starts the runtime and the application inside it.
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };

        // Docker's port proxy accepts a connection before the emulator behind it listens, so the
        // first request after start can be refused or reset. A few attempts, spaced out, is the
        // whole of the cold-start handling; a real failure still surfaces after the last one.
        HttpResponseMessage response = null!;

        for (var attempt = 1; ; attempt++) {
            try {
                response = await client.PostAsync(
                    url, new StringContent(payload, Encoding.UTF8, "application/json"), cancellationToken);

                break;
            }
            catch (HttpRequestException) when (attempt < 5) {
                await Task.Delay(TimeSpan.FromSeconds(attempt), cancellationToken);
            }
        }

        using var _ = response;

        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        // The Invoke API reports a failed function with a 200 and X-Amz-Function-Error; the emulator
        // does the same, and its error document carries errorType and errorMessage either way.
        var failed = response.Headers.Contains("X-Amz-Function-Error") ||
                     (body.Contains("\"errorType\"") && body.Contains("\"errorMessage\""));

        return new InvocationResult(failed, (int)response.StatusCode, body);
    }

    public sealed record InvocationResult(bool Failed, int StatusCode, string Body);

    public ValueTask DisposeAsync() => _container.DisposeAsync();
}
