using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Middleware;
using Hardened.Shared.Runtime.Application;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Hardened.Web.Kestrel.Runtime.Tests;

/// <summary>
/// TLS on the Kestrel host.
/// </summary>
/// <remarks>
/// Every <c>UseHttps</c> overload resolves services from
/// <c>KestrelServerOptions.ApplicationServices</c>, which is the application's provider. The
/// certificate overloads go through <c>EnableHttpsConfiguration</c>, which needs
/// <c>IHttpsConfigurationService</c> and <c>IHostEnvironment</c>. The options overload needs
/// <c>KestrelMetrics</c>. <c>[KestrelRuntime]</c> registers all three.
/// </remarks>
public class HttpsOverASocketTests
{
    [Fact]
    public async Task UseHttpsWithACertificateAnswersOverTls()
    {
        using var certificate = SelfSigned();

        await using var app = Build(listen => listen.UseHttps(certificate));

        await app.StartAsync(TestContext.Current.CancellationToken);

        Assert.Equal("https", await Get(app, certificate));
    }

    [Fact]
    public async Task UseHttpsWithOptionsAnswersOverTls()
    {
        using var certificate = SelfSigned();

        await using var app = Build(listen =>
            listen.UseHttps(new HttpsConnectionAdapterOptions { ServerCertificate = certificate })
        );

        await app.StartAsync(TestContext.Current.CancellationToken);

        Assert.Equal("https", await Get(app, certificate));
    }

    /// <summary>
    /// A generic host registers its own environment before the application's modules, and that one
    /// is the one Kestrel reads a relative certificate path against.
    /// </summary>
    [Fact]
    public void AHostEnvironmentAlreadyRegisteredIsKept()
    {
        var hosts = new HostingEnvironment
        {
            EnvironmentName = "Staging",
            ApplicationName = "Host",
            ContentRootPath = AppContext.BaseDirectory,
            ContentRootFileProvider = new NullFileProvider(),
        };

        var services = Services();

        services.AddSingleton<IHostEnvironment>(hosts);

        new KestrelRuntime().PopulateServiceCollection(services);

        using var provider = services.BuildServiceProvider();

        Assert.Same(hosts, provider.GetRequiredService<IHostEnvironment>());
    }

    [Fact]
    public void WithoutAHostTheEnvironmentIsTheApplications()
    {
        var services = Services();

        new KestrelRuntime().PopulateServiceCollection(services);

        using var provider = services.BuildServiceProvider();

        var environment = provider.GetRequiredService<IHostEnvironment>();

        Assert.Equal("test", environment.EnvironmentName);
        Assert.Equal(Directory.GetCurrentDirectory(), environment.ContentRootPath);
    }

    /// <summary>
    /// <c>UseKestrelCore</c> also registers a server, which would replace the one a test host or an
    /// ASP.NET Core host put there.
    /// </summary>
    [Fact]
    public void TheModuleRegistersNoServer()
    {
        var services = Services();

        new KestrelRuntime().PopulateServiceCollection(services);

        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IServer));
    }

    private static async Task<string> Get(HardenedKestrelApplication app, X509Certificate2 expected)
    {
        using var client = new HttpClient(
            new HttpClientHandler
            {
                // Accepts the one certificate the server was given, and no other.
                ServerCertificateCustomValidationCallback = (_, presented, _, _) =>
                    presented?.Thumbprint == expected.Thumbprint,
            }
        )
        {
            Timeout = TimeSpan.FromSeconds(10),
        };

        var address = new UriBuilder(app.Addresses.First()) { Scheme = "https" }.Uri;

        using var response = await client.GetAsync(address, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
    }

    private static HardenedKestrelApplication Build(Action<ListenOptions> https)
    {
        var services = Services();

        new KestrelRuntime().PopulateServiceCollection(services);

        // Port 0, so the OS picks one and concurrent test classes cannot collide.
        var app = HardenedKestrelApplication.Create(
            services,
            kestrel => kestrel.Listen(IPAddress.Loopback, 0, https)
        );

        app.Services.GetRequiredService<IMiddlewareService>().Use(_ => new AnsweringScheme());

        return app;
    }

    private static ServiceCollection Services()
    {
        var services = new ServiceCollection();

        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddTransient<IHardenedEnvironment>(_ => new EnvironmentImpl("test"));

        return services;
    }

    /// <summary>
    /// A certificate for this test alone.
    /// </summary>
    /// <remarks>
    /// Exported and loaded again, because Windows will not serve TLS with a key that exists only
    /// in memory, which is what <c>CreateSelfSigned</c> makes.
    /// </remarks>
    private static X509Certificate2 SelfSigned()
    {
        using var key = RSA.Create(2048);

        var request = new CertificateRequest(
            "CN=localhost",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1
        );

        using var created = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow.AddHours(1)
        );

        return new X509Certificate2(created.Export(X509ContentType.Pfx));
    }

    private sealed class AnsweringScheme : IExecutionFilter
    {
        public async Task Execute(IExecutionChain chain)
        {
            var response = chain.Context.Response;
            var bytes = Encoding.UTF8.GetBytes(
                chain.Context.Request.Transport.Get(KnownTransportKeys.UrlScheme) ?? ""
            );

            response.Status = 200;
            response.ContentType = "text/plain";
            response.Headers["Content-Length"] = bytes.Length.ToString();
            response.ShouldSerialize = false;

            await response.Body.WriteAsync(bytes, chain.Context.CancellationToken);
        }
    }
}
