using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using Hardened.Functions.Testing.Containers;
using Testcontainers.Azurite;
using Testcontainers.EventHubs;

namespace Hardened.IntegrationTests.AzureStream.Host.Tests;

/// <summary>
/// The Functions host image running the stream fixture, with the Event Hubs emulator beside it.
/// </summary>
/// <remarks>
/// <para>
/// Three containers on one network. Azurite is both the storage account the host requires and
/// the store the emulator keeps its metadata and the extension keeps its checkpoints in; the
/// Event Hubs emulator declares the one hub the fixture's handler names, with one partition so a
/// test's events arrive in the order they were sent; and the host image is the queue fixture's,
/// with the stream fixture's build output mounted where a deployment puts it.
/// </para>
/// <para>
/// The images, the waits and the host image being amd64 only are as the queue fixture's
/// <c>FunctionsHostSimulator</c> records.
/// </para>
/// </remarks>
public sealed class StreamHostSimulator : IAsyncDisposable {
    public const string HostImage = "mcr.microsoft.com/azure-functions/dotnet-isolated:4-dotnet-isolated8.0";

    public const string AzuriteImage = "mcr.microsoft.com/azure-storage/azurite:3.35.0";

    public const string EventHubsImage = "mcr.microsoft.com/azure-messaging/eventhubs-emulator:2.2.1";

    /// <summary>The hub the emulator's configuration declares and the fixture's handler names.</summary>
    public const string Hub = "clickstream";

    private const string AzuriteAlias = "azurite";
    private const string EventHubsAlias = "eventhubs";
    private const int HostPort = 80;

    private readonly INetwork _network;
    private readonly AzuriteContainer _azurite;
    private readonly EventHubsContainer _eventHubs;
    private readonly IContainer _host;

    public StreamHostSimulator(string outputDirectory) {
        _network = new NetworkBuilder().Build();

        _azurite = new AzuriteBuilder(AzuriteImage)
            .WithNetwork(_network)
            .WithNetworkAliases(AzuriteAlias)
            .Build();

        _eventHubs = new EventHubsBuilder(EventHubsImage)
            .WithAcceptLicenseAgreement(true)
            .WithAzuriteContainer(_network, _azurite, AzuriteAlias)
            .WithNetworkAliases(EventHubsAlias)
            .WithConfigurationBuilder(EventHubsServiceConfiguration.Create().WithEntity(Hub, 1))
            .Build();

        _host = new ContainerBuilder(HostImage)
            .WithNetwork(_network)
            .WithBindMount(outputDirectory, "/home/site/wwwroot", AccessMode.ReadOnly)
            .WithEnvironment("AzureWebJobsScriptRoot", "/home/site/wwwroot")
            .WithEnvironment("FUNCTIONS_WORKER_RUNTIME", "dotnet-isolated")
            .WithEnvironment("AzureFunctionsJobHost__Logging__Console__IsEnabled", "true")
            // The storage account the host keeps its locks in and the Event Hubs extension keeps
            // its checkpoints in, which is Azurite by its well-known development account.
            .WithEnvironment("AzureWebJobsStorage", AzuriteConnectionString())
            // The setting the Event Hubs binding reads when the attribute names no Connection:
            // the extension's default, AzureWebJobsEventHubs.
            .WithEnvironment("AzureWebJobsEventHubs", EventHubsConnectionString())
            .WithPortBinding(HostPort, assignRandomHostPort: true)
            .DependsOn(_azurite)
            .DependsOn(_eventHubs)
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilHttpRequestIsSucceeded(request => request.ForPort(HostPort).ForPath("/")))
            .Build();
    }

    public ObservedInvocations Observed => new(_host);

    /// <summary>The connection string a test publishes with, from outside the network.</summary>
    public string PublisherConnectionString => _eventHubs.GetConnectionString();

    public Task StartAsync(CancellationToken cancellationToken = default) =>
        _host.StartAsync(cancellationToken);

    public async Task<string> HostLog(CancellationToken cancellationToken = default) {
        var (stdout, stderr) = await _host.GetLogsAsync(ct: cancellationToken);

        return stdout + "\n" + stderr;
    }

    /// <summary>
    /// The host log once it contains <paramref name="fragment"/>, or a failure carrying the log.
    /// </summary>
    public async Task<string> HostLogContaining(
        string fragment, TimeSpan? timeout = null, CancellationToken cancellationToken = default) {
        var deadline = DateTime.UtcNow + (timeout ?? ObservedInvocations.DefaultTimeout);

        while (true) {
            var log = await HostLog(cancellationToken);

            if (log.Contains(fragment, StringComparison.Ordinal)) {
                return log;
            }

            if (DateTime.UtcNow > deadline) {
                throw new TimeoutException(
                    $"The host never logged '{fragment}'. It printed:\n{log}");
            }

            await Task.Delay(250, cancellationToken);
        }
    }

    private static string AzuriteConnectionString() =>
        "DefaultEndpointsProtocol=http;AccountName=" + AzuriteBuilder.AccountName +
        ";AccountKey=" + AzuriteBuilder.AccountKey +
        $";BlobEndpoint=http://{AzuriteAlias}:{AzuriteBuilder.BlobPort}/{AzuriteBuilder.AccountName}" +
        $";QueueEndpoint=http://{AzuriteAlias}:{AzuriteBuilder.QueuePort}/{AzuriteBuilder.AccountName}" +
        $";TableEndpoint=http://{AzuriteAlias}:{AzuriteBuilder.TablePort}/{AzuriteBuilder.AccountName};";

    private static string EventHubsConnectionString() =>
        $"Endpoint=sb://{EventHubsAlias}:{EventHubsBuilder.EventHubsPort};" +
        "SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;";

    public async ValueTask DisposeAsync() {
        await _host.DisposeAsync();
        await _eventHubs.DisposeAsync();
        await _azurite.DisposeAsync();
        await _network.DisposeAsync();
    }
}
