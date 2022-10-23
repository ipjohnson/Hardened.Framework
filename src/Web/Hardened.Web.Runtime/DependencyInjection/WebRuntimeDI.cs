using System.Security.Cryptography;
using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Runtime.Collections;
using Hardened.Shared.Runtime.Configuration;
using Hardened.Web.Runtime.Configuration;
using Hardened.Web.Runtime.Handlers;
using Hardened.Web.Runtime.StaticContent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Hardened.Web.Runtime.DependencyInjection;

public static class WebRuntimeDI
{
    private static readonly WeakReference<IServiceCollection?> _lastServiceCollection = new(null);

    public static void Register(IEnvironment environment, IServiceCollection serviceCollection)
    {
        if (!_lastServiceCollection.TryGetTarget(out var lastServiceCollection) ||
            !ReferenceEquals(lastServiceCollection, serviceCollection))
        {
            _lastServiceCollection.SetTarget(serviceCollection);

            serviceCollection.TryAddSingleton<IWebExecutionHandlerService, WebExecutionHandlerService>();
            serviceCollection.TryAddSingleton<IStaticContentHandler, StaticContentHandler>();
            serviceCollection.TryAddSingleton<IETagProvider, ETagProvider>();
            serviceCollection.TryAddSingleton<IGZipStaticContentCompressor, GZipStaticContentCompressor>();
            serviceCollection.AddSingleton<IConfigurationPackage>(
                new SimpleConfigurationPackage(
                    new[]
                    {
                        new NewConfigurationValueProvider<IStaticContentConfiguration, StaticContentConfiguration>(null)
                    }, Array.Empty<IConfigurationValueAmender>())
            );
            serviceCollection.TryAddSingleton(
                serviceProvider => Microsoft.Extensions.Options.Options.Create(
                    serviceProvider.GetRequiredService<IConfigurationManager>()
                        .GetConfiguration<IStaticContentConfiguration>()));
        }
    }
}