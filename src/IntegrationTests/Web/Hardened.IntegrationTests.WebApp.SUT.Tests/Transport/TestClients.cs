using System.Net.Http.Json;
using Hardened.IntegrationTests.WebApp.SUT.Models;

namespace Hardened.IntegrationTests.WebApp.SUT.Tests.Transport;

/// <summary>
/// The shape an NSwag generator or a hand writes: one constructor over an <see cref="HttpClient"/>.
/// Built by convention, with no factory.
/// </summary>
public sealed class ProbeClient {
    public ProbeClient(HttpClient http) {
        Http = http;
    }

    public HttpClient Http { get; }

    public Task<HttpResponseMessage> Pets(CancellationToken cancellationToken) =>
        Http.GetAsync("authorization/pets", cancellationToken);

    public async Task<int> Add(MathAddModel model, CancellationToken cancellationToken) {
        using var response = await Http.PostAsJsonAsync("int/add", model, cancellationToken);

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<int>(cancellationToken);
    }
}

/// <summary>
/// The shape Kiota writes: the constructor takes an adapter the harness has never heard of, so
/// the test project says how to build one, once.
/// </summary>
public sealed class AdaptedClient {
    public AdaptedClient(Func<Uri, HttpClient> adapter) {
        Http = adapter(new Uri("http://harness"));
    }

    public HttpClient Http { get; }

    public Task<HttpResponseMessage> Pets(CancellationToken cancellationToken) =>
        Http.GetAsync("authorization/pets", cancellationToken);
}

public sealed class AdaptedClientFactory : ITestClientFactory<AdaptedClient> {
    public AdaptedClient Create(HttpClient http) => new(_ => http);
}

/// <summary>None of the routes: nothing the harness can build it from.</summary>
public sealed class OrphanClient {
    public OrphanClient(string endpoint) {
        Endpoint = endpoint;
    }

    public string Endpoint { get; }
}
