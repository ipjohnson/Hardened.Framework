using System.Net.Http.Headers;
using System.Text;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using Hardened.Functions.Testing.Containers;

namespace Hardened.Gcp.CloudRun.Testing.Containers;

/// <summary>
/// A fixture application in the image Cloud Run would run it in, on the port Cloud Run would
/// name.
/// </summary>
/// <remarks>
/// <para>
/// <c>mcr.microsoft.com/dotnet/aspnet:8.0</c> with the application's build output mounted and
/// started as <c>dotnet &lt;assembly&gt;.dll</c>, with <c>PORT=8080</c> set the way Cloud Run
/// sets it. That is the whole of the container contract a service has to meet, and the fixture's
/// <c>Program.cs</c> is what meets it; nothing here compensates for it.
/// </para>
/// <para>
/// Reachable two ways: by alias on the test's network, where a push subscription's endpoint
/// points, and on a host port the kernel picked, for a test that posts to it directly the way
/// Scheduler, Eventarc or a caller would.
/// </para>
/// </remarks>
public sealed class CloudRunService : IAsyncDisposable {
    public const string Image = "mcr.microsoft.com/dotnet/aspnet:8.0";

    /// <summary>What the emulator names the service by on the network.</summary>
    public const string Alias = "service";

    public const int Port = 8080;

    private readonly IContainer _container;

    /// <param name="network">The network to join, or null for a service reached from the host alone.</param>
    /// <param name="outputDirectory">The fixture's build output, from <c>ApplicationOutput.Of</c>.</param>
    /// <param name="assembly">The fixture's assembly name, which is what <c>dotnet</c> runs.</param>
    public CloudRunService(INetwork? network, string outputDirectory, string assembly) {
        var builder = new ContainerBuilder(Image)
            .WithBindMount(outputDirectory, "/app", AccessMode.ReadOnly)
            .WithEnvironment("PORT", Port.ToString())
            .WithEntrypoint("dotnet")
            .WithCommand("/app/" + assembly + ".dll")
            .WithPortBinding(Port, assignRandomHostPort: true)
            // The line Program.cs prints once Kestrel has bound, so a request cannot arrive
            // before there is anything to receive it.
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("LISTENING"));

        if (network != null) {
            builder = builder.WithNetwork(network).WithNetworkAliases(Alias);
        }

        _container = builder.Build();
    }

    /// <summary>The service for <paramref name="projectName"/>, whose build output sits beside the test project.</summary>
    public static CloudRunService For(string projectName, INetwork? network = null) =>
        new(network, ApplicationOutput.Of(projectName), projectName);

    /// <summary>Where the emulator posts a push: the service by alias, inside the network.</summary>
    public string PushEndpoint => $"http://{Alias}:{Port}/";

    /// <summary>The service from the host, on the port the kernel picked.</summary>
    public Uri HostAddress => new($"http://{_container.Hostname}:{_container.GetMappedPublicPort(Port)}/");

    public ObservedInvocations Observed => new(_container);

    public Task StartAsync(CancellationToken cancellationToken = default) =>
        _container.StartAsync(cancellationToken);

    /// <summary>
    /// What Cloud Run does to shut an instance down: <c>SIGTERM</c>, then <c>SIGKILL</c> ten
    /// seconds later, which is Docker's own stop timeout as well.
    /// </summary>
    public Task StopAsync(CancellationToken cancellationToken = default) =>
        _container.StopAsync(cancellationToken);

    public Task<long> ExitCodeAsync(CancellationToken cancellationToken = default) =>
        _container.GetExitCodeAsync(cancellationToken);

    /// <summary>
    /// One request from the host to the service, the way a source outside the container sends it.
    /// </summary>
    public async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string path, string? body, string contentType, CancellationToken cancellationToken = default,
        params (string Name, string Value)[] headers) {
        using var client = new HttpClient { BaseAddress = HostAddress, Timeout = TimeSpan.FromSeconds(60) };
        using var request = new HttpRequestMessage(method, path);

        if (body != null) {
            request.Content = new StringContent(body, Encoding.UTF8);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        }

        foreach (var (name, value) in headers) {
            request.Headers.TryAddWithoutValidation(name, value);
        }

        return await client.SendAsync(request, cancellationToken);
    }

    public ValueTask DisposeAsync() => _container.DisposeAsync();
}
