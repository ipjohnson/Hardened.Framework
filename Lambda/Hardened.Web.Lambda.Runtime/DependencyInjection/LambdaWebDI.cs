using Hardened.Shared.Runtime.Application;
using Hardened.Web.Lambda.Runtime.Impl;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Hardened.Web.Lambda.Runtime.DependencyInjection
{
    public static class LambdaWebDI
    {
        public static void Register(IEnvironment environment, IServiceCollection serviceCollection)
        {
            serviceCollection.TryAddSingleton<IApiGatewayEventProcessor, ApiGatewayEventProcessor>();
        }
    }
}
