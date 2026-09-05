using System.Reflection;
using DependencyModules.Testing.Attributes.Interfaces;
using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Testing.Impl;
using Hardened.Web.Testing;
using Hardened.Web.Testing.Tests.Conformance;
using Hardened.Web.Testing.Tests.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

[assembly: TestClientRoute(typeof(RoutedClientRoute))]

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

/// <summary>A whole shape of client, which is what a route answers for rather than one type.</summary>
public class RoutedClient {
    public RoutedClient(HttpClient http, string marker) {
        Http = http;
        Marker = marker;
    }

    public HttpClient Http { get; }

    public string Marker { get; }
}

/// <summary>A second one, built by the same route with nothing written per client.</summary>
public sealed class AnotherRoutedClient : RoutedClient {
    public AnotherRoutedClient(HttpClient http, string marker) : base(http, marker) { }
}

public sealed class RoutedClientRoute : ITestClientRoute {
    public bool CanBuild(Type clientType) => typeof(RoutedClient).IsAssignableFrom(clientType);

    public object Build(TestClientContext context, Type clientType) =>
        Activator.CreateInstance(clientType, context.Http, "routed")!;
}

/// <summary>A client an explicit factory claims, so the route never sees it.</summary>
public sealed class ClaimedClient : RoutedClient {
    public ClaimedClient(HttpClient http, string marker) : base(http, marker) { }
}

public sealed class ClaimedClientFactory : ITestClientFactory<ClaimedClient> {
    public ClaimedClient Create(HttpClient http) => new(http, "factory");
}

/// <summary>None of the three: a constructor over something no factory supplies.</summary>
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

        var context = Context();

        var client = (ConventionClient)TestClientBuilder.Build(typeof(ConventionClient), context, TestAssembly);

        Assert.Same(context.Http, client.Http);
    }

    [Fact]
    public void APublicFactoryInTheTestAssemblyIsARoute() {
        Assert.True(TestClientBuilder.HasRoute(typeof(AdaptedClient), TestAssembly));

        var context = Context();

        var client = (AdaptedClient)TestClientBuilder.Build(typeof(AdaptedClient), context, TestAssembly);

        Assert.Same(context.Http, client.Http);
    }

    [Fact]
    public void TwoConstructorsAreNotTheConvention() {
        Assert.False(TestClientBuilder.HasRoute(typeof(AmbiguousClient), TestAssembly));
    }

    /// <summary>
    /// The route answers for a shape, so a second client of that shape costs nothing.
    /// </summary>
    [Fact]
    public void ARouteTheAssemblyNamedIsARoute() {
        Assert.True(TestClientBuilder.HasRoute(typeof(RoutedClient), TestAssembly));
        Assert.True(TestClientBuilder.HasRoute(typeof(AnotherRoutedClient), TestAssembly));

        var context = Context();

        var client = (AnotherRoutedClient)TestClientBuilder.Build(
            typeof(AnotherRoutedClient), context, TestAssembly);

        Assert.Same(context.Http, client.Http);
        Assert.Equal("routed", client.Marker);
    }

    /// <summary>
    /// A test project that wants one client built its own way says so without giving up the route
    /// for the rest.
    /// </summary>
    [Fact]
    public void AFactoryWinsOverARouteThatWouldAlsoBuildIt() {
        var context = Context();

        var client = (ClaimedClient)TestClientBuilder.Build(typeof(ClaimedClient), context, TestAssembly);

        Assert.Equal("factory", client.Marker);
    }

    [Fact]
    public void NoRouteFailsNamingAllThree() {
        Assert.False(TestClientBuilder.HasRoute(typeof(OrphanClient), TestAssembly));

        var context = Context();

        var failure = Assert.Throws<InvalidOperationException>(
            () => TestClientBuilder.Build(typeof(OrphanClient), context, TestAssembly));

        Assert.Contains("ITestClientFactory<OrphanClient>", failure.Message);
        Assert.Contains("exactly one HttpClient", failure.Message);
        Assert.Contains(nameof(RoutedClientRoute), failure.Message);
        Assert.Contains(TestAssembly.GetName().Name!, failure.Message);
    }

    /// <summary>
    /// A handler of the caller's own runs in front of the pipeline, which is what a client testing
    /// package needs to see the responses its client swallows.
    /// </summary>
    [Fact]
    public async Task ARouteMayPutItsOwnHandlersInFrontOfThePipeline() {
        var host = new PipelineHost();
        var seen = new CountingHandler();

        var context = TestClientBuilder.CreateContext(host.Provider, null);

        using var http = context.CreateHttpClient(seen);

        Assert.Equal("http://harness/", http.BaseAddress!.ToString());

        await http.GetAsync("/anything", Xunit.TestContext.Current.CancellationToken);

        Assert.Equal(1, seen.Calls);
    }

    private sealed class CountingHandler : DelegatingHandler {
        public int Calls;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) {
            Calls++;

            return base.SendAsync(request, cancellationToken);
        }
    }

    private static TestClientContext Context() =>
        TestClientBuilder.CreateContext(new PipelineHost().Provider, null);

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
