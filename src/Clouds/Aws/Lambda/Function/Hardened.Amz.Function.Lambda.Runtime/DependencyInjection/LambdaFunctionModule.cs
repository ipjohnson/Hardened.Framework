using DependencyModules.Runtime.Attributes;
using DependencyModules.Runtime.Interfaces;
using Hardened.Amz.Function.Lambda.Runtime.Impl;
using Hardened.Amz.Shared.Lambda.Runtime.Execution;
using Hardened.Amz.Shared.Lambda.Runtime.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Hardened.Amz.Function.Lambda.Runtime.DependencyInjection;

[DependencyModule]
public partial class LambdaFunctionModule : IServiceCollectionConfiguration {
    public void ConfigureServices(IServiceCollection services) {
        services.TryAddSingleton<ILambdaFunctionImplService, LambdaFunctionImplService>();
        services.TryAddSingleton<ILambdaInvokeFilterProvider, LambdaInvokeFilterProvider>();
        services.TryAddSingleton<ILambdaContextAccessor, LambdaContextAccessor>();
        services.RemoveAll<ILoggerProvider>();
        services.AddSingleton<ILoggerProvider, LambdaLoggerProvider>();
    }
}
