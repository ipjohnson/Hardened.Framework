using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using Hardened.Functions.Testing.Containers;
using Testcontainers.Azurite;
using Testcontainers.MsSql;
using Testcontainers.ServiceBus;

namespace Hardened.IntegrationTests.AzureQueue.Host.Tests;

/// <summary>
/// The Functions host image running the queue fixture, with the emulators it needs beside it.
/// </summary>
/// <remarks>
/// <para>
/// Four containers on one network. Azurite stands in for the storage account every Functions host
/// requires; the Service Bus emulator, with the MSSQL instance it keeps its state in, is the queue;
/// and the host image is <c>mcr.microsoft.com/azure-functions/dotnet-isolated:4-dotnet-isolated8.0</c>
/// with the fixture's build output mounted where a deployment puts it. The host reaches both
/// emulators by network alias, which is the first of the plan's three answers to the emulator's
/// bridged-network issue.
/// </para>
/// <para>
/// <b>The host image is published for amd64 only.</b> On an Apple Silicon machine Docker runs it
/// under emulation, after a <c>docker pull --platform linux/amd64</c>; CI runs it natively. The
/// waits below are generous for that reason.
/// </para>
/// <para>
/// A test observes the handler through the container's output: the fixture's
/// <c>ObservedOrderStore</c> prints one marked line per call, the host prints the worker's
/// output with its own logs, and <see cref="ObservedInvocations"/> reads them back.
/// </para>
/// </remarks>
public sealed class FunctionsHostSimulator : IAsyncDisposable {
    public const string HostImage = "mcr.microsoft.com/azure-functions/dotnet-isolated:4-dotnet-isolated8.0";

    public const string AzuriteImage = "mcr.microsoft.com/azure-storage/azurite:3.35.0";

    public const string ServiceBusImage = "mcr.microsoft.com/azure-messaging/servicebus-emulator:2.0.1";

    public const string SqlImage = "mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04";

    /// <summary>The queue the emulator's configuration declares and the fixture's handler names.</summary>
    public const string Queue = "orders";

    private const string AzuriteAlias = "azurite";
    private const string ServiceBusAlias = "servicebus";
    private const string SqlAlias = "mssql";
    private const int HostPort = 80;

    private readonly INetwork _network;
    private readonly AzuriteContainer _azurite;
    private readonly MsSqlContainer _sql;
    private readonly ServiceBusContainer _serviceBus;
    private readonly IContainer _host;

    public FunctionsHostSimulator(string outputDirectory) {
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
            // The storage account the host keeps its locks and secrets in, which is Azurite by
            // its well-known development account, reached by alias.
            .WithEnvironment("AzureWebJobsStorage", AzuriteConnectionString())
            // The setting the Service Bus binding reads when the attribute names no Connection:
            // the extension's default, AzureWebJobsServiceBus.
            .WithEnvironment("AzureWebJobsServiceBus", ServiceBusConnectionString())
            .WithPortBinding(HostPort, assignRandomHostPort: true)
            .DependsOn(_azurite)
            .DependsOn(_serviceBus)
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilHttpRequestIsSucceeded(
                    request => request.ForPort(HostPort).ForPath("/"),
                    strategy => strategy.WithTimeout(StartupTimeout)))
            .Build();
    }

    /// <summary>
    /// How long the host gets to answer on its port. Generous, because the image runs under
    /// emulation on an Apple Silicon machine; bounded, because a host that never comes up would
    /// otherwise hold the run and its emulators open indefinitely.
    /// </summary>
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

    /// <summary>Everything the host has logged, both streams.</summary>
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
