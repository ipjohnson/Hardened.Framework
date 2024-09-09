using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Runtime.DependencyInjection;
using System.Security.Cryptography;
using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Runtime.Collections;
using Hardened.Shared.Runtime.Configuration;
using Hardened.Shared.Runtime.DependencyInjection;
using Hardened.Web.Runtime.Configuration;
using Hardened.Web.Runtime.Handlers;
using Hardened.Web.Runtime.StaticContent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Hardened.Web.Runtime.DependencyInjection;

public class WebRuntimeDI {
    public static void Register(IHardenedEnvironment environment, IServiceCollection serviceCollection) {
        if (DependencyRegistry<WebRuntimeDI>.ShouldRegisterModule(serviceCollection)) {
            RequestRuntimeDI.Register(environment, serviceCollection);
            
            serviceCollection.TryAddSingleton<IWebExecutionHandlerService, WebExecutionHandlerService>();
            serviceCollection.TryAddSingleton<IStaticContentHandler, StaticContentHandler>();
            serviceCollection.TryAddSingleton<IETagProvider, ETagProvider>();
            serviceCollection.TryAddSingleton<IGZipStaticContentCompressor, GZipStaticContentCompressor>();
            serviceCollection.AddSingleton<IConfigurationPackage>(
                new SimpleConfigurationPackage(
                    new[] {
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