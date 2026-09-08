using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using Hardened.Functions.Testing.Containers;
using Testcontainers.Azurite;
using Testcontainers.MsSql;
using Testcontainers.ServiceBus;

namespace Hardened.IntegrationTests.AzureEvents.Host.Tests;

/// <summary>
/// The Functions host image running the events fixture, with the emulators it needs beside it.
/// </summary>
/// <remarks>
/// <para>
/// The queue fixture's simulator with a topic beside the queue: the Service Bus emulator's
/// configuration declares <c>order-events</c> with the subscription the application named, so a
/// message published to the topic reaches the subscription function the generator wrote. The
/// schedule the timer function reads is an app setting, supplied here as the environment
/// variable configuration maps onto it, set to a moment the test never reaches. Event Grid has no
/// emulator; its function is indexed and left alone.
/// </para>
/// <para>
/// Everything else - the images, the network, the waits, and the host image being amd64 only -
/// is as the queue fixture's <c>FunctionsHostSimulator</c> records.
/// </para>
/// </remarks>
public sealed class EventsHostSimulator : IAsyncDisposable {
    public const string HostImage = "mcr.microsoft.com/azure-functions/dotnet-isolated:4-dotnet-isolated8.0";

    public const string AzuriteImage = "mcr.microsoft.com/azure-storage/azurite:3.35.0";

    public const string ServiceBusImage = "mcr.microsoft.com/azure-messaging/servicebus-emulator:2.0.1";

    public const string SqlImage = "mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04";

    /// <summary>The queue, topic and subscription the emulator's configuration declares.</summary>
    public const string Queue = "orders-new";

    public const string Topic = "order-events";

    public const string Subscription = "events-function";

    private const string AzuriteAlias = "azurite";
    private const string ServiceBusAlias = "servicebus";
    private const string SqlAlias = "mssql";
    private const int HostPort = 80;

    private readonly INetwork _network;
    private readonly AzuriteContainer _azurite;
    private readonly MsSqlContainer _sql;
    private readonly ServiceBusContainer _serviceBus;
    private readonly IContainer _host;

    public EventsHostSimulator(string outputDirectory) {
        _network = new NetworkBuilder().Build();

        _azurite = new AzuriteBuilder(AzuriteImage)
            .WithNetwork(_network)
            .WithNetworkAliases(AzuriteAlias)
            .Build();

        _sql = new MsSqlBuilder(SqlImage)
            .WithNetwork(_network)
            .WithNetworkAliases(SqlAlias)
            .Build();

        _serviceBus = new ServiceBusBuilder(ServiceBusImage)
            .WithAcceptLicenseAgreement(true)
            .WithMsSqlContainer(_network, _sql, SqlAlias)
            .WithNetworkAliases(ServiceBusAlias)
            .WithConfig(Path.Combine(AppContext.BaseDirectory, "ServiceBusEmulator.json"))
            .Build();

        _host = new ContainerBuilder(HostImage)
            .WithNetwork(_network)
            .WithBindMount(outputDirectory, "/home/site/wwwroot", AccessMode.ReadOnly)
            .WithEnvironment("AzureWebJobsScriptRoot", "/home/site/wwwroot")
            .WithEnvironment("FUNCTIONS_WORKER_RUNTIME", "dotnet-isolated")
            .WithEnvironment("AzureFunctionsJobHost__Logging__Console__IsEnabled", "true")
            .WithEnvironment("AzureWebJobsStorage", AzuriteConnectionString())
            .WithEnvironment("AzureWebJobsServiceBus", ServiceBusConnectionString())
            // The timer function's schedule, %Hardened:Timers:nightly-rollup%, resolved from
            // configuration, where a double underscore is the section separator: midnight on the
            // first of January, so the schedule indexes and never fires during a test.
            .WithEnvironment("Hardened__Timers__nightly-rollup", "0 0 0 1 1 *")
            .WithPortBinding(HostPort, assignRandomHostPort: true)
            .DependsOn(_azurite)
            .DependsOn(_serviceBus)
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilHttpRequestIsSucceeded(
                    request => request.ForPort(HostPort).ForPath("/"),
                    strategy => strategy.WithTimeout(StartupTimeout)))
            .Build();
    }

    /// <summary>Bounded for the reason the queue fixture's simulator gives.</summary>
    public static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(5);

    public ObservedInvocations Observed => new(_host);

    /// <summary>The connection string a test publishes with, from outside the network.</summary>
    public string PublisherConnectionString => _serviceBus.GetConnectionString();

    /// <summary>Starts the host and its dependencies, or fails carrying what the host printed.</summary>
    public async Task StartAsync(CancellationToken cancellationToken = default) {
        try {
            await _host.StartAsync(cancellationToken);
        }
        catch (TimeoutException timeout) {
            throw new TimeoutException(
                $"The host did not answer on its port within {StartupTimeout.TotalMinutes:0} minutes. It printed:\n" +
                await HostLog(cancellationToken), timeout);
        }
    }

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

    private static string ServiceBusConnectionString() =>
        $"Endpoint=sb://{ServiceBusAlias}:{ServiceBusBuilder.ServiceBusPort};" +
        "SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;";

    public async ValueTask DisposeAsync() {
        await _host.DisposeAsync();
        await _serviceBus.DisposeAsync();
        await _sql.DisposeAsync();
        await _azurite.DisposeAsync();
        await _network.DisposeAsync();
    }
}
