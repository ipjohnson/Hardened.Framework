using Hardened.Shared.Runtime.Application;
using Hardened.Web.Runtime.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Hardened.Web.Runtime.DependencyInjection
{
    public static class WebRuntimeDI 
    {
        public static void Register(IEnvironment environment, IServiceCollection serviceCollection)
        {
            serviceCollection.TryAddSingleton<IWebExecutionHandlerService, WebExecutionHandlerService>();
        }
    }
}
