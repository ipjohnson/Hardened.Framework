using DependencyModules.Runtime.Attributes;
using DependencyModules.Runtime.Interfaces;
using Hardened.Amz.Shared.Lambda.Runtime.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Hardened.Amz.Function.Lambda.Runtime.DependencyInjection;

[DependencyModule]
public partial class LambdaFunctionModule : IServiceCollectionConfiguration {
    public void ConfigureServices(IServiceCollection services) {
        services.RemoveAll<ILoggerProvider>();
        services.AddSingleton<ILoggerProvider, LambdaLoggerProvider>();
    }
}
