using DependencyModules.Runtime.Attributes;
using DependencyModules.Runtime.Interfaces;
using Hardened.Amz.Shared.Lambda.Runtime.Execution;
using Hardened.Amz.Shared.Lambda.Runtime.Logging;
using Hardened.Amz.Web.Lambda.Runtime.Impl;
using Hardened.Amz.Web.Lambda.Runtime.Metrics;
using Hardened.Shared.Runtime.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Hardened.Amz.Web.Lambda.Runtime.DependencyInjection;

[DependencyModule]
public partial class LambdaWebModule : IServiceCollectionConfiguration {
    public void ConfigureServices(IServiceCollection services) {
        services.TryAddSingleton<IApiGatewayEventProcessor, ApiGatewayEventProcessor>();
        services.AddSingleton<IMetricLoggerProvider, EmbeddedMetricLoggerProvider>();
        services.TryAddSingleton<IDimensionSetProvider, DimensionSetProvider>();
        services.TryAddSingleton<ILambdaContextAccessor, LambdaContextAccessor>();
        services.TryAddSingleton<IProxyRequestContextAccessor, ProxyRequestContextAccessor>();
        services.RemoveAll<ILoggerProvider>();
        services.AddSingleton<ILoggerProvider, LambdaLoggerProvider>();
    }
}
