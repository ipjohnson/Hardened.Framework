using Hardened.Shared.Runtime.Application;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Amz.Web.Lambda.Runtime.DependencyInjection;

public static class LambdaWebDI {
    public static void Register(IHardenedEnvironment environment, IServiceCollection serviceCollection) {
        new LambdaWebModule().ConfigureServices(serviceCollection);
    }
}