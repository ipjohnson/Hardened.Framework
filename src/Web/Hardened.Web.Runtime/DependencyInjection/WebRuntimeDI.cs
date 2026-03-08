using Hardened.Requests.Runtime.DependencyInjection;
using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Runtime.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Web.Runtime.DependencyInjection;

public class WebRuntimeDI {
    public static void Register(IHardenedEnvironment environment, IServiceCollection serviceCollection) {
        if (DependencyRegistry<WebRuntimeDI>.ShouldRegisterModule(serviceCollection)) {
            RequestRuntimeDI.Register(environment, serviceCollection);
            new HardenedWebModule().ConfigureServices(serviceCollection);
        }
    }
}