using System.Net;
using System.Net.Sockets;
using System.Text;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Middleware;
using Hardened.Shared.Testing.Attributes;
using Hardened.Web.AspNetCore.Runtime;
using Hardened.Web.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace Hardened.Web.AspNetCore.Testing.Tests;

/// <summary>
/// The ASP.NET Core host: the container it hands the runner, what it binds, what falls through
/// behind Hardened, and how it stops.
/// </summary>
public class AspNetCoreTestHostTests {

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static Task Answer(IExecutionChain chain, int status, string body) {
        var response = chain.Context.Response;

        response.Status = status;
        response.ContentType = "application/json";
        response.ShouldSerialize = false;

        return response.Body.WriteAsync(Encoding.UTF8.GetBytes(body), chain.Context.CancellationToken).AsTask();
    }

    private static TestHostRequest Get(string path) =>
        new("GET", path, new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase), Stream.Null, null);

    /// <summary>
    /// The container the test resolves from is the application's own: the server is in it, and
    /// the application's services are it.
    /// </summary>
    [Fact]
    public async Task TheContainerIsTheApplicationsOwn() {
        await using var harness = await HostHarness.Start(chain => Answer(chain, 200, "\"ok\""), Token);

        var host = Assert.IsType<AspNetCoreTestHost>(harness.Host);

        Assert.NotNull(harness.Provider.GetService<IServer>());
        Assert.Same(harness.Provider, host.Application.Services);
        Assert.False(host.IsTerminal);
    }

    [Fact]
    public async Task StartAsync_BindsALoopbackPortTheKernelPicked() {
        await using var harness = await HostHarness.Start(chain => Answer(chain, 200, "\"ok\""), Token);

        var address = harness.Host.BaseAddress;

        Assert.Equal("127.0.0.1", address.Host);
        Assert.NotEqual(0, address.Port);
    }

    [Fact]
    public async Task TheDefaultCompositionServesThroughHardened() {
        await using var harness = await HostHarness.Start(chain => Answer(chain, 201, "{\"id\":7}"), Token);

        var response = await harness.Host.SendAsync(Get("/things?culture=en-GB"), Token);

        var request = Assert.Single(harness.Requests);

        Assert.Equal("/things", request.Path);
        Assert.Equal(201, response.StatusCode);
        Assert.True(response.Headers.ContainsKey("Date"), "a header only a server writes");
        Assert.Equal(7, response.Deserialize<Thing>().Id);
        Assert.Equal(201, LastResponse.Status);
    }

    /// <summary>
    /// Not terminal: a path Hardened declares nothing for reaches nothing behind it in the
    /// default composition, and ASP.NET's own 404 answers, with no body.
    /// </summary>
    [Fact]
    public async Task AnUnmatchedPathFallsThroughToAspNetsOwn404() {
        await using var harness = await HostHarness.Start(chain => chain.Next(), Token);

        var response = await harness.Host.SendAsync(Get("/no/such/route"), Token);

        Assert.Equal(404, response.StatusCode);
        Assert.Equal(string.Empty, await response.ReadTextAsync());
    }

    /// <summary>The composition is the pipeline Program.cs has: what it puts behind Hardened answers what Hardened passed on.</summary>
    [Fact]
    public async Task ACompositionsMiddlewareBehindHardenedAnswersWhatItPassedOn() {
        await using var harness = await HostHarness.Start(chain => chain.Next(), Token, typeof(SomethingBehind));

        var response = await harness.Host.SendAsync(Get("/no/such/route"), Token);

        Assert.Equal(200, response.StatusCode);
        Assert.Equal("behind", await response.ReadTextAsync());
    }

    [Fact]
    public async Task TheEnvironmentNameIsTheHardenedOneInScope() {
        await using var plain = await HostHarness.Start(chain => chain.Next(), Token);
        await using var named = await HostHarness.Start(chain => chain.Next(), Token, null, new EnvironmentNameAttribute("development"));

        Assert.Equal("test", Assert.IsType<AspNetCoreTestHost>(plain.Host).Application.Environment.EnvironmentName);
        Assert.Equal("development", Assert.IsType<AspNetCoreTestHost>(named.Host).Application.Environment.EnvironmentName);
    }

    [Fact]
    public async Task DisposingTheContainerClosesThePort() {
        var harness = await HostHarness.Start(chain => Answer(chain, 200, "\"ok\""), Token);
        var port = harness.Host.BaseAddress.Port;

        await harness.DisposeAsync();

        using var probe = new TcpClient();

        await Assert.ThrowsAsync<SocketException>(() => probe.ConnectAsync(IPAddress.Loopback, port, Token).AsTask());
    }

    /// <summary>The attribute an application names its host with is the one a test names it with.</summary>
    [Fact]
    public void TheProviderAnswersForTheAspNetCoreRuntimeAttribute() {
        Assert.Equal(typeof(AspNetCoreRuntimeAttribute), new AspNetCoreTestingAttribute().RuntimeAttribute);
    }

    /// <summary>
    /// The runner asks the assembly's builder for every test's container, and a test whose
    /// narrowest host is another gets the plain container it would have had.
    /// </summary>
    [Fact]
    public void ForATestOnAnotherHostTheBuilderBuildsThePlainContainer() {
        var services = new ServiceCollection();

        services.AddSingleton<ITestHost>(_ => new PipelineHost(services.BuildServiceProvider()));

        var provider = new AspNetCoreTestingAttribute().BuildServiceProvider(null!, services);

        Assert.Null(provider.GetService<IServer>());
    }

    [Fact]
    public void WithNoHostAtAllTheBuilderNamesTheMissingAttribute() {
        var failure = Assert.Throws<InvalidOperationException>(() =>
            new AspNetCoreTestingAttribute().BuildServiceProvider(null!, new ServiceCollection()));

        Assert.Contains("[assembly: WebTesting]", failure.Message);
    }

    [Fact]
    public void ACompositionThatIsNotOneIsRefusedByName() {
        var failure = Assert.Throws<InvalidOperationException>(() =>
            new AspNetCoreTestingAttribute(typeof(string)).CreateHost(null!, new ServiceCollection()));

        Assert.Contains("System.String", failure.Message);
    }

    /// <summary>A composition with terminal middleware behind Hardened, the shape a Program.cs with static files or MVC behind it has.</summary>
    public sealed class SomethingBehind : IAspNetCoreTestComposition {
        public void Configure(WebApplicationBuilder builder) {
        }

        public void Configure(WebApplication app) {
            app.UseHardened();
            app.Run(context => context.Response.WriteAsync("behind"));
        }
    }

    private sealed record Thing(int Id);
}
