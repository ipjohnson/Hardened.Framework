using Hardened.Shared.Runtime.Application;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Amz.Function.Lambda.Runtime.DependencyInjection;

public static class LambdaFunctionDI {
    public static void Register(IHardenedEnvironment environment, IServiceCollection serviceCollection) {
        new LambdaFunctionModule().ConfigureServices(serviceCollection);
    }
}