using Hardened.Amz.Function.Lambda.Runtime.DependencyInjection;
using Hardened.Shared.Runtime.Application;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Amz.Function.Lambda.Streaming.DependencyInjection;

public static class StreamingLambdaFunctionDI {
    public static void Register(IHardenedEnvironment environment, IServiceCollection serviceCollection) {
        LambdaFunctionDI.Register(environment, serviceCollection);
        new StreamingLambdaFunctionModule().ConfigureServices(serviceCollection);
    }
}
