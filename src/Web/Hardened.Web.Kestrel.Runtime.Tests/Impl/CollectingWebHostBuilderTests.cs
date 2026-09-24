using Hardened.Web.Kestrel.Runtime.Impl;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Hardened.Web.Kestrel.Runtime.Tests.Impl;

/// <summary>
/// The web host builder <c>HttpsServices</c> runs <c>UseKestrelCore</c> against. Only the plain
/// <c>ConfigureServices</c> is called by Kestrel today; the rest has to stay harmless if a later
/// Kestrel calls it.
/// </summary>
public class CollectingWebHostBuilderTests
{
    [Fact]
    public void ConfigureServicesRegistersIntoTheCollectionAtOnce()
    {
        var services = new ServiceCollection();
        var builder = new CollectingWebHostBuilder(services);

        Assert.Same(builder, builder.ConfigureServices(collection => collection.AddSingleton("a")));
        Assert.Same(
            builder,
            builder.ConfigureServices((_, collection) => collection.AddSingleton(new Version(1, 0)))
        );

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(string));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(Version));
    }

    [Fact]
    public void SettingsAndConfigurationAreNotKept()
    {
        var configured = false;
        var builder = new CollectingWebHostBuilder(new ServiceCollection());

        Assert.Same(builder, builder.UseSetting("environment", "Staging"));
        Assert.Null(builder.GetSetting("environment"));
        Assert.Same(builder, builder.ConfigureAppConfiguration((_, _) => configured = true));
        Assert.False(configured);
    }

    [Fact]
    public void ThereIsNoHostToBuild()
    {
        Assert.Throws<NotSupportedException>(() =>
            new CollectingWebHostBuilder(new ServiceCollection()).Build()
        );
    }
}
