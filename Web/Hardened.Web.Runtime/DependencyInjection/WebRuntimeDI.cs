using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Runtime.Configuration;
using Hardened.Web.Runtime.Configuration;
using Hardened.Web.Runtime.Handlers;
using Hardened.Web.Runtime.StaticContent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Hardened.Web.Runtime.DependencyInjection
{
    public static class WebRuntimeDI 
    {
        public static void Register(IEnvironment environment, IServiceCollection serviceCollection)
        {
            serviceCollection.TryAddSingleton<IWebExecutionHandlerService, WebExecutionHandlerService>();
            serviceCollection.TryAddSingleton<IStaticContentHandler, StaticContentHandler > ();
            serviceCollection.TryAddSingleton<IETagProvider, ETagProvider>();
            serviceCollection.TryAddSingleton<IGZipStaticContentCompressor, GZipStaticContentCompressor>();
            serviceCollection.TryAddSingleton<IConfigurationValueProvider>(
                new NewConfigurationValueProvider<IStaticContentConfiguration, StaticContentConfiguration>());
        }
    }
}
