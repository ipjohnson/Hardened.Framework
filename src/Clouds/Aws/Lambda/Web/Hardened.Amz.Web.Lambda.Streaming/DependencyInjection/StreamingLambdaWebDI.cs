using Hardened.Amz.Web.Lambda.Runtime.DependencyInjection;
using Hardened.Shared.Runtime.Application;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Amz.Web.Lambda.Streaming.DependencyInjection;

public static class StreamingLambdaWebDI {
    public static void Register(IHardenedEnvironment environment, IServiceCollection serviceCollection) {
        LambdaWebDI.Register(environment, serviceCollection);
        new StreamingLambdaWebModule().ConfigureServices(serviceCollection);
    }
}
