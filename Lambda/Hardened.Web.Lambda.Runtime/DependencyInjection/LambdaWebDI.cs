using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Runtime.Metrics;
using Hardened.Web.Lambda.Runtime.Impl;
using Hardened.Web.Lambda.Runtime.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Hardened.Web.Lambda.Runtime.DependencyInjection
{
    public static class LambdaWebDI
    {
        public static void Register(IEnvironment environment, IServiceCollection serviceCollection)
        {
            serviceCollection.TryAddSingleton<IApiGatewayEventProcessor, ApiGatewayEventProcessor>();
            serviceCollection.AddSingleton<IMetricLoggerProvider, EmbeddedMetricLoggerProvider>();
            serviceCollection.TryAddSingleton<IDimensionSetProvider, DimensionSetProvider>();
        }
    }
}
