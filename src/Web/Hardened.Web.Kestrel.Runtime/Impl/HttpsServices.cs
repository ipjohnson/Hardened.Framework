using System.Reflection;
using Hardened.Shared.Runtime.Application;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;

namespace Hardened.Web.Kestrel.Runtime.Impl;

/// <summary>
/// What <c>ListenOptions.UseHttps</c> resolves from <c>KestrelServerOptions.ApplicationServices</c>,
/// which <see cref="KestrelServerRunner"/> sets to the application's provider.
/// </summary>
/// <remarks>
/// <para>
/// The certificate overloads resolve <c>IHttpsConfigurationService</c>, <c>IHostEnvironment</c> and
/// two loggers through <c>KestrelServerOptions.EnableHttpsConfiguration</c>. The options overloads
/// resolve <c>KestrelMetrics</c>, which needs <c>IMeterFactory</c>. The <c>KestrelServer</c>
/// constructor the runner calls creates its own HTTPS configuration and metrics, which the
/// extensions do not see.
/// </para>
/// <para>
/// <c>IHttpsConfigurationService</c> and <c>KestrelMetrics</c> are internal to Kestrel, and
/// <c>IWebHostBuilder.UseKestrelCore</c> is the only public code that registers them. It runs here
/// against a collection of its own, and only its registrations of internal types are kept. The
/// server, transport and options setup it also registers are public types and are left out: the
/// runner builds its own, and an <c>IServer</c> here would replace the one an ASP.NET Core host or
/// a test host registered.
/// </para>
/// </remarks>
internal static class HttpsServices
{
    public static void Add(IServiceCollection services)
    {
        var kestrel = new ServiceCollection();

        new CollectingWebHostBuilder(kestrel).UseKestrelCore();

        foreach (var descriptor in kestrel)
        {
            if (!descriptor.ServiceType.IsVisible)
            {
                services.TryAdd(descriptor);
            }
        }

        services.AddMetrics();

        // TryAdd, because a generic host registers its own before any module runs.
        services.TryAddSingleton(provider =>
            DefaultHostEnvironment(provider.GetRequiredService<IHardenedEnvironment>())
        );
    }

    /// <summary>
    /// The environment a host would have registered, for an application that runs without one.
    /// </summary>
    /// <remarks>
    /// Kestrel resolves a relative certificate path against <c>ContentRootPath</c>. The current
    /// directory is the root <c>WebApplication.CreateBuilder</c> and
    /// <c>Host.CreateApplicationBuilder</c> use.
    /// </remarks>
    private static IHostEnvironment DefaultHostEnvironment(IHardenedEnvironment environment)
    {
        var contentRoot = Directory.GetCurrentDirectory();

        return new HostingEnvironment
        {
            EnvironmentName = environment.Name,
            ApplicationName = Assembly.GetEntryAssembly()?.GetName().Name ?? "",
            ContentRootPath = contentRoot,
            ContentRootFileProvider = new PhysicalFileProvider(contentRoot),
        };
    }
}
