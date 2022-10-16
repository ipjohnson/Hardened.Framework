using Hardened.Amz.Function.Lambda.Runtime.Impl;
using Hardened.Amz.Shared.Lambda.Runtime.Execution;
using Hardened.Shared.Runtime.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Hardened.Amz.Function.Lambda.Runtime.DependencyInjection;

public static class LambdaFunctionDI
{
    public static void Register(IEnvironment environment, IServiceCollection serviceCollection)
    {
        serviceCollection.TryAddSingleton<ILambdaFunctionImplService, LambdaFunctionImplService>();
        serviceCollection.TryAddSingleton<ILambdaInvokeFilterProvider, LambdaInvokeFilterProvider>();
        serviceCollection.TryAddSingleton<ILambdaContextAccessor, LambdaContextAccessor>();
    }
}