using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Runtime.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Requests.Runtime.DependencyInjection;

public class RequestRuntimeDI {
    public static void Register(IHardenedEnvironment environment, IServiceCollection serviceCollection) {
        if (DependencyRegistry<RequestRuntimeDI>.ShouldRegisterModule(serviceCollection)) {
            new HardenedRequestModule().ConfigureServices(serviceCollection);
        }
    }
}