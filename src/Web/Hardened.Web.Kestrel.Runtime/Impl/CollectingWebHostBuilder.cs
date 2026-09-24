using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Web.Kestrel.Runtime.Impl;

/// <summary>
/// Applies <c>ConfigureServices</c> to a collection straight away, so an extension written for a
/// web host can register into it.
/// </summary>
/// <remarks>
/// <see cref="HttpsServices"/> runs <c>UseKestrelCore</c> against one. Everything else a web host
/// builder does is left undone: settings read as unset and are not kept, configuration is not
/// built, and there is no host to build.
/// </remarks>
internal sealed class CollectingWebHostBuilder(IServiceCollection services) : IWebHostBuilder
{
    public IWebHost Build() =>
        throw new NotSupportedException("This builder only collects registrations.");

    public IWebHostBuilder ConfigureAppConfiguration(
        Action<WebHostBuilderContext, IConfigurationBuilder> configureDelegate
    ) => this;

    public IWebHostBuilder ConfigureServices(Action<IServiceCollection> configureServices)
    {
        configureServices(services);

        return this;
    }

    public IWebHostBuilder ConfigureServices(
        Action<WebHostBuilderContext, IServiceCollection> configureServices
    )
    {
        configureServices(new WebHostBuilderContext(), services);

        return this;
    }

    public string? GetSetting(string key) => null;

    public IWebHostBuilder UseSetting(string key, string? value) => this;
}
