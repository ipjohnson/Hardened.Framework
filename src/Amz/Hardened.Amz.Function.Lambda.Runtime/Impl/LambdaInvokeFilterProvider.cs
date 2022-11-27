using Hardened.Amz.Shared.Lambda.Runtime.Execution;
using Hardened.Requests.Abstract.Execution;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Amz.Function.Lambda.Runtime.Impl;

/// <summary>
/// Provides ILambdaInvokeFilter given a service provider
/// This is intended as an extension point for developers to
/// provider custom implementation of lambda invoke filter
/// </summary>
public interface ILambdaInvokeFilterProvider
{
    IExecutionFilter ProvideFilter(IServiceProvider serviceProvider);
}

public class LambdaInvokeFilterProvider : ILambdaInvokeFilterProvider
{
    public IExecutionFilter ProvideFilter(IServiceProvider serviceProvider)
    {
        return new LambdaFunctionInvokeFilter(
            serviceProvider.GetRequiredService<ILambdaContextAccessor>(),
            serviceProvider.GetServices<ILambdaHandlerPackage>());
    }
}