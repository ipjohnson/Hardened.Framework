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

    /// <summary>
    /// A path the emulator does not serve, used to prove that it is answering.
    /// </summary>
    /// <remarks>
    /// Anything except <c>/2015-03-31/functions/function/invocations</c>. A readiness probe must not
    /// be an invocation: the emulator reserves a single slot for one, and a probe would spend it.
    /// </remarks>
    private const string ReadinessPath = "/hardened-readiness-probe";

    /// <summary>
    /// How long the emulator gets to start answering. Bounded, so a container that never comes up
    /// fails its fixture rather than holding the run open.
    /// </summary>
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(2);

    /// <summary>
    /// How long one invocation gets. Generous, because the first one starts the runtime and the
    /// application inside the container: that costs 0.2 s with a full core and 20 s with a twentieth
    /// of one, so this is headroom rather than an expectation.
    /// </summary>
    private static readonly TimeSpan InvocationTimeout = TimeSpan.FromSeconds(120);

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
            //
            // An HTTP request rather than a TCP connect. Docker's port proxy accepts a connection
            // whatever the container is doing, so UntilExternalTcpPortIsAvailable was satisfied about
            // ten milliseconds after start at every CPU share from four cores down to a twentieth of
            // one - long before the emulator was listening. The first invocation then failed at the
            // transport, and the retry that covered it is what wedged the emulator; see InvokeAsync.
            //
            // Any status counts, because the point is that the emulator answered, and the path is one
            // it does not serve. It replies 404, and a future version replying something else would
            // still be answering.
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilHttpRequestIsSucceeded(
                    request => request.ForPort(Port).ForPath(ReadinessPath).ForStatusCodeMatching(_ => true),
                    strategy => strategy.WithTimeout(StartupTimeout)))
            .Build();
    }

    public ObservedInvocations Observed => new(_container);

    public Task StartAsync(CancellationToken cancellationToken = default) =>
        _container.StartAsync(cancellationToken);

    /// <summary>
    /// One invocation, as the Invoke API would report it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Sent once. An invocation is not safe to retry: the emulator reserves a single slot for the one
    /// it is serving, and a second arriving while that reservation is held fails it with
    /// <c>ReserveFailed: AlreadyReserved</c> and panics the emulator, after which nothing answers the
    /// first request either. That is what the retry this replaced did, because a transport error on
    /// the way back is no evidence the invocation was not accepted.
    /// </para>
    /// <para>
    /// The retry existed to cover a container that was not listening yet, which the wait strategy now
    /// establishes before any of this runs.
    /// </para>
    /// </remarks>
    public async Task<InvocationResult> InvokeAsync(string payload, CancellationToken cancellationToken = default) {
        var url = $"http://{_container.Hostname}:{_container.GetMappedPublicPort(Port)}/2015-03-31/functions/function/invocations";

        using var client = new HttpClient { Timeout = InvocationTimeout };

        HttpResponseMessage response;

        try {
            response = await client.PostAsync(
                url, new StringContent(payload, Encoding.UTF8, "application/json"), cancellationToken);
        }
        // The emulator accepted the request and never answered, which on its own reports as nothing
        // but elapsed time. The reason is in the container's log, and the usual one is the panic
        // above, so the log is what the failure carries. Not the caller's own cancellation, which is
        // the test being torn down and is not this class's to explain.
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested) {
            var (stdout, stderr) = await _container.GetLogsAsync(ct: CancellationToken.None);

            throw new TimeoutException(
                $"The emulator did not answer an invocation within {InvocationTimeout.TotalSeconds:0} s. " +
                $"It printed:\n--- stdout ---\n{stdout}\n--- stderr ---\n{stderr}", exception);
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
