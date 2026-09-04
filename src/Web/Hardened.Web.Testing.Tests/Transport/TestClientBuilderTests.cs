using System.Reflection;
using DependencyModules.Testing.Attributes.Interfaces;
using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Testing.Impl;
using Hardened.Web.Testing.Tests.Conformance;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Hardened.Web.Testing.Tests.Transport;

/// <summary>A client an NSwag generator or a hand would write: one constructor, one HttpClient.</summary>
public sealed class ConventionClient {
    public ConventionClient(HttpClient http) {
        Http = http;
    }

    public HttpClient Http { get; }
}

/// <summary>A client shaped like Kiota's: the constructor takes something the harness has never heard of.</summary>
public sealed class AdaptedClient {
    public AdaptedClient(Func<HttpClient> adapter) {
        Http = adapter();
    }

    public HttpClient Http { get; }
}

public sealed class AdaptedClientFactory : ITestClientFactory<AdaptedClient> {
    public AdaptedClient Create(HttpClient http) => new(() => http);
}

/// <summary>Constructible by the container from what every provider answers for.</summary>
public sealed class ProviderClient {
    public ProviderClient(IServiceProvider provider) { }
}

/// <summary>Constructible through an open generic registration, which logging is.</summary>
public sealed class LoggingClient {
    public LoggingClient(ILogger<LoggingClient> logger) { }
}

/// <summary>Constructible because its one parameter has a default.</summary>
public sealed class DefaultedClient {
    public DefaultedClient(string endpoint = "http://harness") { }
}

/// <summary>Neither route: a constructor over something no factory supplies.</summary>
public sealed class OrphanClient {
    public OrphanClient(string endpoint) {
        Endpoint = endpoint;
    }

    public string Endpoint { get; }
}

/// <summary>Two constructors, one of them over an HttpClient, which is not the convention.</summary>
public sealed class AmbiguousClient {
    public AmbiguousClient(HttpClient http) { }

    public AmbiguousClient(HttpClient http, string baseUrl) { }
}

/// <summary>
/// The two construction routes, what each is found by, and the failure that names both.
/// </summary>
public class TestClientBuilderTests {

    private static readonly Assembly TestAssembly = typeof(TestClientBuilderTests).Assembly;

    [Fact]
    public void ASingleHttpClientConstructorIsARoute() {
        Assert.True(TestClientBuilder.HasRoute(typeof(ConventionClient), TestAssembly));

        using var http = new HttpClient();

        var client = (ConventionClient)TestClientBuilder.Build(typeof(ConventionClient), http, TestAssembly);

        Assert.Same(http, client.Http);
    }

    [Fact]
    public void APublicFactoryInTheTestAssemblyIsARoute() {
        Assert.True(TestClientBuilder.HasRoute(typeof(AdaptedClient), TestAssembly));

        using var http = new HttpClient();

        var client = (AdaptedClient)TestClientBuilder.Build(typeof(AdaptedClient), http, TestAssembly);

        Assert.Same(http, client.Http);
    }

    [Fact]
    public void TwoConstructorsAreNotTheConvention() {
        Assert.False(TestClientBuilder.HasRoute(typeof(AmbiguousClient), TestAssembly));
    }

    [Fact]
    public void NeitherRouteFailsNamingBoth() {
        Assert.False(TestClientBuilder.HasRoute(typeof(OrphanClient), TestAssembly));

        using var http = new HttpClient();

        var failure = Assert.Throws<InvalidOperationException>(
            () => TestClientBuilder.Build(typeof(OrphanClient), http, TestAssembly));

        Assert.Contains("ITestClientFactory<OrphanClient>", failure.Message);
        Assert.Contains("exactly one HttpClient", failure.Message);
        Assert.Contains(TestAssembly.GetName().Name!, failure.Message);
    }

    [Fact]
    public void TheHttpClientCarriesTheBaseAddressAndTheCredential() {
        var host = new PipelineHost();

        using var client = TestClientBuilder.CreateHttpClient(host.Provider, new TestCredential(new[] { "x" }, "pia"));

        Assert.Equal("http://harness/", client.BaseAddress!.ToString());
        Assert.Equal("x", client.DefaultRequestHeaders.GetValues("X-Test-Grants").Single());
    }

    [Theory]
    [InlineData(typeof(OrphanClient), false)]
    [InlineData(typeof(ITestClientFactory<ConventionClient>), false)]
    [InlineData(typeof(TestClientBuilderTests), true)]
    [InlineData(typeof(ConventionClient), false)]
    public void TheContainerCanBuildOnlyWhatItsRegistrationsSatisfy(Type type, bool constructible) {
        var services = new ServiceCollection();

        Assert.Equal(constructible, TestClientBuilder.IsConstructibleByTheContainer(type, services));
    }

    [Fact]
    public void ARegisteredConstructorParameterMakesATypeConstructible() {
        var services = new ServiceCollection();

        services.AddSingleton(new HttpClient());

        Assert.True(TestClientBuilder.IsConstructibleByTheContainer(typeof(ConventionClient), services));
    }

    /// <summary>
    /// The provider itself, an open generic registration and a defaulted parameter all count as
    /// satisfied, because the container satisfies them.
    /// </summary>
    [Fact]
    public void WhatEveryProviderAnswersForCountsAsRegistered() {
        var services = new ServiceCollection();

        services.AddLogging();

        Assert.True(TestClientBuilder.IsConstructibleByTheContainer(typeof(ProviderClient), services));
        Assert.True(TestClientBuilder.IsConstructibleByTheContainer(typeof(LoggingClient), services));
        Assert.True(TestClientBuilder.IsConstructibleByTheContainer(typeof(DefaultedClient), services));
    }

    /// <summary>
    /// What <see cref="WebTestingAttribute"/> registers for each parameter of a test method:
    /// clients over the pipeline for the two routes, and a failure naming both for neither.
    /// </summary>
    private sealed class Case : ITestMethodContext {
        public Case(string name) {
            Method = typeof(Case).GetMethod(name)!;
            Attributes = Method.GetCustomAttributes().ToList();
        }

        public MethodInfo Method { get; }

        public IReadOnlyList<Attribute> Attributes { get; }

        [Grants("method:grant")]
        public void Clients(ConventionClient convention, AdaptedClient adapted, OrphanClient orphan, HttpClient http, [Grants("own")] ConventionClient own) { }

        [Grants("method:grant")]
        public void Attributed([Grants("own")] HttpClient http, [Grants("own")] OrphanClient orphan, IServiceProvider provider, ProviderClient plain) { }
    }

    private static ServiceProvider ProviderFor(Case method) {
        var host = new PipelineHost();
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddSingleton<IApplicationRoot>(new ServiceProviderApplicationRoot(host.Provider));

        new WebTestingAttribute().SetupServiceCollection(method, services);

        return services.BuildServiceProvider();
    }

    [Fact]
    public void AConventionClientParameterIsRegisteredOverThePipeline() {
        using var provider = ProviderFor(new Case("Clients"));

        var client = provider.GetRequiredService<ConventionClient>();

        Assert.Equal("http://harness/", client.Http.BaseAddress!.ToString());
        Assert.Equal("method:grant", client.Http.DefaultRequestHeaders.GetValues("X-Test-Grants").Single());
    }

    [Fact]
    public void AFactoryClientParameterIsRegisteredThroughTheFactory() {
        using var provider = ProviderFor(new Case("Clients"));

        Assert.NotNull(provider.GetRequiredService<AdaptedClient>().Http.BaseAddress);
    }

    [Fact]
    public void AnHttpClientParameterIsTheHarnessesOwn() {
        using var provider = ProviderFor(new Case("Clients"));

        Assert.Equal("http://harness/", provider.GetRequiredService<HttpClient>().BaseAddress!.ToString());
    }

    [Fact]
    public void AParameterWithNeitherRouteFailsNamingBoth() {
        using var provider = ProviderFor(new Case("Clients"));

        var failure = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<OrphanClient>());

        Assert.Contains("ITestClientFactory<OrphanClient>", failure.Message);
        Assert.Contains("exactly one HttpClient", failure.Message);
    }

    /// <summary>
    /// A bare HttpClient parameter with its own credential is the harness's client carrying it, and
    /// a parameter with no route stands aside for ordinary resolution.
    /// </summary>
    [Fact]
    public async Task AnAttributedHttpClientIsBuiltAndANoRouteTypeStandsAside() {
        var method = new Case("Attributed");

        using var provider = ProviderFor(method);

        var parameters = method.Method.GetParameters();
        var httpAttribute = parameters[0].GetCustomAttributes().OfType<ITestParameterValueProvider>().Single();
        var orphanAttribute = parameters[1].GetCustomAttributes().OfType<ITestParameterValueProvider>().Single();

        var http = (HttpClient)(await httpAttribute.GetParameterValueAsync(method, provider, parameters[0]))!;

        Assert.Equal("own", http.DefaultRequestHeaders.GetValues("X-Test-Grants").Single());
        Assert.Null(await orphanAttribute.GetParameterValueAsync(method, provider, parameters[1]));
    }

    /// <summary>
    /// The provider itself and a type the container can build are left to ordinary resolution:
    /// nothing is registered for either.
    /// </summary>
    [Fact]
    public void TheProviderAndAConstructibleTypeAreLeftToTheContainer() {
        var host = new PipelineHost();
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddSingleton<IApplicationRoot>(new ServiceProviderApplicationRoot(host.Provider));

        new WebTestingAttribute().SetupServiceCollection(new Case("Attributed"), services);

        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IServiceProvider));
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(ProviderClient));
    }

    /// <summary>
    /// A parameter carrying its own credential is built by the attribute rather than resolved, so
    /// two parameters of one type are two clients with two credentials.
    /// </summary>
    [Fact]
    public async Task AParameterWithItsOwnCredentialIsBuiltWithIt() {
        var method = new Case("Clients");

        using var provider = ProviderFor(method);

        var parameter = method.Method.GetParameters()[4];
        var attribute = parameter.GetCustomAttributes().OfType<ITestParameterValueProvider>().Single();

        var own = (ConventionClient)(await attribute.GetParameterValueAsync(method, provider, parameter))!;
        var shared = provider.GetRequiredService<ConventionClient>();

        Assert.NotSame(shared, own);
        Assert.Equal("own", own.Http.DefaultRequestHeaders.GetValues("X-Test-Grants").Single());
        Assert.Equal("method:grant", shared.Http.DefaultRequestHeaders.GetValues("X-Test-Grants").Single());
    }
}
